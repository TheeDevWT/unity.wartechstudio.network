using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.WartechStudio.Network
{
    public class NetworkAssetManager : MonoBehaviour
    {
        public static NetworkAssetManager Singalton;

        private static List<GameObject> m_NetworkObjects;

        NetworkAssetManager()
        {
            Singalton = this;
        }

        public static void AddNetworkObjects(GameObject networkObject)
        {
            m_NetworkObjects.Add(networkObject);
        }
    }
}
