using Unity.WartechStudio.Network.WebRTC;
using Unity.WartechStudio.Network.Session.Http;
using Unity.WartechStudio.Network.Session.Json;
using System.Collections.Generic;
using UnityEngine;
using Unity.WebRTC;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System;

namespace Unity.WartechStudio.Network.Session
{
    class SessionManager
    {
        public static readonly string SYSTEM_CHANNEL_NAME = "system";
        public static readonly string OBJECT_CHANNEL_NAME = "object";
        public static readonly string RPC_CHANNEL_NAME = "rpc";
        public static readonly string REPLICATED_CHANNEL_NAME = "replicate";
        public static readonly string MANAGER_CHANNEL_NAME = "manager";

        public struct ConnectionInfo
        {
            public PeerConnection Connection;
            public ConnectionState State;
            public ulong ClientId;
            

            public ConnectionInfo(PeerConnection connection, ConnectionState state)
            {
                Connection = connection;
                State = state;
                ClientId = 0;
            }
        }

        public enum ConnectionState
        {
            None,
            Wait,
            Connecting,
            Connected,
            Fail
        }
        public bool IsInitSuccess { get; private set; } = false;
        public ulong SessionId => m_SessionId;

        /// <summary>
        /// params = senderId,channel,reqId,data
        /// </summary>
        public Action<ulong,string,ulong,byte[]> OnReceiveDataDelegate;
        /// <summary>
        /// params = connectoinId
        /// </summary>
        public Action<ulong> OnConnectedDelegate;
        /// <summary>
        /// params = connectoinId
        /// </summary>
        public Action<ulong> OnDisconnectedDelegate;
        

        private ulong m_SessionId = 0;
        private ulong m_SessionToken = 0;
        private ulong m_ClientId = 0;
        private ConnectionInfo m_LocalConnection;
        private int m_OwnerIndexOfSessionConnection = 0;
        private string m_P2PServerUrl = "";
        private bool m_IsLoopRunning = false;
        private List<int> m_IndexWaitConfirm = new List<int>();

        //SYSTEM_CHANNEL_NAME only of SessionManager use for internal.
        private List<string> m_Channels = new List<string>(new string[] { SYSTEM_CHANNEL_NAME, OBJECT_CHANNEL_NAME, RPC_CHANNEL_NAME, REPLICATED_CHANNEL_NAME , MANAGER_CHANNEL_NAME });
        private ConnectionInfo[] m_Connections;
        private Dictionary<ulong, Action<bool>> ResCallback = new Dictionary<ulong, Action<bool>>();
        private Dictionary<ulong, double> ResCallbackTimestamp = new Dictionary<ulong, double>();
        private int m_MaxConnection;
        public SessionManager(ulong clientId,List<string> dataChanels = null, int maxConnection = 8)
            => (m_ClientId,m_Channels, m_Connections, m_MaxConnection)
            =  (clientId,AppendChanels(m_Channels,dataChanels), m_Connections = new ConnectionInfo[maxConnection], maxConnection);

        public void Init(RTCConfiguration iceConfig,string p2pServerUrl,Action<bool> successCallback = null)
            => internal_Init(iceConfig, p2pServerUrl, successCallback);
        public void StartHost(Action<bool> successCallback = null)
            => internal_StartHost(successCallback);
        public void StartClient(ulong sessionId, Action<bool> successCallback = null)
            => internal_StartClient(sessionId, successCallback);
        public bool Send(ulong toId, string channel, string message, Action<bool> res = null, ulong reqId = 0)
            => internal_Send(toId, channel,System.Text.Encoding.UTF8.GetBytes(message), res, reqId);
        public bool Send(ulong toId, string channel, byte[] data, Action<bool> res = null, ulong reqId = 0)
            => internal_Send(toId, channel, data, res, reqId);
        public void Close()
        {
            for(int i = 0; i < m_Connections.Length; ++i)
            {
                m_Connections[i].Connection?.Close();
            }
            m_IsLoopRunning = false;
        }

        ~SessionManager()
        {
            Close();
        }

        public ConnectionState GetConnectionState(int targetIndex)
        {
            return m_Connections[targetIndex].State;
        }

        public int GetOnlineConnectionCount()
        {
            int count = 0;

            foreach(ConnectionInfo conn in m_Connections)
            {
                if (conn.State == ConnectionState.Connected) count++;
            }

            return count;
        }


