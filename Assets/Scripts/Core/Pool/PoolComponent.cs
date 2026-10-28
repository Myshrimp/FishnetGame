using System.Collections.Generic;
using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>
    /// 本地 GameObject 池：用于需要频繁创建/销毁的纯本地对象（特效、掉落物等）。
    /// 注意：网络对象请使用 FishNet 自带的对象池（NetworkManager.GetPooledInstantiated）。
    /// </summary>
    public class PoolComponent : MonoBehaviour, IGameComponent
    {
        /// <summary>记录实例的来源 Prefab，挂在每个池化实例上。</summary>
        public class PooledObject : MonoBehaviour
        {
            public GameObject SourcePrefab;
        }

        private readonly Dictionary<GameObject, Queue<GameObject>> _pools = new Dictionary<GameObject, Queue<GameObject>>(16);
        private Transform _root;

        public void OnInit()
        {
            _root = new GameObject("~Pools").transform;
            _root.SetParent(transform, false);
        }

        public void OnShutdown() => _pools.Clear();

        /// <summary>取出一个实例（没有可用缓存时实例化新的）。</summary>
        public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            GameObject go;
            if (_pools.TryGetValue(prefab, out Queue<GameObject> queue) && queue.Count > 0)
            {
                go = queue.Dequeue();
                go.transform.SetParent(null, false);
                go.transform.SetPositionAndRotation(position, rotation);
                go.SetActive(true);
            }
            else
            {
                go = Instantiate(prefab, position, rotation);
                go.name = prefab.name;
                PooledObject marker = go.GetComponent<PooledObject>();
                if (marker == null) marker = go.AddComponent<PooledObject>();
                marker.SourcePrefab = prefab;
            }

            IPoolable[] poolables = go.GetComponents<IPoolable>();
            for (int i = 0; i < poolables.Length; i++) poolables[i].OnGet();
            return go;
        }

        /// <summary>归还实例（非池化对象会被直接销毁）。</summary>
        public void Return(GameObject go)
        {
            if (go == null) return;
            PooledObject marker = go.GetComponent<PooledObject>();
            if (marker == null || marker.SourcePrefab == null)
            {
                Destroy(go);
                return;
            }

            IPoolable[] poolables = go.GetComponents<IPoolable>();
            for (int i = 0; i < poolables.Length; i++) poolables[i].OnReturn();

            go.SetActive(false);
            go.transform.SetParent(_root, false);

            if (!_pools.TryGetValue(marker.SourcePrefab, out Queue<GameObject> queue))
            {
                queue = new Queue<GameObject>();
                _pools[marker.SourcePrefab] = queue;
            }
            queue.Enqueue(go);
        }
    }
}
