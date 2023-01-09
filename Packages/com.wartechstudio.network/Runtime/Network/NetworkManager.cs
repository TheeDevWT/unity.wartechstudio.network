using UnityEngine;
using Unity.WebRTC;
using Unity.WartechStudio.Network.Session;
using Unity.WartechStudio.Network;
using System;
using System.Collections.Generic;

namespace Unity.WartechStudio.Network
{
    [Serializable]
    public class NetworkObjectJsonList
    {
        public NetworkObjectJson[] Objects;

        public NetworkObjectJsonList(NetworkObjectJson[] objects)
        {
            Objects = objects;
        }

        public NetworkObject GetNetworkObjectPrefab(string name)
        {
            foreach (NetworkObjectJson item in Objects)
            {
                if (name == item.Name)
                    return Resources.Load<NetworkObject>(item.Path);
            }
            return null;
        }
    }

    [Serializable]
    public struct NetworkObjectJson
    {
        public string Name;
        public string Path;

        public NetworkObjectJson(string name, string path)
        {
            Name = name;
            Path = path;
        }
    }

    public class NetworkManager : MonoBehaviour
    {
        public UnityEngine.UI.Text ClientIdText;
        public UnityEngine.UI.Text SessionIdText;
        public UnityEngine.UI.InputField SessionIdInput;
        public static NetworkManager Singleton { get; private set; }

        public ulong ClientId { get; private set; } = 0;
        public ulong ServerId { get; private set; } = 0;
        public bool IsServer { get; private set; } = false;
        public ulong? SessionId => m_SessionManager?.SessionId;

        public List<ulong> ClientIds { get; private set; } = new List<ulong>();

        [SerializeField]
        private string m_P2PServerUrl;
        [SerializeField]
        private Session.SessionConfig m_SessionConfig;
        [SerializeField]
        private RTCConfiguration m_Configuration;

        private SessionManager m_SessionManager;

        private Dictionary<ulong, int> m_NetworkObjectSpawnCount = new Dictionary<ulong, int>();
        private Dictionary<ulong, int> m_ClientSyncMaxObject = new Dictionary<ulong, int>();
        private Dictionary<ulong, int> m_ClientSyncCount = new Dictionary<ulong, int>();
        public NetworkObjectJsonList NetworkObjectPrefabList { get; private set; }

        private Dictionary<ulong, NetworkObject> m_NetworkObjects = new Dictionary<ulong, NetworkObject>();
        private Dictionary<ulong, string> m_NetworkObjectsName = new Dictionary<ulong, string>();

        public Action OnStartedDelegate;
        public Action<ulong> OnConnectedDelegate;
        public Action<ulong> OnDisconnectedDelegate;

        private void Awake()
        {
            Singleton = this;
            Unity.WebRTC.WebRTC.Initialize();
            TextAsset targetFile = Resources.Load<TextAsset>("Data/NetworkObjects");
            NetworkObjectPrefabList = JsonUtility.FromJson<NetworkObjectJsonList>(targetFile.ToString());
        }

        private void Start()
        {
            ClientId = RpcMessageHelpers.ULongRandom();
            ClientIdText.text = ClientId.ToString();
            m_SessionManager = new SessionManager(ClientId,null,m_SessionConfig.MaxPlayer);
            m_SessionManager.OnReceiveDataDelegate += OnReceiveData;
            m_SessionManager.OnConnectedDelegate += OnConnected;
            m_SessionManager.OnDisconnectedDelegate += OnDisconnected;
            m_SessionManager.Init(m_Configuration, m_P2PServerUrl, (bool success) =>
            {
                Debug.Log("connection manager init " + (success ? "success." : "fail."));
            });
        }

        private void OnDestroy()
        {
            if (m_SessionManager != null)
            {
                m_SessionManager.OnReceiveDataDelegate -= OnReceiveData;
                m_SessionManager.OnConnectedDelegate -= OnConnected;
                m_SessionManager.Close();
            }

            Unity.WebRTC.WebRTC.Dispose();
        }

