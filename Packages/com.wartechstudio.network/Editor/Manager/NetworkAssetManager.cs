using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.WartechStudio.Network.Editor
{
    public class NetworkAssetManagerWindow : EditorWindow
    {
        private static NetworkAssetManagerWindow m_Window;

        private static List<NetworkObjectJson> m_NetworkAssets = null;

        [MenuItem("WartechStudio/Network/NetworkAssetManager", false, 2)]
        static void Init()
        {
            m_Window = (NetworkAssetManagerWindow)EditorWindow.GetWindow(typeof(NetworkAssetManagerWindow));
            m_Window.Show();
        }

        void OnGUI()
        {
            GUILayout.Label("Network Asset Manager", EditorStyles.boldLabel);

            DrawAssetPathFromResourcesFolderEditor();

            if (GUILayout.Button("Fetch")) FetchNetworkAsset();
            if (GUILayout.Button("Save")) Save();
            if (GUILayout.Button("Load")) Load();
        }

        void DrawAssetPathFromResourcesFolderEditor()
        {
            GUILayout.Label("");
            GUILayout.Label("Asset List (path(\\Resources\\Prefabs\\Network))");
            GUILayout.BeginScrollView(new Vector2(0, 0), false, true, GUILayout.Width(position.width - 10), GUILayout.Height(position.height - 150));
            if(m_NetworkAssets != null)
                foreach (var item in m_NetworkAssets)
                {
                    GUILayout.Label(item.Path);
                }
            GUILayout.EndScrollView();
        }

        void FetchNetworkAsset()
        {
            m_NetworkAssets = new List<NetworkObjectJson>();
            DirectoryInfo resourceDir = new DirectoryInfo($"{Application.dataPath}/Resources/Prefabs/Network");
            if (!resourceDir.Exists)
                return;
            FileInfo[] files = resourceDir.GetFiles("*.prefab", SearchOption.AllDirectories);
            foreach (FileInfo fileInfo in files)
            {
                string filePath = $"{fileInfo.Directory}/{fileInfo.Name}";
                filePath = filePath.Split("Prefabs\\Network")[1];
                filePath = filePath.Replace(".prefab", "");
                m_NetworkAssets.Add(new NetworkObjectJson(fileInfo.Name.Replace(".prefab", ""), $"Prefabs/Network{filePath}"));
            }
        }

        async void Save()
        {
            if (m_NetworkAssets == null)
            {
                Debug.Log($"please fetch before save.");
                return;
            }
            DirectoryInfo resourceDir = new DirectoryInfo($"{Application.dataPath}/Resources/Data/NetworkObjects.json");
            NetworkObjectJsonList networkObjectJsonList = new NetworkObjectJsonList(m_NetworkAssets.ToArray());
            await File.WriteAllTextAsync(resourceDir.ToString(), JsonUtility.ToJson(networkObjectJsonList));
            Debug.Log($"network object count = {m_NetworkAssets.Count} \nsave success");
        }
        public void Load()
        {
            TextAsset targetFile = Resources.Load<TextAsset>("Data/NetworkObjects");
            if (targetFile.ToString() == "")
            {
                Debug.Log($"NetworkObjects is empty.");
                return;
            }
            NetworkObjectJsonList networkObjectJsonList = JsonUtility.FromJson<NetworkObjectJsonList>(targetFile.ToString());
            m_NetworkAssets = new List<NetworkObjectJson>(networkObjectJsonList.Objects);
            Debug.Log($"network object count = {m_NetworkAssets.Count} \nload success");
        }
    }

}
