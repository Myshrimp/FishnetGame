using System;
using System.Collections.Generic;

// 替代原生组件与网络边界；服务、事件总线、RPC 入口和 JSON 均使用项目代码。
namespace UnityEngine
{
    public class Object
    {
        public static implicit operator bool(Object value) => value != null;
        public static bool operator !(Object value) => value == null;
    }
    public class MonoBehaviour : Object { }
    public class GameObject : Object
    {
        public static readonly Dictionary<Type, object> Instances = new Dictionary<Type, object>();
        public static T FindObjectOfType<T>() where T : class => Instances.TryGetValue(typeof(T), out var value) ? (T)value : null;
    }
    public class SerializeField : Attribute { }
    public static class Debug
    {
        public static void Log(object value) { }
        public static void LogWarning(object value) { }
        public static void LogError(object value) { }
    }
}
namespace FaceSlapper.Core
{
    public class GameManager
    {
        public static readonly GameManager Instance = new GameManager();
        public T Get<T>() where T : new() => new T();
        public void AddAndRegister<T>() { }
    }
    public class LogComponent { }
    public class TimeComponent { }
    public class PoolComponent { }
}
namespace FaceSlapper.Input { public class InputComponent { } }
namespace FaceSlapper.Network { public class NetworkComponent { } }
namespace LiteNetLib.Utils { }
namespace FaceSlapper.FrameSync
{
    public struct FP { public static FP FromFloat(float value) => default; }
    public struct FPVec3 { public FPVec3(FP x, FP y, FP z) { } }
}
namespace FaceSlapper.Networking
{
    public sealed class NetRpcAttribute : Attribute { }
    public class NetObject
    {
        public bool IsSpawned = true;
        public int RpcSenderClientId = -1;
    }
    public abstract class NetBehaviour : UnityEngine.MonoBehaviour
    {
        public NetObject NetObject = new NetObject();
        public bool IsServer => Net.IsServer;
        public bool IsClient => Net.IsClient;
        protected virtual void Awake() { }
        protected virtual void OnDestroy() { }
        public virtual void OnNetSpawnServer() { }
        public virtual void OnNetSpawnClient() { }
        public virtual void OnNetDespawnServer() { }
        public virtual void OnNetDespawnClient() { }
        protected void SendServerRpc(string method, params object[] args) => Net.Send("Server", method, args);
        protected void SendObserversRpc(string method, params object[] args) => Net.Send("Observers", method, args);
        protected void SendTargetRpc(int clientId, string method, params object[] args) => Net.Send("Target", method, args);
    }
    public static class Net
    {
        public static bool IsServer, IsClient;
        public static bool IsHost => IsServer && IsClient;
        public static int LocalClientId;
        public static event Action<int> OnRemoteClientConnected;
        public static event Action<int> OnRemoteClientDisconnected;
        public static readonly List<(string Direction, string Method, object[] Args)> Sent = new();
        public static Action<string, string, object[]> Wire;
        public static class Server { public static readonly HashSet<int> ClientIds = new HashSet<int>(); }
        public static void Send(string direction, string method, object[] args)
        {
            Sent.Add((direction, method, args));
            Wire?.Invoke(direction, method, args);
        }
        public static void Reset()
        {
            IsServer = IsClient = true;
            LocalClientId = 7;
            Server.ClientIds.Clear();
            Server.ClientIds.Add(7);
            Server.ClientIds.Add(11);
            Sent.Clear();
            Wire = null;
            OnRemoteClientConnected = null;
            OnRemoteClientDisconnected = null;
        }
    }
}
namespace FaceSlapper.FrameSync.Separated.UI
{
    public class ChatView : UnityEngine.MonoBehaviour
    {
        public readonly List<string> Messages = new List<string>();
        public void AddMsgItem(string message) => Messages.Add(message);
    }
}
