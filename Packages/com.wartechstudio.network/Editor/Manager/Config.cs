using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.WartechStudio.Network.Editor.Manager
{
    public class OptionsWindow : EditorWindow
    {
        private static OptionsWindow m_Window;

        private static bool m_ShowDebugLog;

        [MenuItem("WartechStudio/Network/Options",false,1)]
        static void Init()
        {
            m_Window = (OptionsWindow)EditorWindow.GetWindow(typeof(OptionsWindow));
            m_Window.Show();
        }

        void OnGUI()
        {
            GUILayout.Label("WartechStudio Network Options", EditorStyles.boldLabel);
            GUILayout.Label("", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Show Editor Debug Log");
            m_ShowDebugLog = GUILayout.Toggle(m_ShowDebugLog, "");
            GUILayout.EndHorizontal();
        }
    }
}
