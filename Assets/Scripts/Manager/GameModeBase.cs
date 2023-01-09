using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Unity.WartechStudio.Network;

namespace WartechStudio.BattleMonster
{
    public class GameModeBase : NetworkObject
    {
        public PlayerController PlayerControllerPrefab;

        private Dictionary<ulong, PlayerController> m_Players = new Dictionary<ulong, PlayerController>();

        private void Start()
        {
            if (!NetworkManager.Singleton.IsServer) return;

            NetworkManager.Singleton.OnConnectedDelegate += PreLogin;
            foreach(ulong playerId in NetworkManager.Singleton.ClientIds)
            {
                PreLogin(playerId);
            }
        }

        void PreLogin(ulong playerId)
        {
            if(m_Players.ContainsKey(playerId))
            {
                Debug.LogError("client can't duplicate login.");
                return;
            }
            PlayerController playerController = NetworkManager.Singleton.Spawn<PlayerController>(PlayerControllerPrefab, playerId);
            playerController.OnNetworkSpawnedDelegate += (obj) =>
            {
                OnLogin((PlayerController)obj);
            };
        }

        virtual protected void OnLogin(PlayerController playerController)
        {
            playerController.OnDestroyedDelegate += () =>
            {
                OnLogout(playerController.PlayerId);
            };
            m_Players.Add(playerController.PlayerId, playerController);
            playerController.OnLogin();
            Debug.Log($"player id {playerController.PlayerId} is logged in.");
        }


        virtual protected void OnLogout(ulong playerId)
        {
            m_Players.Remove(playerId);
        }

        public PlayerStartPoint GetNextStartPoint(bool random = false)
        {
            List<PlayerStartPoint> emptyStartPonits = new List<PlayerStartPoint>();
            PlayerStartPoint[] startpoints = FindObjectsOfType<PlayerStartPoint>();
            foreach (PlayerStartPoint ps in startpoints)
            {
                if (ps.IsEmpty)
                    emptyStartPonits.Add(ps);
            }
            emptyStartPonits = emptyStartPonits.OrderBy(a => a.Index).ToList();
            if (emptyStartPonits.Count == 0)
                return null;
            if (!random)
                return emptyStartPonits[0];
            return emptyStartPonits[ Random.Range(0, emptyStartPonits.Count)];
        }
    }
}

