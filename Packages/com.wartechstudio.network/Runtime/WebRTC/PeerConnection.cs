using System;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using System.Collections.Generic;
using Unity.WebRTC;
using System.Threading.Tasks;

namespace Unity.WartechStudio.Network
{
    namespace WebRTC
    {
        public sealed class IceCandidate
        {
            [JsonProperty("candidate")]
            private string m_Candidate;
            [JsonProperty("sdpMLineIndex")]
            private int m_SdpMLineIndex;
            [JsonProperty("sdpMid")]
            private string m_SdpMid;

            public IceCandidate(string candidate, int sdpMLineIndex, string sdpMid)
            {
                m_Candidate = candidate;
                m_SdpMLineIndex = sdpMLineIndex;
                m_SdpMid = sdpMid;
            }
            /*
            public IceCandidate(RTCIceCandidate candidate)
            {
                m_Candidate = candidate.Candidate;
                m_SdpMLineIndex = candidate.SdpMLineIndex.Value;
                m_SdpMid = candidate.SdpMid;
            }
            */
            public RTCIceCandidate ToRTCIceCandidate()
            {
                RTCIceCandidateInit rTCIceCandidateInit = new RTCIceCandidateInit();
                rTCIceCandidateInit.candidate = m_Candidate;
                rTCIceCandidateInit.sdpMLineIndex = m_SdpMLineIndex;
                rTCIceCandidateInit.sdpMid = m_SdpMid;
                return new RTCIceCandidate(rTCIceCandidateInit);
            }
        }

        public sealed class SessionDescription
        {
            [JsonProperty("sdp")]
            private string m_Sdp;

            [JsonProperty("type")]
            private RTCSdpType m_Type;

            public SessionDescription(string sdp, RTCSdpType type)
            {
                m_Sdp = sdp;
                m_Type = type;
            }
            /*
            public SessionDescription(RTCSessionDescription desc)
            {
                m_Sdp = desc.sdp;
                m_Type = desc.type;
            }
            */
            public RTCSessionDescription ToRTCSessionDescription()
            {
                RTCSessionDescription rTCSessionDescription = new RTCSessionDescription();
                rTCSessionDescription.sdp = m_Sdp;
                rTCSessionDescription.type = m_Type;
                return rTCSessionDescription;
            }
        }

        public struct SignalData
        {
            [JsonProperty("iceCandidates")]
            private List<IceCandidate> IceCandidates;
            [JsonProperty("sessionDescription")]
            private SessionDescription SessionDescription;
            
            [JsonIgnore]
            public readonly List<RTCIceCandidate> RTCIceCandidates;
            [JsonIgnore]
            public readonly RTCSessionDescription RTCSessionDescription;
            
            public SignalData(RTCSessionDescription rtcSessionDescription, List<RTCIceCandidate> rtcIceCandidates)
            {
                RTCIceCandidates = rtcIceCandidates;
                RTCSessionDescription = rtcSessionDescription;
                IceCandidates = new List<IceCandidate>();
                foreach(RTCIceCandidate candidate in rtcIceCandidates)
                {
                    IceCandidates.Add(new IceCandidate(candidate.Candidate, candidate.SdpMLineIndex.Value, candidate.SdpMid));
                }
                this.SessionDescription = new SessionDescription(rtcSessionDescription.sdp, rtcSessionDescription.type);
            }

            public SignalData(byte[] bytes)
            {
                string jsonStr = Encoding.UTF8.GetString(bytes);
                SignalData tempObject = JsonConvert.DeserializeObject<SignalData>(jsonStr);
                IceCandidates = tempObject.IceCandidates;
                SessionDescription = tempObject.SessionDescription;
                RTCIceCandidates = new List<RTCIceCandidate>();
                RTCSessionDescription = SessionDescription.ToRTCSessionDescription();
                foreach(IceCandidate candidate in IceCandidates)
                {
                    RTCIceCandidates.Add(candidate.ToRTCIceCandidate());
                }
            }