        private void OnReceiveData(ulong connectionId,string channel,byte[] data)
        {
            if (channel == SYSTEM_CHANNEL_NAME)
            {
                ReadDataFromSystemChannel(connectionId, data);
                return;
            }

            Message message = new Message(data);
            if (message.ReqId != 0)
            {
                if (!message.Success)
                {
                    message.Success = true;
                    foreach (ConnectionInfo item in m_Connections)
                    {
                        if (item.Connection.Id == connectionId)
                        {
                            item.Connection.Send(channel, message.SerializeToBytes());
                        }
                    }
                }
                else
                {
                    ResCallback[message.ReqId]?.Invoke(true);
                    ResCallback.Remove(message.ReqId);
                    return;
                }
            }
            OnReceiveDataDelegate?.Invoke(m_Connections[GetConnectionIndexFromPeerConnectionId(connectionId)].ClientId, channel, message.ReqId,message.Data);
        }

        private void OnConnected(ConnectionInfo connectionInfo)
        {
            OnConnectedDelegate?.Invoke(connectionInfo.ClientId);
        }

        private async void Loop()
        {
            while(m_IsLoopRunning)
            {
                await Task.Delay(200);

                if (m_SessionId == 0)
                    continue;

                GetWaitRequestConfirm();
                CheckSendCallbackTimeout();
                HealthCheck();
            }
        }

        private async void internal_Init(RTCConfiguration iceConfig, string p2pServerUrl, Action<bool> successCallback)
        {
            bool bEnd = false;
            int bSuccessCount = 0;
            m_P2PServerUrl = p2pServerUrl;
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                Debug.LogWarning("internet not reachable");
                successCallback?.Invoke(false);
            }

            bSuccessCount++;

            m_LocalConnection = new ConnectionInfo(new PeerConnection(iceConfig),ConnectionState.Connected);
            m_LocalConnection.ClientId = m_ClientId;
            m_LocalConnection.Connection.OnReceiveDelegate += OnReceiveData;
            m_LocalConnection.Connection.Init(m_Channels, () =>
            {
                bSuccessCount++;
            }, (string err) =>
            {
                bEnd = true;
                Debug.LogError(err);
            });

            GetRequest(m_P2PServerUrl + "/api/healthcheck", (result, data) =>
            {
                if (result != UnityWebRequest.Result.Success)
                {
                    bEnd = true;
                    Debug.LogWarning("p2p server not reachable");
                    return;
                }

                // check p2p server success.
                bSuccessCount++;
            });

            while (!bEnd && bSuccessCount != 3)
            {
                await Task.Delay(300);
            }

            for (int i = 0; i < m_MaxConnection; ++i)
            {
                m_Connections[i] = new ConnectionInfo(new WebRTC.PeerConnection(iceConfig), ConnectionState.None);
                m_Connections[i].Connection.OnReceiveDelegate += OnReceiveData;
                //m_ConnectionsState[i] = ConnectionState.None;

                //m_PeerConnections[i] = new WebRTC.PeerConnection(iceConfig);
                //m_PeerConnections[i].OnReceiveDelegate += OnReceiveData;
            }

            IsInitSuccess = bSuccessCount == 3;

            successCallback?.Invoke(IsInitSuccess);
            
            if (IsInitSuccess)
            {
                m_IsLoopRunning = true;
                Loop();
            }
        }

        private void internal_StartHost(Action<bool> successCallback = null)
        {
            m_Connections[m_OwnerIndexOfSessionConnection] = m_LocalConnection;
            ReqRegisterHost reqRegisterHost = new ReqRegisterHost(m_ClientId, m_MaxConnection);
            GetRequest(GetRequestUrl(reqRegisterHost), (result, data) =>
            {
                RecvRegisterHost recvRegisterHost = RecvRegisterHost.Deserialize(data);
                m_SessionId = recvRegisterHost.SessionId;
                m_SessionToken = recvRegisterHost.Token;
                int successCount = 0;
                for(int i = 0; i < m_Connections.Length; ++i)
                {
                    PeerConnection item = m_Connections[i].Connection;
                    if (i == m_OwnerIndexOfSessionConnection)
                        continue;
                    item.Init(m_Channels, () =>
                    {
                        SetSignalToSession(item, (bool success) =>
                        {
                            if (success)
                            {
                                successCount++;
                                if (successCount == m_MaxConnection - 1)
                                    successCallback?.Invoke(true);
                                return;
                            }
                            successCallback?.Invoke(false);
                        });
                    }, (string err) => { Debug.LogError(err); });

                };
            });
        }

