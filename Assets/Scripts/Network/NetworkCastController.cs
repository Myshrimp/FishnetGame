using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;

namespace FaceSlapper.Network.Prediction
{
    /// <summary>
    /// 挂在玩家身上，管理所有释放物（技能/投射物）的施放。
    /// 预测流程：Owner 本地校验通过后立即生成一个纯表现（view-only）实例并预测飞行，
    /// 同时把请求随 Replicate 发给服务端；服务端权威校验后生成权威释放物，
    /// 校验结果随本 tick 的 ReconcileData 返回；Owner 收到回执后按 PredictionKey
    /// 销毁对应 view 实例——允许施放时由网络生成的权威物体接替表现，
    /// 拒绝时即完成预测回滚。
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkCastController : TickNetworkBehaviour
    {
        /// <summary>
        /// 权威释放物可选接口。生成后由服务端调用，注入施法者、方向与初速度。
        /// 不实现该接口的释放物仅按出生点姿态生成。
        /// </summary>
        public interface INetworkCastObject
        {
            void InitializeCast(NetworkObject caster, Vector3 direction, float speed);
        }

        [Serializable]
        private sealed class CastDefinition
        {
            public string Name = "Cast";
            [Tooltip("服务端生成的权威释放物，必须带 NetworkObject 并注册为可生成预制体。")]
            public NetworkObject AuthoritativePrefab;
            [Tooltip("本地预测用的纯表现实例，不得携带 NetworkObject。")]
            public GameObject ViewPrefab;
            [Min(0f)] public float Cooldown = 1f;
            [Tooltip("沿施放方向离出生点的距离。")]
            [Min(0f)] public float SpawnOffset = 0.8f;
            [Tooltip("预测视图的飞行速度；权威物体若实现 INetworkCastObject 也会收到该值。")]
            [Min(0f)] public float Speed = 20f;
        }

        public struct ReplicateData : IReplicateData
        {
            /// <summary>Owner 递增的请求序号，用于把服务端回执对应到本地挂起的 view 实例。</summary>
            public int PredictionKey;
            public bool CastPressed;
            /// <summary>请求使用的释放物槽位。</summary>
            public int CastIndex;
            /// <summary>Owner 瞄准方向，服务端按它确定性生成权威释放物。</summary>
            public Vector3 Direction;

            private uint _tick;

            public uint GetTick()
            {
                return _tick;
            }

            public void SetTick(uint value)
            {
                _tick = value;
            }

            public void Dispose()
            {
                CastPressed = false;
                Direction = Vector3.zero;
            }
        }

        public struct ReconcileData : IReconcileData
        {
            /// <summary>本 tick 服务端处理到的施放请求序号；0 表示本 tick 没有请求。</summary>
            public int ConfirmedKey;
            /// <summary>服务端是否允许施放。false 时客户端销毁对应 view 实例。</summary>
            public bool CanCast;

            private uint _tick;

            public uint GetTick()
            {
                return _tick;
            }

            public void SetTick(uint value)
            {
                _tick = value;
            }

            public void Dispose() { }
        }

        /// <summary>本地挂起的预测视图，等待服务端回执或超时销毁。</summary>
        private struct PendingView
        {
            public GameObject View;
            public Vector3 Direction;
            public float Speed;
            public float ExpiryTime;
        }

        [Header("Casting")]
        [SerializeField] private List<CastDefinition> _casts = new();
        [Tooltip("施放出生点，未设置时使用自身。")]
        [SerializeField] private Transform _castOrigin;
        [SerializeField] private KeyCode _castKey = KeyCode.R;

        [Header("Prediction View")]
        [Tooltip("预测视图最长存活时间。回执走不可靠通道，丢包时由超时兜底销毁。")]
        [SerializeField, Min(0.1f)] private float _viewTimeout = 2f;

        private readonly Dictionary<int, PendingView> _pendingViews = new();
        private readonly List<int> _viewRemovalCache = new();
        private PlayerControllerPrediction _motor;
        private int _predKey;
        private int _selectedIndex;
        private bool _castPressed;
        private float _nextLocalCastTime;
        private uint _serverNextCastTick;
        private int _serverConfirmedKey;
        private bool _serverCanCast;

        public int SelectedIndex => _selectedIndex;
        public int CastCount => _casts.Count;

        private void Awake()
        {
            _motor = GetComponent<PlayerControllerPrediction>();
            if (_castOrigin == null)
                _castOrigin = transform;

            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        private void OnDestroy()
        {
            ClearPendingViews();
        }

        public override void OnStopNetwork()
        {
            ClearPendingViews();
            _castPressed = false;
            _serverConfirmedKey = 0;
            _serverCanCast = false;
        }

        /// <summary>切换当前选中的释放物槽位，仅影响本地输入，服务端以请求中的槽位为准。</summary>
        public void SelectCast(int index)
        {
            if (_casts.Count == 0)
                return;

            _selectedIndex = Mathf.Clamp(index, 0, _casts.Count - 1);
        }

        private void Update()
        {
            UpdatePendingViews(Time.deltaTime);

            if (!IsOwner)
                return;

            if (UnityEngine.Input.GetKeyDown(_castKey))
                _castPressed = true;

            float scroll = UnityEngine.Input.mouseScrollDelta.y;
            if (_casts.Count > 1 && scroll != 0f)
            {
                int step = scroll > 0f ? 1 : -1;
                _selectedIndex = (_selectedIndex + step + _casts.Count) % _casts.Count;
            }
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(BuildCastData());
        }

        protected override void TimeManager_OnPostTick()
        {
            CreateReconcile();
        }

        /// <summary>
        /// Owner 构建施放请求。本地校验（冷却/眩晕/被携带）只做表现层防抖，
        /// 不通过则不生成 view 也不发请求；权威判定在服务端。
        /// </summary>
        private ReplicateData BuildCastData()
        {
            if (!IsOwner || !_castPressed)
                return default;

            _castPressed = false;

            if (_casts.Count == 0 || Time.time < _nextLocalCastTime || IsCastBlocked())
                return default;

            int index = Mathf.Clamp(_selectedIndex, 0, _casts.Count - 1);
            CastDefinition def = _casts[index];
            Vector3 direction = transform.forward;

            int key = ++_predKey;
            _nextLocalCastTime = Time.time + def.Cooldown;
            SpawnPredictionView(key, def, direction);

            return new ReplicateData
            {
                PredictionKey = key,
                CastPressed = true,
                CastIndex = index,
                Direction = direction
            };
        }

        [Replicate]
        private void PerformReplicate(
            ReplicateData rd,
            ReplicateState state = ReplicateState.Invalid,
            Channel channel = Channel.Unreliable)
        {
            /* 视图预测已在 Owner 的 Tick 中完成，这里只有服务端响应施放请求。
             * ContainsCreated 保证同一份输入不会被重复结算。 */
            if (!IsServerStarted || !state.ContainsCreated() || !rd.CastPressed)
                return;

            bool canCast = ValidateCast(rd.CastIndex);
            if (canCast)
            {
                CastDefinition def = _casts[rd.CastIndex];
                SpawnAuthoritative(def, rd.Direction);

                uint cooldownSteps = (uint)Mathf.Max(1, Mathf.CeilToInt(def.Cooldown / (float)TimeManager.TickDelta));
                _serverNextCastTick = TimeManager.LocalTick + cooldownSteps;
            }

            // 结果交给本 tick 的 Reconcile 带回给 Owner。
            _serverConfirmedKey = rd.PredictionKey;
            _serverCanCast = canCast;
        }

        public override void CreateReconcile()
        {
            ReconcileData rd = new()
            {
                ConfirmedKey = _serverConfirmedKey,
                CanCast = _serverCanCast
            };
            _serverConfirmedKey = 0;
            _serverCanCast = false;

            PerformReconcile(rd);
        }

        [Reconcile]
        private void PerformReconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
        {
            // 视图清理只发生在持有预测的 Owner 客户端。
            if (IsServerStarted || !IsOwner || rd.ConfirmedKey == 0)
                return;

            if (!_pendingViews.TryGetValue(rd.ConfirmedKey, out PendingView pending))
                return;

            _pendingViews.Remove(rd.ConfirmedKey);
            if (pending.View != null)
                Destroy(pending.View);

            /* CanCast == true：权威释放物已由服务端生成，将随网络消息到达并接替表现。
             * CanCast == false：请求被拒绝（冷却/眩晕等），视图已销毁，预测回滚完成。 */
        }

        /// <summary>服务端权威校验：槽位合法、冷却结束、未处于无法行动状态。</summary>
        private bool ValidateCast(int castIndex)
        {
            if (castIndex < 0 || castIndex >= _casts.Count)
                return false;
            if (TimeManager.LocalTick < _serverNextCastTick)
                return false;
            return !IsCastBlocked();
        }

        private bool IsCastBlocked()
        {
            return _motor != null && (_motor.IsStunned || _motor.IsCarried);
        }

        private void SpawnAuthoritative(CastDefinition def, Vector3 direction)
        {
            if (def.AuthoritativePrefab == null)
            {
                Debug.LogError($"Cast '{def.Name}' has no authoritative prefab assigned.", this);
                return;
            }

            Vector3 position = GetSpawnPosition(def, direction);
            NetworkObject nob = Instantiate(
                def.AuthoritativePrefab,
                position,
                Quaternion.LookRotation(direction));
            ServerManager.Spawn(nob);

            if (nob.TryGetComponent(out INetworkCastObject castObject))
                castObject.InitializeCast(NetworkObject, direction, def.Speed);
        }

        /// <summary>生成纯表现预测视图。Host 同 tick 内即可生成权威物体，跳过视图避免双重表现。</summary>
        private void SpawnPredictionView(int key, CastDefinition def, Vector3 direction)
        {
            if (IsServerStarted || def.ViewPrefab == null)
                return;

            Vector3 position = GetSpawnPosition(def, direction);
            GameObject view = Instantiate(
                def.ViewPrefab,
                position,
                Quaternion.LookRotation(direction));

            _pendingViews[key] = new PendingView
            {
                View = view,
                Direction = direction,
                Speed = def.Speed,
                ExpiryTime = Time.time + _viewTimeout
            };
        }

        private Vector3 GetSpawnPosition(CastDefinition def, Vector3 direction)
        {
            return _castOrigin.position + direction * def.SpawnOffset;
        }

        /// <summary>与 PlayerProjectileController 相同的瞄准约定：主相机前向，缺失时用角色前向。</summary>
        private Vector3 GetAimDirection()
        {
            UnityEngine.Camera playerCamera = UnityEngine.Camera.main;
            Vector3 direction = playerCamera != null ? playerCamera.transform.forward : transform.forward;
            if (direction.sqrMagnitude < 0.001f)
                direction = transform.forward;
            return direction.normalized;
        }

        /// <summary>驱动预测视图飞行，并销毁超时未收到回执的视图。</summary>
        private void UpdatePendingViews(float deltaTime)
        {
            if (_pendingViews.Count == 0)
                return;

            float now = Time.time;
            foreach (KeyValuePair<int, PendingView> pair in _pendingViews)
            {
                PendingView pending = pair.Value;
                if (pending.View == null || now >= pending.ExpiryTime)
                {
                    _viewRemovalCache.Add(pair.Key);
                    if (pending.View != null)
                        Destroy(pending.View);
                    continue;
                }

                pending.View.transform.position += pending.Direction * (pending.Speed * deltaTime);
            }

            foreach (int key in _viewRemovalCache)
                _pendingViews.Remove(key);
            _viewRemovalCache.Clear();
        }

        private void ClearPendingViews()
        {
            foreach (KeyValuePair<int, PendingView> pair in _pendingViews)
            {
                if (pair.Value.View != null)
                    Destroy(pair.Value.View);
            }
            _pendingViews.Clear();
        }
    }
}
