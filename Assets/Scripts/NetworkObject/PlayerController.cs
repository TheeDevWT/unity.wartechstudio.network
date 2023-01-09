using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.WartechStudio.Network;

namespace WartechStudio.BattleMonster
{
    public class PlayerController : NetworkObject
    {
        public NetworkObject PlayerPrefab;

        public ulong PlayerId => OwnerId;

        public override void OnNetworkSpawned()
        {
            base.OnNetworkSpawned();

            if (IsOwner)
            {
                BroadcastSetAuthorizedFlags(EAuthorizedFlags.Owner);
                if(!IsServer)
                    OnLogin();
            }
        }

        public void OnLogin()
        {
            if (IsOwner)
                ServerCreatePlayer(PlayerId);
        }

        [ServerRpc]
        public void ServerCreatePlayer(ulong playerId)
        {
            PlayerStartPoint startPoint = World.Singleton.GameMode.GetNextStartPoint(true);
            World.Singleton.WorldSpawnObject<NetworkObject>(PlayerPrefab,startPoint.transform.position, startPoint.transform.rotation, playerId);
            startPoint.IsEmpty = false;
        }

        [ClientRpc]
        public void Expelled(string message)
        {
            Debug.LogWarning(message);
            NetworkManager.Singleton.Despawn(this);
        }

    }
}