        private void internal_StartClient(ulong sessionId, Action<bool> successCallback = null)
        {
            ReqRegisterConnection reqRegisterConnection = new ReqRegisterConnection(m_ClientId, sessionId);
            GetRequest(GetRequestUrl(reqRegisterConnection), (result, data) =>
            {
                RecvRegisterConnection recvRegisterConnection = RecvRegisterConnection.Deserialize(data);
                m_SessionId = sessionId;
                m_OwnerIndexOfSessionConnection = recvRegisterConnection.ConnectionIndex;
                m_SessionToken = recvRegisterConnection.Token;
                m_Connections[m_OwnerIndexOfSessionConnection] = m_LocalConnection;
                int successCount = 0;
                for (int i = 0; i < m_Connections.Length; ++i)
                {
                    PeerConnection item = m_Connections[i].Connection;
                    if (i < m_OwnerIndexOfSessionConnection)
                    {
                        item.Init(null, () =>
                        {
                            GetSignalAndCreateAnswer(item, (bool success) =>
                            {
                                if (success)
                                {
                                    successCount++;
                                    if (successCount == m_MaxConnection - 1)
                                        successCallback?.Invoke(true);
                                    return;
                                }
                                successCallback?.Invoke(false);
                            });
                        }, (string err) => { Debug.LogError(err); });
                        continue;
                    }

                    if (i == m_OwnerIndexOfSessionConnection)
                        continue; ;

                    item.Init(m_Channels, () =>
                    {
                        SetSignalToSession(item, (bool success) =>
                        {
                            if (success)
                            {
                                successCount++;
                                if (successCount == m_MaxConnection - 1)
                                    successCallback?.Invoke(true);
                                return;
                            }
                            successCallback?.Invoke(false);
                        });
                    }, (string err) => { Debug.LogError(err); });
                };
            });
        }

        [Serializable]
        struct Message
        {
            public readonly ulong ReqId;
            public bool Success;
            public readonly byte[] Data;

            public Message(ulong reqId, bool success, byte[] data) => (ReqId, Success, Data) = (reqId, success, data);

            public Message(byte[] data)
            {
                this = (Message)RpcMessageHelpers.ByteArrayToObject(data);
            }
            public byte[] SerializeToBytes()
            {
                return RpcMessageHelpers.ObjectToByteArray(this);
            }
        }

        private bool internal_Send(ulong toId,string channel,byte[] data, Action<bool> res = null, ulong reqId = 0)
        {
            if (toId == m_ClientId)
                return false;

            if (reqId == 0 && res != null)
                reqId = RpcMessageHelpers.ULongRandom();
            foreach(ConnectionInfo item in m_Connections)
            {
                if(item.ClientId == toId)
                {
                    if(res != null && reqId != 0 && !ResCallback.ContainsKey(reqId))
                    {
                        ResCallback.Add(reqId, res);
                        ResCallbackTimestamp.Add(reqId, new TimeSpan().TotalMilliseconds);
                    }
                    Message message = new Message(reqId, false,data);
                    item.Connection.Send(channel, message.SerializeToBytes());
                    return true;
                }
            }

            return false;
        }

        private void CheckSendCallbackTimeout()
        {
            double milliSec = new TimeSpan().TotalMilliseconds;
            foreach (var item in ResCallbackTimestamp)
            {
                if(item.Value + 1000 < milliSec)
                {
                    if (ResCallback.ContainsKey(item.Key))
                    {
                        ResCallback[item.Key]?.Invoke(false);
                        ResCallback.Remove(item.Key);
                    }
                    ResCallbackTimestamp.Remove(item.Key);
                }
            }

        }
        // for set signal after answer.
        private void SetSignalAndRequestConfirm(PeerConnection peerConnection, Action<bool> successCallback = null)
        {
            SetSignalToSession(peerConnection,(bool success) =>
            {
                if(!success)
                {
                    successCallback?.Invoke(false);
                    return;
                }
                int indexTargetOfSignal = GetConnectionIndexFromPeerConnection(peerConnection);
                ReqRequestConfirm reqRequestConfirm = new ReqRequestConfirm(m_SessionId, m_SessionToken, m_OwnerIndexOfSessionConnection, indexTargetOfSignal);
                GetRequest(GetRequestUrl(reqRequestConfirm), (result, data) =>
                {
                    if (result != UnityWebRequest.Result.Success)
                    {
                        successCallback?.Invoke(false);
                        return;
                    }
                    successCallback?.Invoke(true);
                });
            });
        }

