using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;

namespace FaceSlapper.Network.Prediction
{
    /// <summary>
    /// 服务端驱动的直线飞行释放物：无 Owner，由服务端充当 controller 模拟，
    /// 客户端通过 Reconcile 恢复位置/方向/速度后回放预测。
    /// 移动与反弹只依赖 Reconcile 恢复的状态，因此首次模拟与回放走同一条确定性路径。
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkCastable : TickNetworkBehaviour, NetworkCastController.INetworkCastObject
    {
        #region CollisionDetection
        [SerializeField]
        private float _detectionRadius;
        [SerializeField]
        private LayerMask _collisionMask;
        #endregion

        [SerializeField] private float _speed;
        [SerializeField] private float _remainingSecs = 3;
        
        private uint _remainingTicks;
        private Vector3 _direction;
        private bool _wasOverlapping;

        private void Awake()
        {
            SetTickCallbacks(TickCallback.PostTick | TickCallback.Tick);
        }

        /// <summary>兜底初始化寿命，覆盖未经 InitializeCast 生成的实例（场景摆放/测试）。</summary>
        public override void OnStartServer()
        {
            if (_remainingTicks == 0)
                _remainingTicks = (uint)Mathf.CeilToInt(_remainingSecs / (float)TimeManager.TickDelta);
        }

        public void SetDirection(Vector3 direction)
        {
            _direction = direction;
        }

        public struct ReplicateData : IReplicateData
        {
            /* 该对象无 Owner，没有任何客户端输入需要上传；
             * 保留空结构仅为满足 [Replicate] 签名，tick 由 FishNet 填充。 */
            private uint _tick;

            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public struct ReconcileData : IReconcileData
        {
            public Vector3 Position;
            public Quaternion Rotation;
            /// <summary>飞行方向。只在服务端生成时赋值，客户端只能通过 Reconcile 拿到。</summary>
            public Vector3 Direction;
            /// <summary>当前速度（含反弹后的符号）。</summary>
            public float Speed;
            /// <summary>上一步是否处于碰撞范围内。反弹沿检测状态，回放时必须一并恢复。</summary>
            public bool WasOverlapping;

            public uint RemainingTicks;

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(default);
        }

        protected override void TimeManager_OnPostTick()
        {
            CreateReconcile();
        }

        public override void CreateReconcile()
        {
            ReconcileData data = new ReconcileData
            {
                Position = transform.position,
                Rotation = transform.rotation,
                Direction = _direction,
                Speed = _speed,
                WasOverlapping = _wasOverlapping,
                RemainingTicks = _remainingTicks
            };
            PerformReconcile(data);
        }

        [Replicate]
        private void PerformReplicate(ReplicateData data, ReplicateState state = ReplicateState.Invalid,
            Channel channel = Channel.Unreliable)
        {
            float delta = (float)TimeManager.TickDelta;
            transform.position += _direction * _speed * delta;

            /* 只在进入碰撞范围的沿上反弹一次；
             * 持续重叠时反复翻转会让物体卡在墙内来回抖动。 */
            bool overlapping = Physics.CheckSphere(transform.position, _detectionRadius, _collisionMask);
            if (overlapping && !_wasOverlapping)
                _speed *= -1f;
            _wasOverlapping = overlapping;

            if (_remainingTicks > 0)
                _remainingTicks--;

            /* Despawn 只能由服务端发起。客户端预测领先服务端，
             * 必然先数到 0，绝不能走这条路。 */
            if (IsServerStarted && _remainingTicks == 0)
                ServerManager.Despawn(NetworkObject);
        }

        [Reconcile]
        private void PerformReconcile(ReconcileData data, Channel channel = Channel.Unreliable)
        {
            transform.position = data.Position;
            transform.rotation = data.Rotation;
            _direction = data.Direction;
            _speed = data.Speed;
            _wasOverlapping = data.WasOverlapping;
            _remainingTicks = data.RemainingTicks;
        }

        public void InitializeCast(NetworkObject caster, Vector3 direction, float speed)
        {
            SetDirection(direction);
            // 施放配置里的速度优先；为 0 时保留预制体上的序列化默认值。
            if (speed > 0f)
                _speed = speed;
            // 寿命已在 OnStartServer 初始化，此处不再重复赋值。
        }
    }
}
