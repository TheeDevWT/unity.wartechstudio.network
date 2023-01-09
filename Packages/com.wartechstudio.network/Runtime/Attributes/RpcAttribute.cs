using System;

namespace Unity.WartechStudio.Network
{
    public abstract class RpcAttribute : Attribute
    {
        public bool Reliable = false;
    }

    /// <summary>
    /// Call by client of object owner to server and execute on server.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ServerRpcAttribute : RpcAttribute 
    {
        public ServerRpcAttribute(bool reliable = false) => (Reliable) = (reliable);
    }

    /// <summary>
    /// Call by server to client of object owner and execute on that client.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ClientRpcAttribute : RpcAttribute
    {
        public ClientRpcAttribute(bool reliable = false) => (Reliable) = (reliable);
    }

    /// <summary>
    /// Call by authorized of object to all client(include server) and execute on that;
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class BroadcastRpcAttribute : RpcAttribute
    {
        public BroadcastRpcAttribute(bool reliable = false) => (Reliable) = (reliable);
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ReplicatedAttribute : RpcAttribute
    {
        public ReplicatedAttribute(bool reliable = false) => (Reliable) = (reliable);
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class ReplicatedOnChangeAttribute : RpcAttribute 
    {
        public string PropertyName;
        public ReplicatedOnChangeAttribute(string propertyName, bool reliable = false) => (PropertyName, Reliable) = (propertyName, reliable);
    }
}