using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Collections.Generic;
using System;
using UnityEngine;

namespace Unity.WartechStudio.Network
{
    [System.Flags]
    public enum EAuthorizedFlags
    {
        None = 1 << 0,
        Server = 1 << 1,
        Owner = 1 << 2,
        Client = 1 << 3,
    }

    [System.Flags]
    public enum EReplicateTransformFlags
    {
        None = 1 << 0,
        Position = 1 << 1,
        Rotation = 1 << 2,
        Scale = 1 << 3,
    }

    public enum EObjectState
    {
        Sync,
        SyncSuccess,
        Spawn,
        SpawnSuccess,
        SpawnFail,
        Spawned,
        Despawn,
        DespawnSuccess
    }

    [Serializable]
    public struct RpcObject
    {
        [field: NonSerialized]
        public NetworkObject NetworkObject { get; private set; }
        public ulong SenderId { get; private set; }
        public string ObjectName { get; private set; }
        public ulong ObjectId { get; private set; }
        public ulong OwnerId { get; private set; }
        public EAuthorizedFlags AuthorizedFlag { get; private set; }
        public EObjectState State { get; private set; }

        public string m_PositionStr { get; private set; }

        public string m_RotationStr { get; private set; }

        [field: NonSerialized]
        public Vector3 Position { get; private set; }
        [field: NonSerialized]
        public Quaternion Rotation { get; private set; }

        /// <summary>
        ///  for new object
        /// </summary>
        public RpcObject(ulong senderId, NetworkObject networkObject, EObjectState objectState, ulong ownerId, EAuthorizedFlags authorizedFlags)
        {
            SenderId = senderId;
            ObjectName = networkObject.name;
            NetworkObject = networkObject;
            ObjectId = RpcMessageHelpers.ULongRandom();
            OwnerId = ownerId;
            AuthorizedFlag = authorizedFlags;
            State = objectState;
            Position = Vector3.zero;
            Rotation = Quaternion.identity;
            m_PositionStr = "";
            m_RotationStr = "";
        }

        public RpcObject(ulong senderId, string rawObjectName, NetworkObject networkObject, EObjectState objectState)
        {
            SenderId = senderId;
            ObjectName = rawObjectName;
            NetworkObject = networkObject;
            ObjectId = networkObject.ObjectId;
            OwnerId = networkObject.OwnerId;
            AuthorizedFlag = networkObject.AuthorizedFlags;
            State = objectState;
            Position = networkObject.transform.position;
            Rotation = networkObject.transform.rotation;
            m_PositionStr = Position.ToString();
            m_RotationStr = Rotation.ToString();
        }

        public void SetPosition(Vector3 position)
        {
            Position = position;
            m_PositionStr = Position.ToString();
        }

        public void SetRotation(Quaternion rotation)
        {
            Rotation = rotation;
            m_RotationStr = Rotation.ToString();
        }
        public RpcObject(byte[] data)
        {
            this = (RpcObject)RpcMessageHelpers.ByteArrayToObject(data);

            if (State <= EObjectState.Spawned)
                NetworkObject = NetworkManager.Singleton.NetworkObjectPrefabList.GetNetworkObjectPrefab(ObjectName);
            else
                NetworkObject = NetworkManager.Singleton.GetNetworkObject(ObjectId);

            Position = RpcMessageHelpers.StringToVector3(m_PositionStr);
            Rotation = RpcMessageHelpers.StringQuaternion(m_RotationStr);
        }

        public byte[] SerializeToBytes()
        {
            return RpcMessageHelpers.ObjectToByteArray(this);
        }

        public void SetState(EObjectState state)
        {
            State = state;
        }

    }

    [Serializable]
    public struct RpcReplicated
    {
        // optimize rpc package
        private readonly dynamic[] a;

        [field: NonSerialized]
        public ulong SenderId { get; private set; }
        [field: NonSerialized] 
        public ulong ObjectId { get; private set; }
        [field: NonSerialized]
        public string PropertyName { get; private set; }
        [field: NonSerialized]
        public dynamic Value { get; private set; }

        public RpcReplicated(ulong senderId, ulong objectId, string propertyName, dynamic value)
        {
            SenderId = senderId;
            ObjectId = objectId;
            PropertyName = propertyName;
            Value = RpcMessageHelpers.ConvertToSupportType(value);
            a = new dynamic[4];
            a[0] = SenderId;
            a[1] = ObjectId;
            a[2] = PropertyName;
            a[3] = Value;
        }

        public RpcReplicated(byte[] data)
        {
            this = (RpcReplicated)RpcMessageHelpers.ByteArrayToObject(data);

            SenderId = a[0];
            ObjectId = a[1];
            PropertyName = a[2];
            Value = a[3];
        }

        public byte[] SerializeToBytes()
        {
            return RpcMessageHelpers.ObjectToByteArray(this);
        }

        public T GetValue<T>()
        {
            return RpcMessageHelpers.GetParameter<T>(Value);
        }
    }

    [Serializable]
    public struct RpcMessage
    {
        // optimize rpc package
        private readonly dynamic[] a;

        [field: NonSerialized]
        public ulong SenderId { get; private set; }
        [field: NonSerialized]
        public ulong ObjectId { get; private set; }
        [field: NonSerialized]
        public string FuncName { get; private set; }
        [field: NonSerialized]
        public byte[] Payload { get; private set; }

