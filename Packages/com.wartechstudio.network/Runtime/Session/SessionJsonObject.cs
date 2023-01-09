using Newtonsoft.Json;
using System.Collections.Generic;
using System.Text;
using Unity.WartechStudio.Network.WebRTC;

namespace Unity.WartechStudio.Network.Session
{
    public struct Signal
    {
        [JsonProperty("iceCandidates")]
        private List<IceCandidate> m_IceCandidates;
        [JsonProperty("sessionDescription")]
        private SessionDescription m_SessionDescription;

        public bool IsValid()
        {
            return m_IceCandidates != null && m_IceCandidates.Count > 0;
        }
    }

    namespace Http
    {
        public interface ReqInterface
        {
            public string ReqApi();
            public string ToHttpParameter();
        }

        public struct ReqRegisterHost : ReqInterface
        {
            public readonly string Req;

            private ulong m_Id;
            private int m_MaxConnection;
                
            public ReqRegisterHost(ulong id, int max_player)
                => (m_Id, m_MaxConnection, Req) = (id, max_player, "create_session");

            public string ToHttpParameter()
            {
                return "host_id=" + m_Id.ToString() + "&max_connection=" + m_MaxConnection.ToString();
            }

            public string ReqApi()
            {
                return Req;
            }
        }
        public struct ReqRegisterConnection : ReqInterface
        {
            public readonly string Req;

            private ulong m_Id;
            private ulong m_SessionId;

            public ReqRegisterConnection(ulong id, ulong sessionId)
                => (m_Id, m_SessionId, Req) = (id, sessionId, "register_connection");

            public string ToHttpParameter()
            {
                return "session_id=" + m_SessionId.ToString() + "&connection_id=" + m_Id.ToString();
            }
            public string ReqApi()
            {
                return Req;
            }
        }
        public struct ReqSetSignal : ReqInterface
        {
            public readonly string Req;

            private ulong m_SessionId;
            private ulong m_Token;
            private int m_OwnerIndex;
            private int m_TargetIndex;
            private string m_Signal;

            public ReqSetSignal(ulong sessionId, ulong token, int ownerIndex, int targetIndex, byte[] signal)
                => (m_SessionId, m_Token, m_OwnerIndex, m_TargetIndex, m_Signal, Req) 
                = (sessionId, token, ownerIndex, targetIndex, Encoding.UTF8.GetString(signal),"set_signal");

            public string ToHttpParameter()
            {
                return "session_id=" + m_SessionId.ToString() +
                        "&token=" + m_Token.ToString() +
                        "&owner_index=" + m_OwnerIndex.ToString() +
                        "&target_index=" + m_TargetIndex.ToString() +
                        "&signal=" + m_Signal;
            }
            public string ReqApi()
            {
                return Req;
            }
        }
        public struct ReqGetSignal : ReqInterface
        {
            public readonly string Req;

            private ulong m_SessionId;
            private ulong m_Token;
            private int m_OwnerIndex;
            private int m_TargetIndex;

            public ReqGetSignal(ulong sessionId, ulong token, int ownerIndex, int targetIndex)
                => (m_SessionId, m_Token, m_OwnerIndex, m_TargetIndex, Req)
                = (sessionId, token, ownerIndex, targetIndex, "get_signal");

            public string ToHttpParameter()
            {
                return "session_id=" + m_SessionId.ToString() +
                        "&token=" + m_Token.ToString() +
                        "&owner_index=" + m_OwnerIndex.ToString() +
                        "&target_index=" + m_TargetIndex.ToString();
            }
            public string ReqApi()
            {
                return Req;
            }
        }
        public struct ReqGetWaitConfirm : ReqInterface
        {
            public readonly string Req;

            private ulong m_SessionId;
            private ulong m_Token;
            private int m_OwnerIndex;

            public ReqGetWaitConfirm(ulong sessionId, ulong token,int ownerIndex)
                => (m_SessionId, m_Token, m_OwnerIndex,Req) = (sessionId, token, ownerIndex,"get_wait_confirm");

            public string ToHttpParameter()
            {
                return "session_id=" + m_SessionId.ToString() + "&token=" + m_Token.ToString() + "&owner_index=" + m_OwnerIndex.ToString();
            }
            public string ReqApi()
            {
                return Req;
            }
        }
        public struct ReqRequestConfirm : ReqInterface
        {
            public readonly string Req;

            private ulong m_SessionId;
            private ulong m_Token;
            private int m_OwnerIndex;
            private int m_TargetIndex;

