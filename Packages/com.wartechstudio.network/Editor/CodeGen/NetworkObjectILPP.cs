using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using ILPPInterface = Unity.CompilationPipeline.Common.ILPostProcessing.ILPostProcessor;

namespace Unity.WartechStudio.Network.Editor.CodeGen
{
    internal sealed class NetworkObjectILPP : ILPPInterface
    {
        private ModuleDefinition m_MainModule;
        private ModuleDefinition m_UnityModule;
        private ModuleDefinition m_WTNetworkModule;
        private PostProcessorAssemblyResolver m_AssemblyResolver;
        private ICompiledAssembly m_CompiledAssembly;
        private System.Text.StringBuilder builder = null;

        public override ILPPInterface GetInstance() => this;

        private readonly List<DiagnosticMessage> m_Diagnostics = new List<DiagnosticMessage>();
        private List<(MethodDefinition, MethodDefinition)> m_rpcHandlersCopy = new List<(MethodDefinition, MethodDefinition)>();

        public override bool WillProcess(ICompiledAssembly compiledAssembly) => compiledAssembly.Name == "Assembly-CSharp" || compiledAssembly.Name == CodeGenHelpers.RuntimeAssemblyName;
        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {

            if (!WillProcess(compiledAssembly))
            {
                return null;
            }
            m_CompiledAssembly = compiledAssembly;
            m_Diagnostics.Clear();

            // read
            AssemblyDefinition assemblyDefinition = CodeGenHelpers.AssemblyDefinitionFor(compiledAssembly, out m_AssemblyResolver);
            if (assemblyDefinition == null)
            {
                m_Diagnostics.AddError($"Cannot read WartechStudio Network Runtime assembly definition: {compiledAssembly.Name}");
                return null;
            }


            (m_UnityModule, m_WTNetworkModule) = CodeGenHelpers.FindBaseModules(assemblyDefinition, m_AssemblyResolver);

            if (m_UnityModule == null)
            {
                m_Diagnostics.AddError($"Cannot find Unity module: {CodeGenHelpers.UnityModuleName}");
                return null;
            }

            if (m_WTNetworkModule == null)
            {
                m_Diagnostics.AddError($"Cannot find Wartech Studio Network module: {CodeGenHelpers.WTNetworkModuleName}");
                return null;

            }
            // process
            builder = new System.Text.StringBuilder();
            ModuleDefinition mainModule = assemblyDefinition.MainModule;

            if (mainModule != null)
            {
                m_MainModule = mainModule;

                ImportReferences(mainModule);
                foreach (var typeDefinition in mainModule.Types)
                {
                    if (!typeDefinition.IsClass)
                    {
                        continue;
                    }

                    if (typeDefinition.IsSubclassOf(typeof(NetworkObject).FullName) || typeDefinition.Name == nameof(NetworkObject))
                    {
                        ProcessNetworkObject(typeDefinition, compiledAssembly.Defines, assemblyDefinition);
                    }

                }

            }
            else
            {
                m_Diagnostics.AddError($"Cannot get main module from WartechStudio Network Runtime assembly definition: {compiledAssembly.Name}");
            }

            // write
            var pe = new MemoryStream();
            var pdb = new MemoryStream();

            var writerParameters = new WriterParameters
            {
                SymbolWriterProvider = new PortablePdbWriterProvider(),
                SymbolStream = pdb,
                WriteSymbols = true
            };

            assemblyDefinition.Write(pe, writerParameters);
            File.WriteAllText($"D:/{compiledAssembly.Name}.txt", builder.ToString());
            return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), m_Diagnostics);
        }

        private void ProcessNetworkObject(TypeDefinition typeDefinition, string[] assemblyDefines, AssemblyDefinition assemblyDefinition)
        {
            bool isEditorOrDevelopment = assemblyDefines.Contains("UNITY_EDITOR") || assemblyDefines.Contains("DEVELOPMENT_BUILD");
            List<(uint RpcMethodId, MethodDefinition RpcHandler)> rpcHandlers = new List<(uint RpcMethodId, MethodDefinition RpcHandler)>();
            List<(uint RpcMethodId, string RpcMethodName)> rpcNames = new List<(uint RpcMethodId, string RpcMethodName)>();
            Dictionary<uint, CustomAttribute> rpcAttributes = new Dictionary<uint, CustomAttribute>();
            var boolTypeRef = assemblyDefinition.MainModule.ImportReference(typeof(bool));
            List<MethodDefinition> copyMethods = new List<MethodDefinition>();
            List<(MethodDefinition, CustomAttribute)> replicatedOnChangeMethods = new List<(MethodDefinition, CustomAttribute)>();
            Dictionary<string, PropertyDefinition> replicatedProperty = new Dictionary<string, PropertyDefinition>();
            MethodDefinition recvRpcMethod = null;
            MethodDefinition recvReplicatedRpcMethod = null;
            foreach (MethodDefinition methodDefinition in typeDefinition.Methods)
            {
                if (methodDefinition.Name == "RecvRpc")
                {
                    recvRpcMethod = methodDefinition;
                    continue;
                }

                if (methodDefinition.Name == "RecvReplicatedRpc")
                {
                    recvReplicatedRpcMethod = methodDefinition;
                    continue;
                }
                if (!methodDefinition.HasCustomAttributes)
                    continue;

                CustomAttribute rpcAttribute = CheckAndGetReplicatedOnChangeAttribute(methodDefinition);
                if (rpcAttribute != null)
                {
                    replicatedOnChangeMethods.Add((methodDefinition, rpcAttribute));
                    replicatedProperty.Add(rpcAttribute.ConstructorArguments[0].Value.ToString(),null);
                    continue;
                }

                rpcAttribute = CheckAndGetRpcAttribute(methodDefinition);
                if (rpcAttribute != null)
                {
                    uint rpcMethodId = methodDefinition.Hash();
                    if (rpcMethodId == 0)
                    {
                        continue;
                    }
                    rpcHandlers.Add((rpcMethodId, methodDefinition));
                    rpcAttributes.Add(rpcMethodId, rpcAttribute);
                    if (isEditorOrDevelopment)
                    {
                        rpcNames.Add((rpcMethodId, methodDefinition.Name));
                    }
                }
            }
            ILProcessor processor = null;
            List<Instruction> instructions = null;
            foreach ((var Id, MethodDefinition method) in rpcHandlers)
            {
                MethodDefinition copyMethod = GetCopyRpcMethod(method);
                typeDefinition.Methods.Add(copyMethod);
                m_rpcHandlersCopy.Add((method, copyMethod));
                processor = method.Body.GetILProcessor();
                instructions = new List<Instruction>();

                if (rpcAttributes[Id].AttributeType.Name == nameof(ServerRpcAttribute))
                {
                    instructions = GetInstructionsOverrideServerRpcMethod(assemblyDefinition, method);
                }
                else if (rpcAttributes[Id].AttributeType.Name == nameof(ClientRpcAttribute))
                {
                    instructions = GetInstructionsOverrideClientRpcMethod(assemblyDefinition, method);
                }
                else if (rpcAttributes[Id].AttributeType.Name == nameof(BroadcastRpcAttribute))
                {
                    instructions = GetInstructionsOverrideBroadcastRpcMethod(assemblyDefinition, method);
                }

                instructions.Reverse();
                instructions.ForEach(instruction => processor.Body.Instructions.Insert(0, instruction));
            }

            if (recvRpcMethod == null)
            {
                recvRpcMethod = CreateRecvRpcMethod(assemblyDefinition);
                typeDefinition.Methods.Add(recvRpcMethod);
            }
            if (recvRpcMethod != null)
            {
                processor = recvRpcMethod.Body.GetILProcessor();
                
                instructions = GetInstructionsOverrideRecvRpcMethod(assemblyDefinition, recvRpcMethod);
                instructions.Reverse();
                instructions.ForEach(instruction =>
                {
                    processor.Body.Instructions.Insert(m_CompiledAssembly.Name == CodeGenHelpers.RuntimeAssemblyName ? 0 : 3, instruction);
                });
            }

            foreach (PropertyDefinition propertyDefinition in typeDefinition.Properties)
            {
                if (!propertyDefinition.HasCustomAttributes) continue;

                CustomAttribute rpcAttribute = CheckAndGetRpcAttribute(propertyDefinition);

                if(rpcAttribute == null) continue;

                LogMethodToFile(propertyDefinition.SetMethod);
                OverrideReplicatedProperty(assemblyDefinition, typeDefinition,propertyDefinition);
                replicatedProperty[propertyDefinition.Name] = propertyDefinition;
            }

            if (recvReplicatedRpcMethod == null)
            {
                recvReplicatedRpcMethod = CreateRecvReplicatedRpcMethod(assemblyDefinition);
                typeDefinition.Methods.Add(recvReplicatedRpcMethod);
            }

            if (recvReplicatedRpcMethod != null)
            {
                processor = recvReplicatedRpcMethod.Body.GetILProcessor();
                
                instructions = GetInstructionsOverrideRecvReplicatedRpcMethod(assemblyDefinition, recvReplicatedRpcMethod, replicatedOnChangeMethods,replicatedProperty);
                instructions.Reverse();
                instructions.ForEach(instruction =>
                {
                    processor.Body.Instructions.Insert(m_CompiledAssembly.Name == CodeGenHelpers.RuntimeAssemblyName ? 0 : 3, instruction);
                });
                
                LogMethodToFile(recvReplicatedRpcMethod);
                
            }
            assemblyDefinition.MainModule.RemoveRecursiveReferences();
        }

        private CustomAttribute CheckAndGetRpcAttribute(MethodDefinition methodDefinition)
        {
            foreach (var attribute in methodDefinition.CustomAttributes)
            {
                if (attribute.AttributeType.Name == nameof(ServerRpcAttribute) ||
                    attribute.AttributeType.Name == nameof(ClientRpcAttribute) ||
                    attribute.AttributeType.Name == nameof(BroadcastRpcAttribute))
                {
                    return attribute;
                }
            }
            return null;
        }

        private CustomAttribute CheckAndGetRpcAttribute(PropertyDefinition propertyDefinition)
        {
            foreach (var attribute in propertyDefinition.CustomAttributes)
            {
                if (attribute.AttributeType.Name == nameof(ReplicatedAttribute))
                {
                    return attribute;
                }
            }
            return null;
        }

        private CustomAttribute CheckAndGetReplicatedOnChangeAttribute(MethodDefinition methodDefinition)
        {
            foreach (var attribute in methodDefinition.CustomAttributes)
            {
                if (attribute.AttributeType.Name == nameof(ReplicatedOnChangeAttribute))
                {
                    return attribute;
                }
            }
            return null;
        }

        private void OverrideReplicatedProperty(AssemblyDefinition assemblyDefinition,TypeDefinition typeDefinition, PropertyDefinition propertyDefinition)
        {
            FieldDefinition fieldDefinition = new FieldDefinition($"___{propertyDefinition.Name}", FieldAttributes.Private, propertyDefinition.PropertyType);
            typeDefinition.Fields.Add(fieldDefinition);
            OverrideReplicatedPropertyGetMethod(propertyDefinition.GetMethod, fieldDefinition);
            OverrideReplicatedPropertySetMethod(assemblyDefinition, propertyDefinition,propertyDefinition.SetMethod, fieldDefinition);
        }

        private void OverrideReplicatedPropertyGetMethod(MethodDefinition method, FieldDefinition fieldDefinition)
        {
            method.Body.Instructions.Clear();
            ILProcessor processor = method.Body.GetILProcessor();
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldfld, fieldDefinition));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ret));
        }

        private void OverrideReplicatedPropertySetMethod(AssemblyDefinition assemblyDefinition, PropertyDefinition propertyDefinition,MethodDefinition method, FieldDefinition fieldDefinition)
        {
            
            method.Body.Instructions.Clear();
            ILProcessor processor = method.Body.GetILProcessor();
            Instruction returnIns = processor.Create(OpCodes.Ret);
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldarg_1));
            method.Body.Instructions.Add(processor.Create(OpCodes.Stfld, fieldDefinition));
            
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(NetworkObject).GetMethod("get_OwnerId"))));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(NetworkObject).GetMethod("get_ClientId"))));
            method.Body.Instructions.Add(processor.Create(OpCodes.Bne_Un_S, returnIns));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldc_I4, (bool)GetCustomAttributeParameter(propertyDefinition.CustomAttributes[0], "reliable") ? 1:0));
            //method.Body.Instructions.Add(processor.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldstr, propertyDefinition.Name));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldfld, fieldDefinition));
            method.Body.Instructions.Add(processor.Create(OpCodes.Box, fieldDefinition.FieldType));
            method.Body.Instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(NetworkObject).GetMethod("ReplicatedRpc"))));
            method.Body.Instructions.Add(returnIns);
            
        }
        private MethodDefinition CreateRecvReplicatedRpcMethod(AssemblyDefinition assemblyDefinition)
        {
            MethodDefinition method = new MethodDefinition("RecvReplicatedRpc", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig, assemblyDefinition.MainModule.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("rpcReplicated", ParameterAttributes.None, assemblyDefinition.MainModule.ImportReference(typeof(RpcReplicated))));
            ILProcessor processor = method.Body.GetILProcessor();
            //method.Body.Instructions.Add(processor.Create(OpCodes.Nop));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldarg_1));
            method.Body.Instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(NetworkObject).GetMethod(nameof(NetworkObject.RecvReplicatedRpc)))));
            //method.Body.Instructions.Add(processor.Create(OpCodes.Nop));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ret));
            return method;
        }
        private MethodDefinition CreateRecvRpcMethod(AssemblyDefinition assemblyDefinition)
        {
            MethodDefinition method = new MethodDefinition("RecvRpc", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig, assemblyDefinition.MainModule.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("rpcMessage", ParameterAttributes.None, assemblyDefinition.MainModule.ImportReference(typeof(RpcMessage))));
            ILProcessor processor = method.Body.GetILProcessor();
            //method.Body.Instructions.Add(processor.Create(OpCodes.Nop));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ldarg_1));
            method.Body.Instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(NetworkObject).GetMethod(nameof(NetworkObject.RecvRpc)))));
            //method.Body.Instructions.Add(processor.Create(OpCodes.Nop));
            method.Body.Instructions.Add(processor.Create(OpCodes.Ret));
            return method;
        }

        private MethodDefinition GetCopyRpcMethod(MethodDefinition methodDefinition)
        {
            MethodDefinition method = new MethodDefinition($"rpc__{methodDefinition.Name}", MethodAttributes.Public, methodDefinition.Module.TypeSystem.Void);
            foreach (Instruction instruction in methodDefinition.Body.Instructions)
            {
                method.Body.Instructions.Add(instruction);
            }
            foreach (ParameterDefinition parameter in methodDefinition.Parameters)
            {
                method.Parameters.Add(parameter);
            }
            foreach (VariableDefinition variable in methodDefinition.Body.Variables)
            {
                method.Body.Variables.Add(variable);
            }
            return method;
        }

        private MethodReference mr_NetworkManager_Singleton;
        private MethodReference mr_NetworkManager_IsServer;
        private MethodReference mr_NetworkObject_IsAuthorized;
        private MethodReference mr_MethodBase_GetCurrentMethod;
        private MethodReference mr_MemberInfo_Name;
        private void ImportReferences(ModuleDefinition moduleDefinition)
        {
            mr_NetworkManager_Singleton = moduleDefinition.ImportReference(typeof(NetworkManager).GetMethod("get_Singleton"));
            mr_NetworkManager_IsServer = moduleDefinition.ImportReference(typeof(NetworkManager).GetMethod("get_IsServer"));
            mr_NetworkObject_IsAuthorized = moduleDefinition.ImportReference(typeof(NetworkObject).GetMethod("get_IsAuthorized"));
            mr_MethodBase_GetCurrentMethod = moduleDefinition.ImportReference(typeof(System.Reflection.MethodBase).GetMethod("GetCurrentMethod"));
            mr_MemberInfo_Name = moduleDefinition.ImportReference(typeof(System.Reflection.MemberInfo).GetMethod("get_Name"));
        }


        private List<Instruction> GetInstructionsOverrideServerRpcMethod(AssemblyDefinition assemblyDefinition, MethodDefinition methodDefinition)
        {
            ILProcessor processor = methodDefinition.Body.GetILProcessor();
            List<Instruction> instructions = new List<Instruction>();

            Instruction beginInstr = processor.Create(OpCodes.Nop);
            Instruction returnInstr = processor.Create(OpCodes.Ret);
            Instruction serverCheck = processor.Create(OpCodes.Nop);
            Instruction callRpc = processor.Create(OpCodes.Nop);

            // if (!IsAuthorized) -> return;
            instructions.Add(processor.Create(OpCodes.Ldarg_0));
            instructions.Add(processor.Create(OpCodes.Call, mr_NetworkObject_IsAuthorized));
            instructions.Add(processor.Create(OpCodes.Brtrue_S, serverCheck));
            instructions.Add(returnInstr);

            // if (!NetworkManager.Singleton.IsServer) -> callRpc
            instructions.Add(serverCheck);
            instructions.Add(processor.Create(OpCodes.Call, mr_NetworkManager_Singleton));
            instructions.Add(processor.Create(OpCodes.Callvirt, mr_NetworkManager_IsServer));
            instructions.Add(processor.Create(OpCodes.Brtrue_S, beginInstr));

            {
                // ServerRpcFunc(reliable,MethodBase.GetCurrentMethod().Name,...parameters);
                // return;
                instructions.Add(callRpc);
                instructions.Add(processor.Create(OpCodes.Ldarg_0));
                instructions.Add(processor.Create(OpCodes.Ldc_I4, (bool)GetCustomAttributeParameter(methodDefinition.CustomAttributes[0], "reliable") ? 1 : 0));
                //instructions.Add(processor.Create(OpCodes.Ldarg_0));
                instructions.Add(processor.Create(OpCodes.Call, mr_MethodBase_GetCurrentMethod));
                instructions.Add(processor.Create(OpCodes.Callvirt, mr_MemberInfo_Name));
                instructions.Add(processor.Create(OpCodes.Ldc_I4, methodDefinition.Parameters.Count));
                instructions.Add(processor.Create(OpCodes.Newarr, assemblyDefinition.MainModule.TypeSystem.Object));
                for (int i = 0; i < methodDefinition.Parameters.Count; ++i)
                {
                    instructions.Add(processor.Create(OpCodes.Dup));
                    instructions.Add(processor.Create(OpCodes.Ldc_I4, i));
                    instructions.Add(processor.Create(OpCodes.Ldarg, i + 1));
                    if (methodDefinition.Parameters[i].ParameterType.IsValueType)
                        instructions.Add(processor.Create(OpCodes.Box, methodDefinition.Parameters[i].ParameterType));
                    instructions.Add(processor.Create(OpCodes.Stelem_Ref));
                }
                instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(NetworkObject).GetMethod(nameof(NetworkObject.ServerRpcFunc)))));
                instructions.Add(returnInstr);
            }
            instructions.Add(beginInstr);
            return instructions;
        }


        private List<Instruction> GetInstructionsOverrideClientRpcMethod(AssemblyDefinition assemblyDefinition, MethodDefinition methodDefinition)
        {
            ILProcessor processor = methodDefinition.Body.GetILProcessor();

            List<Instruction> instructions = new List<Instruction>();

            Instruction beginInstr = processor.Create(OpCodes.Nop);
            Instruction returnInstr = processor.Create(OpCodes.Ret);
            Instruction callRpc = processor.Create(OpCodes.Nop);

            // if (!NetworkManager.Singleton.IsServer) -> return;
            instructions.Add(processor.Create(OpCodes.Call, mr_NetworkManager_Singleton));
            instructions.Add(processor.Create(OpCodes.Callvirt, mr_NetworkManager_IsServer));
            instructions.Add(processor.Create(OpCodes.Brtrue_S, callRpc));
            instructions.Add(returnInstr);


            // ClientRpcFunc(MethodBase.GetCurrentMethod().Name, ...);
            // return;
            instructions.Add(callRpc);
            instructions.Add(processor.Create(OpCodes.Ldarg_0));
            instructions.Add(processor.Create(OpCodes.Ldc_I4, (bool)GetCustomAttributeParameter(methodDefinition.CustomAttributes[0], "reliable") ? 1 : 0));
            //instructions.Add(processor.Create(OpCodes.Ldarg_0));
            instructions.Add(processor.Create(OpCodes.Call, mr_MethodBase_GetCurrentMethod));
            instructions.Add(processor.Create(OpCodes.Callvirt, mr_MemberInfo_Name));
            instructions.Add(processor.Create(OpCodes.Ldc_I4, methodDefinition.Parameters.Count));
            instructions.Add(processor.Create(OpCodes.Newarr, assemblyDefinition.MainModule.TypeSystem.Object));
            for (int i = 0; i < methodDefinition.Parameters.Count; ++i)
            {
                instructions.Add(processor.Create(OpCodes.Dup));
                instructions.Add(processor.Create(OpCodes.Ldc_I4, i));
                instructions.Add(processor.Create(OpCodes.Ldarg, i + 1));
                if (methodDefinition.Parameters[i].ParameterType.IsValueType)
                    instructions.Add(processor.Create(OpCodes.Box, methodDefinition.Parameters[i].ParameterType));
                instructions.Add(processor.Create(OpCodes.Stelem_Ref));
            }
            instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(NetworkObject).GetMethod(nameof(NetworkObject.ClientRpcFunc)))));
            instructions.Add(returnInstr);
            instructions.Add(beginInstr);
            return instructions;
        }

        private List<Instruction> GetInstructionsOverrideBroadcastRpcMethod(AssemblyDefinition assemblyDefinition, MethodDefinition methodDefinition)
        {
            ILProcessor processor = methodDefinition.Body.GetILProcessor();
            List<Instruction> instructions = new List<Instruction>();

            Instruction beginInstr = processor.Create(OpCodes.Nop);
            Instruction returnInstr = methodDefinition.Body.Instructions[methodDefinition.Body.Instructions.Count - 1];
            Instruction callRpc = processor.Create(OpCodes.Nop);

            // if (!IsAuthorized) -> return;
            instructions.Add(processor.Create(OpCodes.Ldarg_0));
            instructions.Add(processor.Create(OpCodes.Call, mr_NetworkObject_IsAuthorized));
            instructions.Add(processor.Create(OpCodes.Brtrue_S, callRpc));
            instructions.Add(returnInstr);

            // BroadcastRpcFunc(MethodBase.GetCurrentMethod().Name,...);
            instructions.Add(callRpc);
            instructions.Add(processor.Create(OpCodes.Ldarg_0));
            instructions.Add(processor.Create(OpCodes.Ldc_I4, (bool)GetCustomAttributeParameter(methodDefinition.CustomAttributes[0], "reliable") ? 1 : 0));
            //instructions.Add(processor.Create(OpCodes.Ldarg_0));
            instructions.Add(processor.Create(OpCodes.Call, mr_MethodBase_GetCurrentMethod));
            instructions.Add(processor.Create(OpCodes.Callvirt, mr_MemberInfo_Name));
            instructions.Add(processor.Create(OpCodes.Ldc_I4, methodDefinition.Parameters.Count));
            instructions.Add(processor.Create(OpCodes.Newarr, assemblyDefinition.MainModule.TypeSystem.Object));
            for (int i = 0; i < methodDefinition.Parameters.Count; ++i)
            {
                instructions.Add(processor.Create(OpCodes.Dup));
                instructions.Add(processor.Create(OpCodes.Ldc_I4, i));
                instructions.Add(processor.Create(OpCodes.Ldarg, i + 1));
                if(methodDefinition.Parameters[i].ParameterType.IsValueType)
                    instructions.Add(processor.Create(OpCodes.Box, methodDefinition.Parameters[i].ParameterType));
                instructions.Add(processor.Create(OpCodes.Stelem_Ref));
            }
            instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(NetworkObject).GetMethod(nameof(NetworkObject.BroadcastRpcFunc)))));
            instructions.Add(beginInstr);

            return instructions;
        }

        private List<Instruction> GetInstructionsOverrideRecvRpcMethod(AssemblyDefinition assemblyDefinition, MethodDefinition methodDefinition)
        {
            ILProcessor processor = methodDefinition.Body.GetILProcessor();

            List<Instruction> instructions = new List<Instruction>();

            Instruction returnInstr = methodDefinition.Body.Instructions[methodDefinition.Body.Instructions.Count - 1];
            Instruction nextRpc = processor.Create(OpCodes.Ldarga_S, methodDefinition.Parameters[0]);
            int count = 0;
            foreach ((MethodDefinition baseMethod, MethodDefinition copyMethod) in m_rpcHandlersCopy)
            {
                // if(rpcMessage.FuncName == "(baseMethod.Name)"){exec_copy_func;return;}
                //if (!(count == 0 && m_CompiledAssembly.Name != CodeGenHelpers.RuntimeAssemblyName))
                //{
                 instructions.Add(nextRpc);
                 nextRpc = processor.Create(OpCodes.Ldarga_S, methodDefinition.Parameters[0]);
                //}
                //instructions.Add(processor.Create(OpCodes.Ldarga_S, methodDefinition.Parameters[0]));
                instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(RpcMessage).GetMethod("get_FuncName"))));
                instructions.Add(processor.Create(OpCodes.Ldstr, baseMethod.Name));
                instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(string).GetMethod("op_Equality"))));
                if(count == m_rpcHandlersCopy.Count - 1)
                    instructions.Add(processor.Create(OpCodes.Brfalse_S, returnInstr));
                else
                    instructions.Add(processor.Create(OpCodes.Brfalse_S, nextRpc));
                //instructions.Add(processor.Create(OpCodes.Nop));
                instructions.Add(processor.Create(OpCodes.Ldarg_0));
                for (int i = 0; i < baseMethod.Parameters.Count; ++i)
                {
                    instructions.Add(processor.Create(OpCodes.Ldarga_S, methodDefinition.Parameters[0]));
                    instructions.Add(processor.Create(OpCodes.Ldc_I4, i));
                    instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(RpcMessage)
                        .GetMethod(nameof(RpcMessage.GetParameter))//)));
                        .MakeGenericMethod(GetType(baseMethod.Parameters[i].ParameterType)))));
                }
                instructions.Add(processor.Create(OpCodes.Call, copyMethod));
                //instructions.Add(processor.Create(OpCodes.Nop));
                instructions.Add(processor.Create(OpCodes.Ret));
                count++;
            }
            return instructions;
        }
        
        private List<Instruction> GetInstructionsOverrideRecvReplicatedRpcMethod(AssemblyDefinition assemblyDefinition, MethodDefinition methodDefinition,List<(MethodDefinition, CustomAttribute)> recvReplicatedRpcMethods, Dictionary<string, PropertyDefinition> replicatedProperty)
        {
            ILProcessor processor = methodDefinition.Body.GetILProcessor();

            List<Instruction> instructions = new List<Instruction>();

            Instruction returnInstr = methodDefinition.Body.Instructions[methodDefinition.Body.Instructions.Count - 1];
            Instruction nextRpc = processor.Create(OpCodes.Ldarga_S, methodDefinition.Parameters[0]);
            int count = 0;
            foreach ((MethodDefinition repMethod, CustomAttribute customAttribute) in recvReplicatedRpcMethods)
            {
                object attributePropertyName = GetCustomAttributeParameter(customAttribute, "propertyName");
                //if(rpcMessage.FuncName == "(baseMethod.Name)"){exec_copy_func;return;}
                //if (!(count == 0 && m_CompiledAssembly.Name != CodeGenHelpers.RuntimeAssemblyName))
                //{
                instructions.Add(nextRpc);
                nextRpc = processor.Create(OpCodes.Ldarga_S, methodDefinition.Parameters[0]);
                //}
                //instructions.Add(processor.Create(OpCodes.Ldarga_S, methodDefinition.Parameters[0]));
                instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(RpcReplicated).GetMethod("get_PropertyName"))));
                instructions.Add(processor.Create(OpCodes.Ldstr, attributePropertyName.ToString()));
                instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(string).GetMethod("op_Equality"))));
                if (count == recvReplicatedRpcMethods.Count - 1)
                    instructions.Add(processor.Create(OpCodes.Brfalse_S, returnInstr));
                else
                    instructions.Add(processor.Create(OpCodes.Brfalse_S, nextRpc));
                //instructions.Add(processor.Create(OpCodes.Nop));
                instructions.Add(processor.Create(OpCodes.Ldarg_0));
                instructions.Add(processor.Create(OpCodes.Ldarga_S, methodDefinition.Parameters[0]));
                instructions.Add(processor.Create(OpCodes.Call, assemblyDefinition.MainModule.ImportReference(typeof(RpcReplicated)
                    .GetMethod(nameof(RpcReplicated.GetValue))
                    .MakeGenericMethod(GetType(replicatedProperty[attributePropertyName.ToString()].PropertyType)))));
                instructions.Add(processor.Create(OpCodes.Call, replicatedProperty[attributePropertyName.ToString()].SetMethod));
                instructions.Add(processor.Create(OpCodes.Ldarg_0));
                instructions.Add(processor.Create(OpCodes.Call, repMethod));
                //instructions.Add(processor.Create(OpCodes.Nop));
                instructions.Add(processor.Create(OpCodes.Ret));
                count++;
            }
            return instructions;
        }

        Type GetType(TypeReference typeReference)
        {
            Type type = Type.GetType(typeReference.FullName);

            string typeFullName = typeReference.FullName;

            if (type == null)
            {
                string assemblyName = m_UnityModule.Assembly.FullName;
                string typeassembly = $"{typeFullName},{assemblyName}";
                type = Type.GetType(typeassembly);
            }

            if (type == null)
            {
                string assemblyName = m_WTNetworkModule.Assembly.FullName;
                string typeassembly = $"{typeFullName},{assemblyName}";
                type = Type.GetType(typeassembly);

            }

            if (type == null)
            {
                string assemblyName = m_MainModule.Assembly.FullName;
                string typeassembly = $"{typeFullName},{assemblyName}";
                type = Type.GetType(typeassembly);
            }
            /*
            if (type == null)
            {
                builder.AppendLine(typeFullName);
            }
            */
            return type;
        }

        object GetCustomAttributeParameter(CustomAttribute customAttribute,string parameterName)
        {
            for (int i = 0; i < customAttribute.Constructor.Resolve().Parameters.Count; i++)
            {
                ParameterDefinition parameterDefinition = customAttribute.Constructor.Resolve().Parameters[i];
                if (parameterDefinition.Name == parameterName)
                {
                    return customAttribute.ConstructorArguments[i].Value;
                }
                
            }
            return null;
        }

        private void LogMethodToFile(MethodDefinition methodDefinition)
        {
            builder.AppendLine($"{methodDefinition.FullName}");

            foreach(var i in methodDefinition.Body.Instructions)
            {
                builder.AppendLine(i.ToString());
            }
        }
    }
}