        public RpcMessage(ulong senderId, ulong objectId, string funcName, params dynamic[] parameters)
        {
            SenderId = senderId;
            ObjectId = objectId;
            FuncName = funcName;
            Payload = null;
            m_parameters = null;
            a = new dynamic[4];

            Payload = GetBytesDataFromDynamicParams(parameters);
            a[0] = SenderId;
            a[1] = ObjectId;
            a[2] = FuncName;
            a[3] = Payload;

        }

        public RpcMessage(byte[] data)
        {
            this = (RpcMessage)RpcMessageHelpers.ByteArrayToObject(data);

            SenderId = a[0];
            ObjectId = a[1];
            FuncName = a[2];
            Payload = a[3];
        }

        public byte[] SerializeToBytes()
        {
            return RpcMessageHelpers.ObjectToByteArray(this);
        }

        dynamic[] m_parameters;
        public T GetParameter<T>(int index)
        {
            if (m_parameters == null)
                m_parameters = (dynamic[])RpcMessageHelpers.ByteArrayToObject(Payload);
            return RpcMessageHelpers.GetParameter<T>(m_parameters[index]);
        }

        private byte[] GetBytesDataFromDynamicParams(dynamic[] parameters)
        {
            List<dynamic> data = new List<dynamic>();
            for (int i = 0; i < parameters.Length; ++i)
            {
                if (parameters[i] == null)
                {
                    data.Add(parameters[i]);
                    continue;
                }
                data.Add(RpcMessageHelpers.ConvertToSupportType(parameters[i]));
            }

            return RpcMessageHelpers.ObjectToByteArray(data.ToArray());
        }
    }
    static public class RpcMessageHelpers
    {

        public static dynamic ConvertToSupportType(dynamic parameter)
        {
            if (parameter == null)
            {
                return parameter;
            }

            Type paramType = parameter.GetType();
            switch (paramType.Name)
            {
                case nameof(String): return parameter;
                case nameof(Vector3): return ((Vector3)parameter).ToString();
                case nameof(Quaternion): return ((Quaternion)parameter).ToString();
            }

            if (typeof(INetworkStruct).IsAssignableFrom(paramType))
            {
                INetworkStruct networkStruct = (INetworkStruct)parameter;
                return networkStruct.SerializeToJson();
            }

            if (typeof(NetworkObject).IsAssignableFrom(paramType))
            {
                NetworkObject networkObject = (NetworkObject)parameter;
                return networkObject.ObjectId;
            }

            if (typeof(GameObject).IsAssignableFrom(paramType))
            {
                GameObject networkObject = (GameObject)parameter.GetComponent<NetworkObject>();
                if (networkObject != null)
                {
                    return networkObject.GetComponent<NetworkObject>()?.ObjectId;
                }
            }

            if (paramType.IsValueType)
            {
                return parameter;
            }

            if (paramType.IsEnum)
            {
                return (int)parameter;
            }

            return null;
        }

        public static T GetParameter<T>(dynamic parameter)
        {
            switch (typeof(T).Name)
            {
                case nameof(String):
                    return (T)parameter;
                case nameof(Vector3):
                    return RpcMessageHelpers.StringToVector3(parameter);
                case nameof(Quaternion):
                    return RpcMessageHelpers.StringQuaternion(parameter);
            }

            if (!typeof(T).IsValueType)
            {
                return (T)RpcMessageHelpers.ObjectIdToNetworkObject(parameter);
            }

            if (typeof(T).IsEnum)
            {
                return (T)parameter;
            }

            return parameter;
        }
        public static ulong ULongRandom()
        {
            System.Random rand = new System.Random();
            byte[] buf = new byte[8];
            rand.NextBytes(buf);
            ulong result = (ulong)BitConverter.ToInt64(buf, 0);
            return result;
        }

        public static NetworkObject ObjectIdToNetworkObject(ulong objectId)
        {
            return NetworkManager.Singleton.GetNetworkObject(objectId); ;
        }

        public static string DynamicToString(dynamic[] value, int index)
        {
            return (string)value[index];
        }

        public static Vector3 StringToVector3(string sVector)
        {
            if (sVector.StartsWith("(") && sVector.EndsWith(")"))
            {
                sVector = sVector.Substring(1, sVector.Length - 2);
            }
            string[] sArray = sVector.Split(',');
            return new Vector3(
                float.Parse(sArray[0]),
                float.Parse(sArray[1]),
                float.Parse(sArray[2]));
        }

        public static Quaternion StringQuaternion(string sQuaternion)
        {
            if (sQuaternion.StartsWith("(") && sQuaternion.EndsWith(")"))
            {
                sQuaternion = sQuaternion.Substring(1, sQuaternion.Length - 2);
            }
            string[] sArray = sQuaternion.Split(',');
            return new Quaternion(
                float.Parse(sArray[0]),
                float.Parse(sArray[1]),
                float.Parse(sArray[2]),
                float.Parse(sArray[3]));
        }


        public static byte[] ObjectToByteArray(System.Object obj)
        {
            BinaryFormatter bf = new BinaryFormatter();
            using (var ms = new MemoryStream())
            {
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }
        }

        public static System.Object ByteArrayToObject(byte[] bytes)
        {
            using (var memStream = new MemoryStream())
            {
                var binForm = new BinaryFormatter();
                memStream.Write(bytes, 0, bytes.Length);
                memStream.Seek(0, SeekOrigin.Begin);
                var obj = binForm.Deserialize(memStream);
                return obj;
            }
        }

        public static string GetGameObjectPath(GameObject obj)
        {
            string path = "/" + obj.name;
            while (obj.transform.parent != null)
            {
                obj = obj.transform.parent.gameObject;
                path = "/" + obj.name + path;
            }
            return path;
        }
    }

}
