using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>普通 C# 单例模板。</summary>
    public abstract class Singleton<T> where T : class, new()
    {
        private static T _instance;

        public static T Instance => _instance ??= new T();

        public static bool HasInstance => _instance != null;

        protected Singleton() { }

        public static void Release() => _instance = null;
    }

    /// <summary>MonoBehaviour 单例模板。不存在时自动创建。</summary>
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        private static T _instance;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<T>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject(typeof(T).Name);
                        _instance = go.AddComponent<T>();
                    }
                }
                return _instance;
            }
        }

        public static bool HasInstance => _instance != null;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = (T)this;
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
