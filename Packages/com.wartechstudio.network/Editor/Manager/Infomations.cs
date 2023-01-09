using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Unity.WartechStudio.Network.Editor.Manager
{
    public static class InformationWindow
    {
        [MenuItem("WartechStudio/Network/Information", false, 3)]
        public static void PrintAssemblyNames()
        {
            UnityEngine.Debug.Log("== Player Assemblies ==");
            Assembly[] playerAssemblies =
                CompilationPipeline.GetAssemblies(AssembliesType.Player);
            /*
            foreach (var assembly in playerAssemblies)
            {
                if (assembly.name == "Unity.WartechStudio.Network.Runtime")
                    foreach (var refAss in assembly.)
                    {
                        Debug.Log(refAss);
                    }
                        
            }
            */
        }
    }
}