        public void StartHost()
        {
            if (m_SessionManager.IsInitSuccess)
            {
                IsServer = true;
                ServerId = ClientId;
                Debug.Log(gameObject.name + " -> host starting...");
                m_SessionManager.StartHost((bool success) =>
                {
                    Debug.Log(gameObject.name + " -> host start " + (success ? "success." : "fail."));
                    Debug.Log("session id " + m_SessionManager.SessionId.ToString());
                    SessionIdText.text = m_SessionManager.SessionId.ToString();
                    if (success)
                        OnStartedDelegate?.Invoke();
                });
            }
        }

        public void StartClient()
        {
            StartClient(System.Convert.ToUInt64(SessionIdInput.text));
        }

        public void StartClient(ulong sessionId)
        {
            if (m_SessionManager.IsInitSuccess)
            {
                Debug.Log(gameObject.name + " -> client starting...");
                m_SessionManager.StartClient(sessionId,(bool success) =>
                {
                    Debug.Log(gameObject.name + " -> client start " + (success ? "success" : "fail") + " and wait confirm.");
                    if(success)
                        OnStartedDelegate?.Invoke();
                });
            }
        }

        public void OnConnected(ulong clientId)
        {
            Debug.Log(gameObject.name + " -> connection id " + clientId.ToString() + " connected");
            ClientIds.Add(clientId);
            if (IsServer && ClientId != clientId)
            {
                SyncNetworkObject(clientId);
                m_SessionManager.Send(clientId, SessionManager.MANAGER_CHANNEL_NAME, $"serverId:{ClientId}");
            } else if(IsServer)
            {
                OnConnectedDelegate?.Invoke(clientId);
            }
            else
            {
                OnConnectedDelegate?.Invoke(clientId);
            }

            if (ClientIds.Count == 1)
                OnConnected(ClientId); // force call OnConnected for owner connection
        }

        public void OnDisconnected(ulong clientId)
        {
            OnDisconnectedDelegate?.Invoke(clientId);
        }

        public void RequestSendServerRpc(bool reliable, ulong objectId, string funcName, params dynamic[] parameters)
        {
            if (IsServer || ServerId == 0) return;
            RpcMessage rpcMessage = new RpcMessage(ClientId, objectId, funcName, parameters);
            byte[] data = rpcMessage.SerializeToBytes();
            if (!reliable)
                m_SessionManager.Send(ServerId, SessionManager.RPC_CHANNEL_NAME, data);
            else
                m_SessionManager.Send(ServerId, SessionManager.RPC_CHANNEL_NAME, data, (success) =>
                {
                    if (!success)
                        RequestSendServerRpc(reliable, objectId, funcName, parameters);
                });
            
        }

        public void RequestSendClientRpc(bool reliable, ulong recvId,ulong objectId, string funcName, params dynamic[] parameters)
        {
            RpcMessage rpcMessage = new RpcMessage(ClientId, objectId, funcName, parameters);
            byte[] data = rpcMessage.SerializeToBytes();
            ClientIds.ForEach((ulong id) =>
            {
                if (id == recvId)
                {
                    if (!reliable)
                        m_SessionManager.Send(id, SessionManager.RPC_CHANNEL_NAME, data);
                    else
                        m_SessionManager.Send(id, SessionManager.RPC_CHANNEL_NAME, data, (success) =>
                        {
                            if (!success)
                                RequestSendClientRpc(reliable, recvId,objectId, funcName, parameters);
                        });
                }
            });
        }

        public void RequestSendBroadcastRpc(bool reliable, ulong objectId, string funcName, params dynamic[] parameters)
        {
            RpcMessage rpcMessage = new RpcMessage(ClientId, objectId, funcName, parameters);
            byte[] data = rpcMessage.SerializeToBytes();
            ClientIds.ForEach((ulong id) =>
            {
                if (id != rpcMessage.SenderId)
                {
                    if (!reliable)
                        m_SessionManager.Send(id, SessionManager.RPC_CHANNEL_NAME, data);
                    else
                        m_SessionManager.Send(id, SessionManager.RPC_CHANNEL_NAME, data, (success) =>
                        {
                            if (!success)
                                RequestSendBroadcastRpc(reliable, objectId, funcName, parameters);
                        });
                }
            });
        }

