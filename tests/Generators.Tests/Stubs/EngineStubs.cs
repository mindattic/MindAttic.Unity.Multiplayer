// Stand-ins for the parts of UnityEngine, Unity.Netcode (NGO 2.11) and the adapter's NetEntity that generated code
// touches, with the same names and shapes, so a test compilation proves the generated code binds. Embedded in the test
// assembly as text, never compiled into it. NetEntityStubTests checks the NetEntity members still match the real class.
using System;

namespace UnityEngine
{
    public class Object { }
    public class Component : Object { }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }

    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector3 { public float x, y, z; }
    public struct Vector4 { public float x, y, z, w; }
    public struct Vector2Int { public int x, y; }
    public struct Vector3Int { public int x, y, z; }
    public struct Quaternion { public float x, y, z, w; }
    public struct Color { public float r, g, b, a; }
    public struct Color32 { public byte r, g, b, a; public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; } }
    public struct Ray { }
    public struct Ray2D { }
}

namespace Unity.Collections
{
    public struct FixedString64Bytes { }
}

namespace Unity.Netcode
{
    public enum SendTo { Owner, NotOwner, Server, NotServer, Me, NotMe, Everyone, ClientsAndHost, Authority, NotAuthority, SpecifiedInParams }
    public enum RpcDelivery { Reliable, Unreliable }
    public enum RpcInvokePermission { Server, Owner, Everyone }
    public enum RpcTargetUse { Temp, Persistent }
    public enum NetworkVariableReadPermission { Everyone, Owner }
    public enum NetworkVariableWritePermission { Server, Owner }

    [AttributeUsage(AttributeTargets.Method)]
    public class RpcAttribute : Attribute
    {
        public RpcAttribute(SendTo target) { }
        public RpcDelivery Delivery = RpcDelivery.Reliable;
        public RpcInvokePermission InvokePermission;
    }

    public struct RpcReceiveParams { public ulong SenderClientId; }

    public abstract class BaseRpcTarget { }

    public struct RpcParams
    {
        public RpcReceiveParams Receive;
        public static implicit operator RpcParams(BaseRpcTarget target) { return default(RpcParams); }
    }

    public class RpcTarget
    {
        public BaseRpcTarget Single(ulong clientId, RpcTargetUse use) { return null; }
    }

    public interface INetworkSerializable { }
    public interface INetworkSerializeByMemcpy { }
    public struct NetworkObjectReference { }
    public struct NetworkBehaviourReference { }

    public class NetworkVariable<T>
    {
        public NetworkVariable(T value = default(T), NetworkVariableReadPermission readPerm = NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission writePerm = NetworkVariableWritePermission.Server) { Value = value; }
        public delegate void OnValueChangedDelegate(T previousValue, T newValue);
        public OnValueChangedDelegate OnValueChanged;
        public T Value { get; set; }
    }

    public abstract class NetworkBehaviour : UnityEngine.MonoBehaviour
    {
        public RpcTarget RpcTarget { get { return null; } }
        public bool IsServer { get { return false; } }
        public bool IsSpawned { get { return false; } }
        public ulong OwnerClientId { get { return 0; } }
        public virtual void OnNetworkSpawn() { }
        public virtual void OnNetworkDespawn() { }
    }
}

namespace MindAttic.Unity.Multiplayer
{
    using global::MindAttic.Unity.Multiplayer.Core;
    using global::Unity.Netcode;

    public abstract class NetEntity : NetworkBehaviour
    {
        protected virtual void OnSpawned() { }
        protected virtual void OnDespawned() { }
        protected virtual void NetBindSynced() { }
        protected virtual void NetUnbindSynced() { }
        protected bool NetReceiveToServer(string rpc, ulong senderClientId, bool requireOwnership, int rateLimit, int burst, out NetSender sender) { sender = default(NetSender); return true; }
        protected bool NetCanSendToClients(string rpc) { return true; }
        protected bool NetReceiveFromServer(string rpc, ulong senderClientId) { return true; }
        protected void NetWriteSynced<T>(NetworkVariable<T> variable, T value, string name) { variable.Value = value; }
    }
}
