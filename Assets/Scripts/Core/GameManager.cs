using System;
using System.Collections.Generic;
using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>
    /// 全局唯一单例，跨场景不销毁。维护组件注册表，并统一分发
    /// Update / FixedUpdate / LateUpdate 生命周期给实现了对应接口的组件。
    /// </summary>
    public class GameManager : MonoSingleton<GameManager>
    {
        private readonly Dictionary<Type, IGameComponent> _components = new Dictionary<Type, IGameComponent>(32);
        private readonly List<IUpdatable> _updatables = new List<IUpdatable>(16);
        private readonly List<IFixedUpdatable> _fixedUpdatables = new List<IFixedUpdatable>(8);
        private readonly List<ILateUpdatable> _lateUpdatables = new List<ILateUpdatable>(8);

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>注册组件（幂等，重复注册返回已有实例）。</summary>
        public T Register<T>(T component) where T : class, IGameComponent
        {
            Type type = typeof(T);
            if (_components.TryGetValue(type, out IGameComponent existing))
                return (T)existing;

            _components[type] = component;
            if (component is IUpdatable u) _updatables.Add(u);
            if (component is IFixedUpdatable f) _fixedUpdatables.Add(f);
            if (component is ILateUpdatable l) _lateUpdatables.Add(l);
            component.OnInit();
            return component;
        }

        /// <summary>AddComponent 并注册到 GameManager。</summary>
        public T AddAndRegister<T>() where T : Component, IGameComponent
        {
            T component = GetComponent<T>();
            if (component == null) component = gameObject.AddComponent<T>();
            return Register(component);
        }

        /// <summary>获取已注册的组件。</summary>
        public T Get<T>() where T : class, IGameComponent
        {
            return _components.TryGetValue(typeof(T), out IGameComponent component) ? (T)component : null;
        }

        public bool Has<T>() where T : class, IGameComponent => _components.ContainsKey(typeof(T));

        /// <summary>反注册并关闭组件。</summary>
        public void Unregister<T>() where T : class, IGameComponent
        {
            Type type = typeof(T);
            if (!_components.TryGetValue(type, out IGameComponent component)) return;

            if (component is IUpdatable u) _updatables.Remove(u);
            if (component is IFixedUpdatable f) _fixedUpdatables.Remove(f);
            if (component is ILateUpdatable l) _lateUpdatables.Remove(l);
            component.OnShutdown();
            _components.Remove(type);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _updatables.Count; i++) _updatables[i].OnUpdate(dt);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            for (int i = 0; i < _fixedUpdatables.Count; i++) _fixedUpdatables[i].OnFixedUpdate(dt);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _lateUpdatables.Count; i++) _lateUpdatables[i].OnLateUpdate(dt);
        }

        protected override void OnDestroy()
        {
            // 反向顺序关闭所有组件。
            var all = new List<IGameComponent>(_components.Values);
            for (int i = all.Count - 1; i >= 0; i--) all[i].OnShutdown();
            _components.Clear();
            _updatables.Clear();
            _fixedUpdatables.Clear();
            _lateUpdatables.Clear();
            base.OnDestroy();
        }
    }
}
