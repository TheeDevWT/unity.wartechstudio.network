using UnityEngine;
using Unity.WartechStudio.Network;

namespace WartechStudio.BattleMonster
{
    public class World : MonoBehaviour
    {
        static public World Singleton;

        public GameModeBase GameModePrefab;

        [HideInInspector]
        public GameModeBase GameMode;

        public bool IsNetworkInitialized => NetworkManager.Singleton != null && NetworkManager.Singleton.SessionId != null && NetworkManager.Singleton.SessionId != 0;

        private void Awake()
        {
            Singleton = this;
        }

        private void Start()
        {
            // no init
            if(!IsNetworkInitialized)
            {
                NetworkManager.Singleton.OnStartedDelegate = () =>
                {
                    StartWorld();
                };

                return;
            }

            StartWorld();
        }

        void StartWorld()
        {
            if (!NetworkManager.Singleton.IsServer)
                return;

            GameMode = WorldSpawnObject<GameModeBase>(GameModePrefab);
        }

        public T WorldSpawnObject<T>(NetworkObject prefab, ulong ownerId = 0)
        {
            return WorldSpawnObject<T>(prefab,Vector3.zero,Quaternion.identity, ownerId);
        }

        public T WorldSpawnObject<T>(NetworkObject prefab, Vector3 position,Quaternion rotation, ulong ownerId = 0)
        {
            if(IsNetworkInitialized)
                return NetworkManager.Singleton.Spawn<T>(prefab, position, rotation,ownerId);

            return Instantiate(prefab).GetComponent<T>();
        }
    }
}