        // for connect to offer
        private async void GetSignalAndCreateAnswer(PeerConnection peerConnection, Action<bool> successCallback = null)
        {
            await Task.Delay(1000);
            int indexTargetOfSignal = GetConnectionIndexFromPeerConnection(peerConnection);
            ReqGetSignal reqGetSignal = new ReqGetSignal(m_SessionId, m_SessionToken, indexTargetOfSignal, m_OwnerIndexOfSessionConnection);
            
            GetRequest(GetRequestUrl(reqGetSignal), (result, data) =>
            {
                if (result != UnityWebRequest.Result.Success)
                {
                    successCallback?.Invoke(false);
                    return;
                }
                RecvGetSignal recvGetSignal = RecvGetSignal.Deserialize(data);
                if (!recvGetSignal.IsValid())
                {
                    GetSignalAndCreateAnswer(peerConnection, successCallback);
                    return;
                }
                WebRTC.SignalData signalData = new WebRTC.SignalData(recvGetSignal.GetSignalDataOfBytes());
                SetConnectionState(indexTargetOfSignal, ConnectionState.Connecting);
                peerConnection.ConnectTo(false, signalData.RTCSessionDescription, signalData.RTCIceCandidates, () =>
                {
                    SetSignalAndRequestConfirm(peerConnection, successCallback);
                },(string err) => { Debug.LogError(err); successCallback?.Invoke(false); });
            });
        }

        // for offer
        private void SetSignalToSession(PeerConnection peerConnection, Action<bool> successCallback = null)
        {
            int indexTargetOfSignal = GetConnectionIndexFromPeerConnection(peerConnection);
            WebRTC.SignalData signalData = new WebRTC.SignalData(peerConnection.SessionDescription, peerConnection.IceCandidates);
            if(peerConnection.SessionDescription.sdp == null || peerConnection.SessionDescription.sdp == "")
            {
                Debug.LogError($"SetSignalToSession Fail {indexTargetOfSignal}");
            }
            ReqSetSignal reqSetSignal = new ReqSetSignal(m_SessionId, m_SessionToken, m_OwnerIndexOfSessionConnection, indexTargetOfSignal, signalData.Serialize());
            SetConnectionState(indexTargetOfSignal, ConnectionState.Wait);
            GetRequest(GetRequestUrl(reqSetSignal),(result, data) =>
            {
                if (result != UnityWebRequest.Result.Success)
                {
                    successCallback?.Invoke(false);
                    return;
                }
                successCallback?.Invoke(RecvSetSignal.Deserialize(data).IsSuccess);
            });
        }

        private void ConfirmConnection(int targetIndex)
        {
            if (m_IndexWaitConfirm.Contains(targetIndex))
                return;
            m_IndexWaitConfirm.Add(targetIndex);
            ReqGetSignal reqGetSignal = new ReqGetSignal(m_SessionId, m_SessionToken, targetIndex, m_OwnerIndexOfSessionConnection);
            GetRequest(GetRequestUrl(reqGetSignal), (result, data) => 
            {
                RecvGetSignal recvGetSignal = RecvGetSignal.Deserialize(data);
                WebRTC.SignalData signalData = new WebRTC.SignalData(recvGetSignal.GetSignalDataOfBytes());
                if (signalData.RTCSessionDescription.sdp == null || signalData.RTCSessionDescription.sdp == "")
                {
                    Debug.LogError($"sdp is null of {targetIndex}");
                    m_IndexWaitConfirm.Remove(targetIndex);
                    return;
                }
                m_Connections[targetIndex].Connection.ConnectTo(true, signalData.RTCSessionDescription, signalData.RTCIceCandidates, () =>
                {
                    ReqConfirmConnection reqConfirmConnection = new ReqConfirmConnection(m_SessionId, m_SessionToken, m_OwnerIndexOfSessionConnection, targetIndex);
                    GetRequest(GetRequestUrl(reqConfirmConnection), (result, data) =>
                    {
                        m_Connections[targetIndex].Connection.Send(SYSTEM_CHANNEL_NAME, "connection_confirm:"+ m_ClientId.ToString());
                        SetConnectionState(targetIndex, ConnectionState.Connected);
                        m_IndexWaitConfirm.Remove(targetIndex);
                    });
                }, (string err) => { Debug.LogError(err); });
            });
        }

