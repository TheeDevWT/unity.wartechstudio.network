using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Unity.WartechStudio.Network.Editor.CodeGen
{
    internal static class CodeGenHelpers
    {
        public const string UnityModuleName = "UnityEngine.CoreModule.dll";
        public const string WTNetworkModuleName = "Unity.WartechStudio.Network.Runtime.dll";

        public const string RuntimeAssemblyName = "Unity.WartechStudio.Network.Runtime";

        public static uint Hash(this MethodDefinition methodDefinition)
        {
            var sigArr = Encoding.UTF8.GetBytes($"{methodDefinition.Module.Name} / {methodDefinition.FullName}");
            var sigLen = sigArr.Length;
            unsafe
            {
                fixed (byte* sigPtr = sigArr)
                {
                    return XXHash.Hash32(sigPtr, sigLen);
                }
            }
        }

        public static bool IsSubclassOf(this TypeDefinition typeDefinition, string classTypeFullName)
        {
            if (!typeDefinition.IsClass)
            {
                return false;
            }

            var baseTypeRef = typeDefinition.BaseType;
            while (baseTypeRef != null)
            {
                if (baseTypeRef.FullName == classTypeFullName)
                {
                    return true;
                }

                try
                {
                    baseTypeRef = baseTypeRef.Resolve().BaseType;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        public static void AddError(this List<DiagnosticMessage> diagnostics, string message)
        {
            diagnostics.AddError((SequencePoint)null, message);
        }

        public static void AddError(this List<DiagnosticMessage> diagnostics, MethodDefinition methodDefinition, string message)
        {
            diagnostics.AddError(methodDefinition.DebugInformation.SequencePoints.FirstOrDefault(), message);
        }

        public static void AddError(this List<DiagnosticMessage> diagnostics, SequencePoint sequencePoint, string message)
        {
            diagnostics.Add(new DiagnosticMessage
            {
                DiagnosticType = DiagnosticType.Error,
                File = sequencePoint?.Document.Url.Replace($"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}", ""),
                Line = sequencePoint?.StartLine ?? 0,
                Column = sequencePoint?.StartColumn ?? 0,
                MessageData = $" - {message}"
            });
        }

        public static void AddWarning(this List<DiagnosticMessage> diagnostics, string message)
        {
            diagnostics.AddWarning((SequencePoint)null, message);
        }

        public static void AddWarning(this List<DiagnosticMessage> diagnostics, MethodDefinition methodDefinition, string message)
        {
            diagnostics.AddWarning(methodDefinition.DebugInformation.SequencePoints.FirstOrDefault(), message);
        }

        public static void AddWarning(this List<DiagnosticMessage> diagnostics, SequencePoint sequencePoint, string message)
        {
            diagnostics.Add(new DiagnosticMessage
            {
                DiagnosticType = DiagnosticType.Warning,
                File = sequencePoint?.Document.Url.Replace($"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}", ""),
                Line = sequencePoint?.StartLine ?? 0,
                Column = sequencePoint?.StartColumn ?? 0,
                MessageData = $" - {message}"
            });
        }
        
        public static void RemoveRecursiveReferences(this ModuleDefinition moduleDefinition)
        {
            // Weird behavior from Cecil: When importing a reference to a specific implementation of a generic
            // method, it's importing the main module as a reference into itself. This causes Unity to have issues
            // when attempting to iterate the assemblies to discover unit tests, as it goes into infinite recursion
            // and eventually hits a stack overflow. I wasn't able to find any way to stop Cecil from importing the module
            // into itself, so at the end of it all, we're just going to go back and remove it again.
            var moduleName = moduleDefinition.Name;
            if (moduleName.EndsWith(".dll") || moduleName.EndsWith(".exe"))
            {
                moduleName = moduleName.Substring(0, moduleName.Length - 4);
            }

            foreach (var reference in moduleDefinition.AssemblyReferences)
            {
                var referenceName = reference.Name.Split(',')[0];
                if (referenceName.EndsWith(".dll") || referenceName.EndsWith(".exe"))
                {
                    referenceName = referenceName.Substring(0, referenceName.Length - 4);
                }

                if (moduleName == referenceName)
                {
                    try
                    {
                        moduleDefinition.AssemblyReferences.Remove(reference);
                        break;
                    }
                    catch (Exception)
                    {
                        //
                    }
                }
            }
        }

        public static AssemblyDefinition AssemblyDefinitionFor(ICompiledAssembly compiledAssembly, out PostProcessorAssemblyResolver assemblyResolver)
        {
            assemblyResolver = new PostProcessorAssemblyResolver(compiledAssembly);
            var readerParameters = new ReaderParameters
            {
                SymbolStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData),
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                AssemblyResolver = assemblyResolver,
                ReflectionImporterProvider = new PostProcessorReflectionImporterProvider(),
                ReadingMode = ReadingMode.Immediate
            };

            var assemblyDefinition = AssemblyDefinition.ReadAssembly(new MemoryStream(compiledAssembly.InMemoryAssembly.PeData), readerParameters);

            //apparently, it will happen that when we ask to resolve a type that lives inside Unity.WartechStudio.Network.Runtime, and we
            //are also postprocessing Unity.WartechStudio.Network.Runtime, type resolving will fail, because we do not actually try to resolve
            //inside the assembly we are processing. Let's make sure we do that, so that we can use postprocessor features inside
            //Unity.Netcode.Runtime itself as well.
            assemblyResolver.AddAssemblyDefinitionBeingOperatedOn(assemblyDefinition);

            return assemblyDefinition;
        }
        

        private static void SearchForBaseModulesRecursive(AssemblyDefinition assemblyDefinition, PostProcessorAssemblyResolver assemblyResolver, ref ModuleDefinition unityModule, ref ModuleDefinition wtNetworkModule, HashSet<string> visited)
        {

            foreach (var module in assemblyDefinition.Modules)
            {
                if (module == null)
                {
                    continue;
                }

                if (unityModule != null && wtNetworkModule != null)
                {
                    return;
                }

                if (unityModule == null && module.Name == UnityModuleName)
                {
                    unityModule = module;
                    continue;
                }

                if (wtNetworkModule == null && module.Name == WTNetworkModuleName)
                {
                    wtNetworkModule = module;
                    continue;
                }
            }
            if (unityModule != null && wtNetworkModule != null)
            {
                return;
            }

            foreach (var assemblyNameReference in assemblyDefinition.MainModule.AssemblyReferences)
            {
                if (assemblyNameReference == null)
                {
                    continue;
                }
                if (visited.Contains(assemblyNameReference.Name))
                {
                    continue;
                }

                visited.Add(assemblyNameReference.Name);

                var assembly = assemblyResolver.Resolve(assemblyNameReference);
                if (assembly == null)
                {
                    continue;
                }
                SearchForBaseModulesRecursive(assembly, assemblyResolver, ref unityModule, ref wtNetworkModule, visited);

                if (unityModule != null && wtNetworkModule != null)
                {
                    return;
                }
            }
        }
        public static (ModuleDefinition UnityModule, ModuleDefinition WTNetworkModule) FindBaseModules(AssemblyDefinition assemblyDefinition, PostProcessorAssemblyResolver assemblyResolver)
        {
            ModuleDefinition unityModule = null;
            ModuleDefinition wtNetworkModule = null;
            var visited = new HashSet<string>();
            SearchForBaseModulesRecursive(assemblyDefinition, assemblyResolver, ref unityModule, ref wtNetworkModule, visited);

            return (unityModule, wtNetworkModule);
        }
    }
}