        public void RequestSendReplicatedRpc(bool reliable, ulong objectId, string propertyName, dynamic value)
        {
            RpcReplicated rpcReplicated = new RpcReplicated(ClientId, objectId, propertyName, value);
            byte[] data = rpcReplicated.SerializeToBytes();
            ClientIds.ForEach((ulong id) =>
            {
                if (id != rpcReplicated.SenderId)
                {
                    if(!reliable)
                        m_SessionManager.Send(id, SessionManager.REPLICATED_CHANNEL_NAME, data);
                    else
                        m_SessionManager.Send(id, SessionManager.REPLICATED_CHANNEL_NAME, data,(success) =>
                        {
                            if (!success)
                                RequestSendReplicatedRpc(reliable, objectId, propertyName, value);
                        });
                }
                    
            });
        }

        void OnReceiveData(ulong senderId, string channel, ulong reqId,byte[] data)
        {

            if (channel == SessionManager.OBJECT_CHANNEL_NAME)
            { 
                RecvRpcObject(senderId,data);
                return;
            }

            if (channel == SessionManager.RPC_CHANNEL_NAME)
            {
                RpcMessage rpcMessage = new RpcMessage(data);
                GetNetworkObject(rpcMessage.ObjectId)?.RecvRpc(rpcMessage);
                return;
            }

            if (channel == SessionManager.REPLICATED_CHANNEL_NAME)
            {
                RecvRpcRplicated(data);
                return;
            }

            if (channel == SessionManager.MANAGER_CHANNEL_NAME)
            {
                RecvManagerMessage(data);
                return;
            }
            
        }

        public NetworkObject GetNetworkObject(ulong objectId)
        {
            if(m_NetworkObjects.ContainsKey(objectId))
                return m_NetworkObjects[objectId];
            return null;
        }

        public T Spawn<T>(NetworkObject networkObject, Vector3 position,Quaternion rotation,ulong ownerId = 0)
        {
            EAuthorizedFlags authorizedFlags = EAuthorizedFlags.Server;
            if (ownerId != 0)
            {
                authorizedFlags |= EAuthorizedFlags.Owner;
            }
            RpcObject rpcObject = new RpcObject(ClientId, networkObject, EObjectState.Spawn, ownerId == 0 ? ClientId : ownerId, authorizedFlags);
            rpcObject.SetPosition(position);
            rpcObject.SetRotation(rotation);
            NetworkObject networkObjectSpawned = SpawnNetworkObject(rpcObject);
            m_NetworkObjectSpawnCount.Add(rpcObject.ObjectId, 0);
            SendRpcObject(rpcObject);
            return networkObjectSpawned.gameObject.GetComponent<T>();
        }
        public T Spawn<T>(NetworkObject networkObject, ulong ownerId = 0)
        {
            return Spawn<T>(networkObject, Vector3.zero, Quaternion.identity, ownerId);
        }

        public void Despawn(NetworkObject networkObject)
        {
            if(!networkObject.IsAuthorized)
            {
                return;
            }

            RpcObject rpcObject = new RpcObject(ClientId, m_NetworkObjectsName[networkObject.ObjectId], networkObject, EObjectState.Despawn);
            if(DespawnNetworkObject(rpcObject))
                SendRpcObject(rpcObject);
        }

        private NetworkObject SpawnNetworkObject(RpcObject rpcObject)
        {
            NetworkObject instantiateObj = Instantiate<NetworkObject>(rpcObject.NetworkObject, rpcObject.Position,rpcObject.Rotation);
            instantiateObj.Initialized(rpcObject);
            m_NetworkObjects.Add(rpcObject.ObjectId, instantiateObj);
            m_NetworkObjectsName.Add(rpcObject.ObjectId, rpcObject.ObjectName);
            return instantiateObj;
        }

        private bool DespawnNetworkObject(RpcObject rpcObject)
        {
            if (rpcObject.NetworkObject == null)
                return false;
            m_NetworkObjects.Remove(rpcObject.ObjectId);
            m_NetworkObjectsName.Remove(rpcObject.ObjectId);
            return true;
        }