        private void GetWaitRequestConfirm()
        {
            ReqGetWaitConfirm reqGetWaitConfirm = new ReqGetWaitConfirm(m_SessionId, m_SessionToken,m_OwnerIndexOfSessionConnection);
            GetRequest(GetRequestUrl(reqGetWaitConfirm), (result, data) =>
            {
                RecvGetWaitConfirm recvGetWaitConfirm = RecvGetWaitConfirm.Deserialize(data);
                recvGetWaitConfirm.WaitIndex.ForEach((int item) =>
                {
                    ConfirmConnection(item);
                });
            });
        }

        private void HealthCheck()
        {

        }

        private void SetConnectionState(int targetIndex,ConnectionState state)
        {
            if(GetConnectionState( targetIndex) != state)
            {
                m_Connections[targetIndex].State = state;
            }
        }

        // utility

        private int GetConnectionIndexFromPeerConnectionId(ulong id)
        {
            for(int i = 0; i < m_Connections.Length; ++i)
            {
                if (m_Connections[i].Connection.Id == id)
                    return i;
            };

            return -1;
        }
        private int GetConnectionIndexFromPeerConnection(PeerConnection peerConnection)
        {
            if (peerConnection == null) return -1;
            return GetConnectionIndexFromPeerConnectionId(peerConnection.Id);
        }

        private string GetRequestUrl(ReqInterface reqInterface)
        {
            return m_P2PServerUrl + "/api/" + reqInterface.ReqApi() + "?" + reqInterface.ToHttpParameter();
        }

        async void GetRequest(string uri, Action<UnityWebRequest.Result, byte[]> callback)
        {
            using (UnityWebRequest webRequest = UnityWebRequest.Get(uri))
            {
                webRequest.SendWebRequest();

                string[] pages = uri.Split('/');
                int page = pages.Length - 1;

                while (webRequest.result == UnityWebRequest.Result.InProgress)
                {
                    switch (webRequest.result)
                    {
                        case UnityWebRequest.Result.ConnectionError:
                        case UnityWebRequest.Result.DataProcessingError:
                            Debug.LogError(pages[page] + ": Error: " + webRequest.error);
                            break;
                        case UnityWebRequest.Result.ProtocolError:
                            Debug.LogError(pages[page] + ": HTTP Error: " + webRequest.error);
                            break;
                        case UnityWebRequest.Result.Success:
                            break;
                    }
                    await Task.Delay(300);
                }
                callback(webRequest.result, webRequest.downloadHandler.data);
            }
        }

        private List<string> AppendChanels(List<string> oldChannels, List<string> newChannels)
        {
            if(newChannels != null)
                oldChannels.AddRange(newChannels);
            return oldChannels;
        }

        // handle receive data
        private void ReadDataFromSystemChannel(ulong connectionId, byte[] data)
        {
            int connectionIndex = GetConnectionIndexFromPeerConnectionId(connectionId);
            string message = System.Text.Encoding.UTF8.GetString(data);
            if (message.Contains("connection_confirm"))
            {
                ulong remoteId = message.Split(":").Length > 0 ? Convert.ToUInt64(message.Split(":")[1]) : 0;
                m_Connections[connectionIndex].ClientId = remoteId;
                SetConnectionState(connectionIndex, ConnectionState.Connected);
                OnConnected(m_Connections[connectionIndex]);
                m_Connections[connectionIndex].Connection.Send(SYSTEM_CHANNEL_NAME, System.Text.Encoding.UTF8.GetBytes("register:" + m_ClientId.ToString()));
            }
            else if (message.Contains("register"))
            {
                ulong clientId = message.Split(":").Length > 0 ? Convert.ToUInt64(message.Split(":")[1]) : 0;
                ConnectionRegister(connectionIndex, clientId);
            }
        }

        private void ConnectionRegister(int connectionIndex, ulong clientId)
        {
            m_Connections[connectionIndex].ClientId = clientId;
            OnConnected(m_Connections[connectionIndex]);
        }
    }
}