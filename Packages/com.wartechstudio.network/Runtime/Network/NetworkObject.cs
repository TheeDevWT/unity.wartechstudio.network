using UnityEngine;
using System;
using System.Collections;

namespace Unity.WartechStudio.Network
{
    public class NetworkObject : NetworkBehaviour
    {
        /// <summary>
        /// unique id of object. id will be same a duplicate object on network.
        /// 999 for fix check IsOwner before seting up.
        /// </summary>
        public ulong ObjectId { get; private set; } = 999;

        /// <summary>
        /// id of client of object.
        ///  888 for fix check IsOwner before seting up.
        /// </summary>
        public ulong OwnerId { get; private set; } = 888;

        /// <summary>
        /// unique client id but not id of client of object.
        /// </summary>
        public ulong ClientId;

        public bool IsServer;
        public bool IsOwner => ClientId == OwnerId;
        public bool IsAuthorized { get; private set; } = true;

        public Action<NetworkObject> OnNetworkSpawnedDelegate;
        public Action OnDestroyedDelegate;

        public EAuthorizedFlags AuthorizedFlags { get; private set; } = EAuthorizedFlags.Server;

        virtual public void OnNetworkSpawned() 
        { 
            OnNetworkSpawnedDelegate?.Invoke(this);
        }
        virtual public void OnAuthorizedUpdate() { }
        virtual public void OnSpawned() { }
        virtual public void OnDestroyed() 
        { 
            OnDestroyedDelegate?.Invoke(); 
        }

        virtual protected void Update()
        {
            //UpdateReplicateTransform(Time.deltaTime);
        }

        public void Initialized(RpcObject rpcObject)
        {
            ClientId = NetworkManager.Singleton.ClientId;
            IsServer = NetworkManager.Singleton.IsServer;
            ObjectId = rpcObject.ObjectId;
            OwnerId = rpcObject.OwnerId;
            AuthorizedFlags = rpcObject.AuthorizedFlag;
            FetchAuthorized();
            transformRepicateds = FindObjectsOfType<NetworkTransformRepicated>();
            foreach (NetworkTransformRepicated transformRepicated in transformRepicateds)
            {
                transformRepicated.Init();
            }
        }

        [BroadcastRpc(true)]
        public void BroadcastRpcNetworkSpawned()
        {
            OnNetworkSpawned();
        }

        [BroadcastRpc(true)]
        public void BroadcastRpcNetworkDespawned()
        {
            OnDestroyed();
            Destroy(this);
        }

        #region Authorized

        [BroadcastRpc(true)]
        public void BroadcastSetAuthorizedFlags(EAuthorizedFlags authorizedFlags)
        {
            SetAuthorizedFlags(authorizedFlags);
        }

        public void SetOwner(ulong clientId)
        {
            OwnerId = clientId;
            FetchAuthorized();
        }
        public void SetAuthorizedFlags(EAuthorizedFlags authorizedFlags)
        {
            AuthorizedFlags = authorizedFlags;
            FetchAuthorized();
        }

        private void FetchAuthorized()
        {
            IsAuthorized = false;

            if (NetworkManager.Singleton.ClientId == OwnerId && AuthorizedFlags.HasFlag(EAuthorizedFlags.Owner))
            {
                IsAuthorized = true;
            }

            if(NetworkManager.Singleton.IsServer && AuthorizedFlags.HasFlag(EAuthorizedFlags.Server))
            {
                IsAuthorized = true;
            }

            if(!NetworkManager.Singleton.IsServer && AuthorizedFlags.HasFlag(EAuthorizedFlags.Client))
            {
                IsAuthorized = true;
            }

            if (!IsAuthorized)
                gameObject.name += " (simulate)";
            else
                gameObject.name = gameObject.name.Replace(" (simulate)", "");

            OnAuthorizedUpdate();
        }
        #endregion Authorized

        #region Core

        /// <summary>
        /// Call by client of object owner to server and execute on server.
        /// </summary>
        public void ServerRpcFunc(bool reliable,string funcName,params dynamic[] parameters)
        {
            NetworkManager.Singleton.RequestSendServerRpc(reliable,ObjectId, funcName, parameters);
        }

        /// <summary>
        /// Call by server to client of object owner and execute on that client.
        /// </summary>
        public void ClientRpcFunc(bool reliable, string funcName, params dynamic[] parameters)
        {
            NetworkManager.Singleton.RequestSendClientRpc(reliable,OwnerId, ObjectId, funcName, parameters);
        }

        /// <summary>
        /// Call by authorized of object to all client(include server) and execute on that;
        /// </summary>
        public void BroadcastRpcFunc(bool reliable, string funcName,params dynamic[] parameters)
        {
            NetworkManager.Singleton.RequestSendBroadcastRpc(reliable,ObjectId, funcName, parameters);
        }

        /// <summary>
        /// Call by authorized of object to all client(include server) and set value on that;
        /// </summary>
        public void ReplicatedRpc(bool reliable, string propertyName, dynamic value)
        {
            NetworkManager.Singleton.RequestSendReplicatedRpc(reliable,ObjectId, propertyName, value);
        }

        /// <summary>
        /// Generate body script after complied.
        /// </summary>
        virtual public void RecvRpc(RpcMessage rpcMessage) { }

        /// <summary>
        /// Generate body script after complied.
        /// </summary>
        virtual public void RecvReplicatedRpc(RpcReplicated rpcReplicated) { }


        NetworkTransformRepicated[] transformRepicateds;
        public void RecvReplicatedTransformRpc(RpcReplicated rpcReplicated) 
        {
            foreach(NetworkTransformRepicated transformRepicated in transformRepicateds)
            {
                transformRepicated.UpdateValue(rpcReplicated.PropertyName, rpcReplicated.Value);
            }
        }

        #endregion Core
    }

}