            public ReqRequestConfirm(ulong sessionId, ulong token,int ownerIndex,int targetIndex)
                => (m_SessionId, m_Token, m_OwnerIndex, m_TargetIndex,Req)
                = (sessionId, token, ownerIndex, targetIndex, "request_confirm");

            public string ToHttpParameter()
            {
                return "session_id=" + m_SessionId.ToString() + "&token=" + m_Token.ToString() + "&owner_index=" + m_OwnerIndex.ToString() + "&target_index=" + m_TargetIndex.ToString();
            }
            public string ReqApi()
            {
                return Req;
            }
        }

        public struct ReqConfirmConnection : ReqInterface
        {
            public readonly string Req;

            private ulong m_SessionId;
            private ulong m_Token;
            private int m_OwnerIndex;
            private int m_TargetIndex;

            public ReqConfirmConnection(ulong sessionId, ulong token, int ownerIndex, int targetIndex)
                => (m_SessionId, m_Token, m_OwnerIndex, m_TargetIndex, Req)
                = (sessionId, token, ownerIndex, targetIndex, "confirm_connection");

            public string ToHttpParameter()
            {
                return "session_id=" + m_SessionId.ToString() + "&token=" + m_Token.ToString() + "&owner_index=" + m_OwnerIndex.ToString() + "&target_index=" + m_TargetIndex.ToString();
            }
            public string ReqApi()
            {
                return Req;
            }
        }

    }

    namespace Json
    {
        public struct RecvRegisterHost
        {
            [JsonProperty("session_id")]
            public readonly ulong SessionId;
            [JsonProperty("token")]
            public readonly ulong Token;
            public RecvRegisterHost(ulong sessionId, ulong token) 
                => (SessionId, Token) = (sessionId, token);

            public static RecvRegisterHost Deserialize(byte[] bytes)
            {
                string str = Encoding.UTF8.GetString(bytes);
                return JsonConvert.DeserializeObject<RecvRegisterHost>(str);
            }
        }

        public struct RecvRegisterConnection
        {
            [JsonProperty("connection_index")]
            public readonly int ConnectionIndex;
            [JsonProperty("token")]
            public readonly ulong Token;
            public RecvRegisterConnection(int connectionIndex, ulong token)
                => (ConnectionIndex, Token) = (connectionIndex, token);

            public static RecvRegisterConnection Deserialize(byte[] bytes)
            {
                string str = Encoding.UTF8.GetString(bytes);
                return JsonConvert.DeserializeObject<RecvRegisterConnection>(str);
            }
        }

        public struct RecvCreateRoom
        {
            [JsonProperty("session_id")]
            public readonly ulong SessionId;
            public RecvCreateRoom(ulong sessionId) => (SessionId) = (sessionId);

            public static RecvCreateRoom Deserialize(byte[] bytes)
            {
                string str = Encoding.UTF8.GetString(bytes);
                return JsonConvert.DeserializeObject<RecvCreateRoom>(str);
            }
        }

        public struct RecvSetSignal
        {
            [JsonProperty("success")]
            public readonly bool IsSuccess;
            public RecvSetSignal(bool success) => (IsSuccess) = (success);

            public static RecvSetSignal Deserialize(byte[] bytes)
            {
                string str = Encoding.UTF8.GetString(bytes);
                return JsonConvert.DeserializeObject<RecvSetSignal>(str);
            }
        }

        public struct RecvGetSignal
        {
            [JsonProperty("signal")]
            public readonly Signal SignalData;
            public RecvGetSignal(Signal signal) => (SignalData) = (signal);

            public static RecvGetSignal Deserialize(byte[] bytes)
            {
                string str = Encoding.UTF8.GetString(bytes);
                if (str.Contains("not found"))
                    return new RecvGetSignal();
                return JsonConvert.DeserializeObject<RecvGetSignal>(str);
            }

            public byte[] GetSignalDataOfBytes()
            {
                return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(SignalData));
            }

            public bool IsValid()
            {
                return SignalData.IsValid();
            }
        }

        public struct RecvGetWaitConfirm
        {
            [JsonProperty("wait_index")]
            public readonly List<int> WaitIndex;

            public RecvGetWaitConfirm(List<int> waitIndex) => (WaitIndex) = (waitIndex);

            public static RecvGetWaitConfirm Deserialize(byte[] bytes)
            {
                string str = Encoding.UTF8.GetString(bytes);
                return JsonConvert.DeserializeObject<RecvGetWaitConfirm>(str);
            }
        }
    }
    
}
