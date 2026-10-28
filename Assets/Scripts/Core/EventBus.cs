using System;
using System.Collections.Generic;

namespace FaceSlapper.Core
{
    /// <summary>
    /// 全局事件派发中心。基于事件结构体类型的泛型订阅/发布，零装箱（事件请定义为 struct）。
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> Handlers = new Dictionary<Type, Delegate>(64);

        public static void Subscribe<T>(Action<T> handler)
        {
            Type type = typeof(T);
            if (Handlers.TryGetValue(type, out Delegate existing))
                Handlers[type] = Delegate.Combine(existing, handler);
            else
                Handlers[type] = handler;
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            Type type = typeof(T);
            if (!Handlers.TryGetValue(type, out Delegate existing)) return;

            Delegate current = Delegate.Remove(existing, handler);
            if (current == null) Handlers.Remove(type);
            else Handlers[type] = current;
        }

        public static void Publish<T>(T eventData)
        {
            if (Handlers.TryGetValue(typeof(T), out Delegate handler))
                ((Action<T>)handler).Invoke(eventData);
        }

        public static void Clear() => Handlers.Clear();
    }
}
