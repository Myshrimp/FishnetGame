using System;
using System.Collections.Generic;

// 仅替代 Unity 原生组件环境与全局服务器状态，路由和序列化使用项目源码。
namespace UnityEngine
{
    public class MonoBehaviour
    {
        public string name = "RpcChannelTest";
        public readonly Dictionary<Type, object> Components = new Dictionary<Type, object>();
        public T GetComponent<T>() => (T)Components[typeof(T)];
    }

    public sealed class RequireComponent : Attribute
    {
        public RequireComponent(Type type) { }
    }

    public sealed class DisallowMultipleComponent : Attribute { }

    public static class Debug
    {
        public static void LogWarning(object message) { }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w)
        { this.x = x; this.y = y; this.z = z; this.w = w; }
    }
}

namespace FaceSlapper.Networking
{
    public static class Net
    {
        public static bool IsServer;
        public static bool IsClient;
    }
}