        private void RecvManagerMessage(byte[] data)
        {
            string message = System.Text.Encoding.UTF8.GetString(data);

            if(message.Contains("serverId"))
            {
                ServerId = Convert.ToUInt64(message.Split(":")[1]);
                return;
            }
        }

        private void RecvRpcRplicated(byte[] data)
        {
            RpcReplicated rpcReplicated = new RpcReplicated(data);
            GetNetworkObject(rpcReplicated.ObjectId)?.RecvReplicatedRpc(rpcReplicated);
            GetNetworkObject(rpcReplicated.ObjectId)?.RecvReplicatedTransformRpc(rpcReplicated);
        }

        private void RecvRpcObject(ulong senderId,byte[] data)
        {
            RpcObject rpcObject = new RpcObject(data);

            switch(rpcObject.State)
            {
                case EObjectState.Sync:
                    if(SpawnNetworkObject(rpcObject))
                    {
                        rpcObject.SetState(EObjectState.SyncSuccess);
                        m_SessionManager.Send(rpcObject.SenderId, SessionManager.OBJECT_CHANNEL_NAME, rpcObject.SerializeToBytes());
                    }
                    return;
                case EObjectState.Spawn:
                    if (SpawnNetworkObject(rpcObject))
                    {
                        rpcObject.SetState(EObjectState.SpawnSuccess);
                        m_SessionManager.Send(rpcObject.SenderId, SessionManager.OBJECT_CHANNEL_NAME, rpcObject.SerializeToBytes());
                    }
                    return;
                case EObjectState.Despawn:
                    if (DespawnNetworkObject(rpcObject))
                    {
                        rpcObject.SetState(EObjectState.DespawnSuccess);
                        m_SessionManager.Send(rpcObject.SenderId, SessionManager.OBJECT_CHANNEL_NAME, rpcObject.SerializeToBytes());
                    }
                    return;
                case EObjectState.SpawnSuccess:
                    m_NetworkObjectSpawnCount[rpcObject.ObjectId]++;
                    if(m_NetworkObjectSpawnCount[rpcObject.ObjectId] == m_SessionManager.GetOnlineConnectionCount() - 1)
                    {
                        GetNetworkObject(rpcObject.ObjectId)?.BroadcastRpcNetworkSpawned();
                    }
                    return;
                case EObjectState.DespawnSuccess:
                    m_NetworkObjectSpawnCount[rpcObject.ObjectId]++;
                    if (m_NetworkObjectSpawnCount[rpcObject.ObjectId] == m_SessionManager.GetOnlineConnectionCount() - 1)
                    {
                        GetNetworkObject(rpcObject.ObjectId)?.BroadcastRpcNetworkDespawned();
                    }
                    return;
                case EObjectState.SyncSuccess:
                    m_ClientSyncCount[senderId]++;
                    if(m_ClientSyncCount[senderId] == m_ClientSyncMaxObject[senderId])
                    {
                        OnConnectedDelegate?.Invoke(senderId);
                    }
                    return;
            }
        }

        private void SendRpcObject(RpcObject rpcObject)
        {
            byte[] data = rpcObject.SerializeToBytes();
            ClientIds.ForEach((ulong id) =>
            {
                if(id != ClientId)
                    m_SessionManager.Send(id, SessionManager.OBJECT_CHANNEL_NAME, data,(success)=>
                    {
                        if(!success)
                        {
                            Debug.Log($"SendRpcObject to {id} fail");
                            SendRpcObject(rpcObject);
                        }
                    });
            });
        }

        private void SyncNetworkObject(ulong clientId)
        {
            if (clientId == ClientId) return;
            m_ClientSyncMaxObject.Add(clientId, m_NetworkObjects.Count);
            m_ClientSyncCount.Add(clientId, 0);
            foreach (var item in m_NetworkObjects)
            {
                RpcObject rpcObject = new RpcObject(ClientId, m_NetworkObjectsName[item.Key],item.Value, EObjectState.Sync);
                m_SessionManager.Send(clientId, SessionManager.OBJECT_CHANNEL_NAME, rpcObject.SerializeToBytes());
            }
        }
    }

}