            public byte[] Serialize()
            {
                return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this));
            }
        }
        public class PeerConnection
        {
            public List<RTCIceCandidate> IceCandidates { get; private set; } = new List<RTCIceCandidate>();
            public RTCSessionDescription SessionDescription { get; private set; }
            public RTCSignalingState SignalingState => m_PeerConnection.SignalingState;
            public ulong Id { get; private set; }

            public Action<ulong, string, byte[]> OnReceiveDelegate;

            private RTCPeerConnection m_PeerConnection;
            private Dictionary<string, RTCDataChannel> m_ChannelMap = new Dictionary<string, RTCDataChannel>();
            private RTCConfiguration m_Configuration;
            public PeerConnection(RTCConfiguration config)
            {
                Id = ULongRandom();
                m_Configuration = config;
            }

            public void Init(List<string> channels, Action SuccessCallback, Action<string> ErrorCallback = null)
            {
                m_PeerConnection = new RTCPeerConnection(ref m_Configuration);
                m_PeerConnection.OnIceCandidate = (candidate) =>
                {
                    IceCandidates.Add(candidate);
                };
                m_PeerConnection.OnDataChannel = (channel) =>
                {
                    if(m_ChannelMap.ContainsKey(channel.Label))
                    {
                        m_ChannelMap.Remove(channel.Label);
                    }
                    m_ChannelMap.Add(channel.Label, channel);
                    m_ChannelMap[channel.Label].OnMessage = (bytes) => { OnMessage(channel.Label, bytes); };
                };

                RTCIceGatheringState iceGatheringState = RTCIceGatheringState.New;
                if (!(channels == null || channels.Count == 0))
                {
                    m_PeerConnection.OnIceGatheringStateChange = (state) =>
                    {
                        iceGatheringState = state;
                        if (state == RTCIceGatheringState.Complete && SessionDescription.sdp != null && SessionDescription.sdp != "")
                        {
                            SuccessCallback.Invoke();
                        }
                    };
                }

                channels?.ForEach((channel) =>
                {
                    RegisterDataChannel(channel);
                });
                
                CreateOffer((desc) =>
                {
                    SetLocalDescription(desc, () =>
                    {
                        SessionDescription = desc;
                        if(channels == null || channels.Count == 0 || iceGatheringState == RTCIceGatheringState.Complete)
                        {
                            m_PeerConnection.OnIceGatheringStateChange = null;
                            SuccessCallback.Invoke();
                        }
                    });
                },  ErrorCallback);
            }
            public void ConnectTo(bool IsHost,RTCSessionDescription desc, List<RTCIceCandidate> candidates, Action SuccessCallback = null, Action<string> ErrorCallback = null)
            {
                m_PeerConnection.OnIceConnectionChange = (state) =>
                {
                    if (state == RTCIceConnectionState.Checking && !IsHost)
                    {
                        m_PeerConnection.OnIceConnectionChange = null;
                        SuccessCallback?.Invoke();
                        return;
                    }

                    if (state == RTCIceConnectionState.Completed && IsHost)
                    {
                        m_PeerConnection.OnIceConnectionChange = null;
                        SuccessCallback?.Invoke();
                        return;
                    }

                    if(state == RTCIceConnectionState.Connected)
                    {
                        m_PeerConnection.OnIceConnectionChange = null;
                        SuccessCallback?.Invoke();
                        return;
                    }

                    if (state == RTCIceConnectionState.Failed)
                    {
                        m_PeerConnection.OnIceConnectionChange = null;
                        ErrorCallback?.Invoke("failure to add ice candidates.");
                        return;
                    }
                };

                if(IsHost)
                {
                    SetRemoteDescription(desc, () =>
                    {
                        SessionDescription = desc;
                        candidates.ForEach((RTCIceCandidate candidate) =>
                        {
                            m_PeerConnection.AddIceCandidate(candidate);
                        });
                    }, ErrorCallback);
                    return;
                }

                SetRemoteDescription(desc, () =>
                {
                    CreateAnswer((desc) =>
                    {
                        SetLocalDescription(desc, () =>
                        {
                            SessionDescription = desc;
                            candidates.ForEach((RTCIceCandidate candidate) =>
                            {
                                m_PeerConnection.AddIceCandidate(candidate);
                            });
                        }, ErrorCallback);
                    }, ErrorCallback);
                }, ErrorCallback);
            }
            public void Send(string channel,string message)
            {
                m_ChannelMap[channel].Send(message);
            }

            public void Send(string channel, byte[] data)
            {
                if(m_ChannelMap[channel].ReadyState != RTCDataChannelState.Open)
                {
                    //Debug.Log($"channel {channel} on connection id {Id} is not open.");
                    return;
                }
                m_ChannelMap[channel].Send(data);
            }

            public void Close()
            {
                //if(m_PeerConnection != null)
                    //m_PeerConnection.Close();
            }

            private async void CreateOffer(Action<RTCSessionDescription> SuccessCallback = null, Action<string> ErrorCallback = null)
            {
                RTCSessionDescriptionAsyncOperation op = m_PeerConnection.CreateOffer();
                if (op.IsError)
                {
                    ErrorCallback?.Invoke(op.Error.message);
                    return;
                }
                int waitCount = 0;
                while (op.Desc.sdp == null)
                {
                    await Task.Delay(100);
                    waitCount++;
                    if (waitCount >= 50) // 100 ms * 50 = 5s
                    {
                        ErrorCallback?.Invoke(op.Error.message);
                        return;
                    }
                }
                SuccessCallback?.Invoke(op.Desc);
            }
            private async void CreateAnswer(Action<RTCSessionDescription> SuccessCallback = null, Action<string> ErrorCallback = null)
            {
                RTCSessionDescriptionAsyncOperation op = m_PeerConnection.CreateAnswer();
                if (op.IsError)
                {
                    ErrorCallback?.Invoke(op.Error.message);
                    return;
                }
                int waitCount = 0;
                while (!op.IsDone)
                {
                    await Task.Delay(100);
                    waitCount++;
                    if (waitCount >= 600) // 100 ms * 600 = 60s
                    {
                        ErrorCallback?.Invoke(op.Error.message);
                        return;
                    }
                }
                SuccessCallback?.Invoke(op.Desc);
            }
            private async void SetLocalDescription(RTCSessionDescription desc, Action SuccessCallback = null, Action<string> ErrorCallback = null)
            {
                RTCSetSessionDescriptionAsyncOperation setop = m_PeerConnection.SetLocalDescription(ref desc);
                int waitCount = 0;
                if (setop.IsError)
                {
                    ErrorCallback?.Invoke(setop.Error.message);
                    return;
                }
                while (!setop.IsDone)
                {
                    await Task.Delay(100);
                    waitCount++;
                    if (waitCount >= 300) // 100 ms * 50 = 5s
                    {
                        ErrorCallback?.Invoke("set discription incomplete!");
                        return;
                    }
                }
                SuccessCallback?.Invoke();
            }
            private async void SetRemoteDescription(RTCSessionDescription desc, Action SuccessCallback = null, Action<string> ErrorCallback = null)
            {
                RTCSetSessionDescriptionAsyncOperation setop = m_PeerConnection.SetRemoteDescription(ref desc);
                int waitCount = 0;
                if (setop.IsError)
                {
                    ErrorCallback?.Invoke(setop.Error.message);
                    return;
                }
                while (!setop.IsDone)
                {
                    await Task.Delay(100);
                    waitCount++;
                    if (waitCount >= 300) // 100 ms * 50 = 5s
                    {
                        ErrorCallback?.Invoke("set discription incomplete!");
                        return;
                    }
                }

                SuccessCallback?.Invoke();
            }

            private bool RegisterDataChannel(string channel)
            {
                if (m_PeerConnection == null) return false;
                if (m_ChannelMap.ContainsKey(channel)) return false;
                m_ChannelMap.Add(channel, m_PeerConnection.CreateDataChannel(channel));
                m_ChannelMap[channel].OnMessage = (bytes) => { OnMessage(channel, bytes); };
                return true;
            }
            private void OnMessage(string channel, byte[] bytes)
            {
                OnReceiveDelegate?.Invoke(Id,channel, bytes);
            }

            private ulong ULongRandom()
            {
                System.Random rand = new System.Random();
                byte[] buf = new byte[8];
                rand.NextBytes(buf);
                ulong result = (ulong)BitConverter.ToInt64(buf, 0);
                return result;
            }
        }
    }
}
