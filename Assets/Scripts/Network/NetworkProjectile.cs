using FaceSlapper.Network.Prediction;
using FishNet.Component.Prediction;
using FishNet.Component.Transforming;
using FishNet.Component.Transforming.Beta;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using FishNet.Utility.Template;
using GameKit.Dependencies.Utilities;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class NetworkProjectile : TickNetworkBehaviour
{
    public struct ProjectileReplicateData : IReplicateData
    {
        /// <summary>One-shot velocity change applied on the throw tick.</summary>
        public Vector3 ThrowVelocity;
        private uint _tick;

        public void Dispose()
        {
            ThrowVelocity = Vector3.zero;
        }

        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }

    public struct ProjectileReconcileData : IReconcileData
    {
        public PredictionRigidbody Rigidbody;
        public bool IsThrown;
        private uint _tick;

        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }

    [Header("Synchronization")]
    [Tooltip("Select before starting the network. Enabled: FishNet prediction. Disabled: RPC events and local physics.")]
    [SerializeField] private bool _usePrediction = true;

    [Header("Presentation")]
    [SerializeField] private Transform _graphicalObject;
    [SerializeField, Range(1, 8)] private byte _interpolationTicks = 2;
    [SerializeField] private AdaptiveInterpolationType _adaptiveInterpolation = AdaptiveInterpolationType.Off;

    [Header("Holding")]
    [SerializeField] private Vector3 _localHoldPosition = new(0.55f, 1.1f, 0.75f);
    [SerializeField] private Vector3 _localHoldEulerAngles;
    [SerializeField, Min(0.1f)] private float _pickupDistance = 2.5f;
    [SerializeField, Min(0f)] private float _throwSpawnOffset = 0.3f;

    [Header("Throwing")]
    [SerializeField, Min(0.1f)] private float _throwSpeed = 18f;
    [SerializeField, Min(0f)] private float _gravity = 9.8f;
    [SerializeField, Min(0f)] private float _ignoreThrowerDuration = 0.35f;

    [Header("Hit")]
    [SerializeField, Min(0f)] private float _knockbackSpeed = 12f;
    [SerializeField, Min(0f)] private float _upwardSpeed = 5f;
    [SerializeField, Min(0f)] private float _stunDuration = 1.25f;

    [Header("Friction")]
    [SerializeField, Min(0f)] private float _airFrictionRate = 0.5f;
    [SerializeField] private float _maxFrictionForce = 5f;

    [Header("Smoothing Diagnostics")]
    [SerializeField] private bool _enableSmoothingLogs = true;
    [SerializeField, Min(0.1f)] private float _smoothingLogInterval = 0.5f;

    private readonly SyncVar<NetworkObject> _holder = new();
    private readonly PredictionRigidbody _prediction = new();

    private Rigidbody _rb;
    private Collider[] _colliders;
    private ProjectileReplicateData _lastReplicateData;
    private Vector3 _throwVelocity;
    private ForceMode _rpcThrowForceMode;
    private NetworkObject _thrower;
    private float _ignoreThrowerUntil;
    private bool _isThrown;
    private bool _hasHit;
    private Vector3 _counterForce;
    private float _nextSmoothingLogTime;
    private float _maxRootFrameStep;
    private float _maxViewFrameStep;
    private Vector3 _lastRootPosition;
    private Vector3 _lastViewPosition;
    private bool _hasSmoothingPositionSample;
    private TickSmootherController _smoother;
    private bool _smoothingActive;
    private bool _presentationHeld;
    private bool _rpcUsesUnityPhysics;
    private Vector3 _viewLocalPosition;
    private Quaternion _viewLocalRotation;
    private Vector3 _viewLocalScale;
    private Pose? _releaseViewPose;
    private Pose _releaseLocalPose;
    private float _releaseBlendRemaining;
    private RigidbodyPauser _rpcReplayPauser;
    private PredictionManager _rpcReplayManager;

    public bool IsHeld => _holder.Value != null;
    public bool IsThrown => _isThrown;
    public NetworkObject Holder => _holder.Value;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.None;
        _colliders = GetComponentsInChildren<Collider>(true);
        if (_graphicalObject == null)
            _graphicalObject = NetworkObject.GetGraphicalObject();
        NetworkObject.SetGraphicalObject(null);
        if (_graphicalObject != null && _graphicalObject != transform)
        {
            _viewLocalPosition = _graphicalObject.localPosition;
            _viewLocalRotation = _graphicalObject.localRotation;
            _viewLocalScale = _graphicalObject.localScale;
        }
        if (_usePrediction)
            _prediction.Initialize(_rb);
        _holder.OnChange += OnHolderChanged;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        UnityEditor.EditorApplication.delayCall -= ValidateNetworkConfiguration;
        UnityEditor.EditorApplication.delayCall += ValidateNetworkConfiguration;
    }

    private void ValidateNetworkConfiguration()
    {
        if (this == null || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        NetworkObject networkObject = GetComponent<NetworkObject>();
        if (networkObject == null)
            return;

        if (_graphicalObject == null && networkObject.GetGraphicalObject() != null)
        {
            _graphicalObject = networkObject.GetGraphicalObject();
            UnityEditor.EditorUtility.SetDirty(this);
        }

        // FishNet 4.7 configures prediction before network callbacks; there is no runtime setter.
        var settings = new UnityEditor.SerializedObject(networkObject);
        settings.FindProperty("_enablePrediction").boolValue = _usePrediction;
        settings.FindProperty("_enableStateForwarding").boolValue = _usePrediction;
        settings.FindProperty("_predictionType").enumValueIndex = 1;
        settings.FindProperty("_graphicalObject").objectReferenceValue = null;
        settings.ApplyModifiedProperties();
    }
#endif

    private void OnDestroy()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= ValidateNetworkConfiguration;
#endif
        _holder.OnChange -= OnHolderChanged;
        StopRpcReplayIsolation();
        DisposeSmoother();
    }

    public override void OnStartNetwork()
    {
        if (NetworkObject.EnablePrediction != _usePrediction)
        {
            Debug.LogError("Projectile prediction configuration does not match its sync mode. Configure and save it before spawning.", this);
            SetTickCallbacks(TickCallback.None);
            return;
        }

        _rpcUsesUnityPhysics = !_usePrediction && TimeManager.PhysicsMode == PhysicsMode.Unity;
        SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        ApplyHolderState(IsHeld);
    }

    public override void OnStartClient()
    {
        if (!_usePrediction && !_rpcUsesUnityPhysics)
        {
            _rpcReplayPauser = new RigidbodyPauser();
            _rpcReplayPauser.UpdateRigidbodies(transform, RigidbodyType.Rigidbody, false);
            _rpcReplayManager = PredictionManager;
            _rpcReplayManager.OnPreReconcile += PauseRpcBody;
            _rpcReplayManager.OnPostReconcile += ResumeRpcBody;
        }

        if (_graphicalObject == null || _graphicalObject.parent != transform)
        {
            Debug.LogError("Projectile Graphical Object must be a direct child of the physics root.", this);
            return;
        }

        if (!_rpcUsesUnityPhysics)
        {
            InitializationSettings settings = new()
            {
                TargetTransform = transform,
                DetachOnStart = true,
                AttachOnStop = true
            };
            settings.SetNetworkedRuntimeValues(this, _graphicalObject, false);
            MovementSettings movement = new(true)
            {
                InterpolationValue = _interpolationTicks,
                SmoothedProperties = TransformPropertiesFlag.Position | TransformPropertiesFlag.Rotation
            };
            MovementSettings spectator = movement;
            spectator.AdaptiveInterpolationValue = _usePrediction
                ? _adaptiveInterpolation : AdaptiveInterpolationType.Off;
            _smoother = new TickSmootherController();
            _smoother.Initialize(settings, movement, spectator);
        }
        LogSmoothingStart();
    }

    public override void OnStopClient()
    {
        StopRpcReplayIsolation();
        DisposeSmoother();
    }

    public override void OnStopNetwork()
    {
        _throwVelocity = Vector3.zero;
    }

    private void PauseRpcBody(uint clientTick, uint serverTick)
    {
        _rpcReplayPauser.Pause();
    }

    private void ResumeRpcBody(uint clientTick, uint serverTick)
    {
        _rpcReplayPauser.Unpause();
    }

    private void StopRpcReplayIsolation()
    {
        if (_rpcReplayManager != null)
        {
            _rpcReplayManager.OnPreReconcile -= PauseRpcBody;
            _rpcReplayManager.OnPostReconcile -= ResumeRpcBody;
            _rpcReplayManager = null;
        }
        _rpcReplayPauser?.Unpause();
        _rpcReplayPauser = null;
    }

    private void DisposeSmoother()
    {
        StopFlightSmoothing();
        _smoother?.OnDestroy();
        _smoother = null;
        _releaseViewPose = null;
        _releaseBlendRemaining = 0f;
        RestoreViewTransform();
    }

    private void StopFlightSmoothing()
    {
        if (!_smoothingActive)
            return;

        _smoother.StopSmoother();
        _smoothingActive = false;
    }

    private void RestoreViewTransform()
    {
        if (_graphicalObject == null || _graphicalObject == transform)
            return;

        _graphicalObject.localPosition = _viewLocalPosition;
        _graphicalObject.localRotation = _viewLocalRotation;
        _graphicalObject.localScale = _viewLocalScale;
    }

    private void Update()
    {
        if ((IsServerStarted || !_usePrediction) && _thrower != null
            && Time.time >= _ignoreThrowerUntil)
        {
            RestoreThrowerCollisions();
        }
    }

    private void SimulateRpcPhysics()
    {
        if (_presentationHeld || _rb.isKinematic)
            return;

        if (_throwVelocity != Vector3.zero)
        {
            _rb.AddForce(_throwVelocity, _rpcThrowForceMode);
            _throwVelocity = Vector3.zero;
        }
        _rb.AddForce(new Vector3(0f, -_gravity, 0f), ForceMode.Acceleration);
        Vector3 counterForce = -_rb.velocity * _airFrictionRate;
        counterForce = counterForce.magnitude > _maxFrictionForce
            ? counterForce.normalized * _maxFrictionForce
            : counterForce;
        _rb.AddForce(counterForce);
    }

    private void FixedUpdate()
    {
        if (!_rpcUsesUnityPhysics || !IsSpawned)
            return;

        if (_presentationHeld)
            UpdateHeldTransform();
        else
            SimulateRpcPhysics();
    }

    private void LateUpdate()
    {
        if (_presentationHeld)
        {
            NetworkObject holder = _holder.Value;
            if (_graphicalObject != null && _graphicalObject != transform
                && holder != null && holder.IsSpawned)
            {
                Quaternion rotation = GetHoldRotation(holder, true);
                Vector3 offset = Vector3.Scale(_viewLocalPosition, transform.lossyScale);
                _graphicalObject.SetPositionAndRotation(
                    GetHoldPosition(holder, true) + rotation * offset,
                    rotation * _viewLocalRotation);
            }
        }
        else if (_smoother != null && !_smoothingActive)
        {
            // Start after the release pose/RPC has been applied, never from the old held pose.
            RestoreViewTransform();
            Vector3 worldScale = _graphicalObject.lossyScale;
            _smoother.StartSmoother();
            // FishNet 4.7's detach restores local scale rather than world scale.
            _graphicalObject.localScale = worldScale;
            if (_releaseViewPose.HasValue)
            {
                Pose pose = _releaseViewPose.Value;
                _graphicalObject.SetPositionAndRotation(pose.position, pose.rotation);
                _releaseViewPose = null;
            }
            _smoothingActive = true;
        }
        else if (_rpcUsesUnityPhysics && _graphicalObject != null)
        {
            UpdateUnityReleaseBlend();
        }

        UpdateSmoothingDiagnostics();
    }

    private void UpdateUnityReleaseBlend()
    {
        float duration = Time.fixedDeltaTime * _interpolationTicks;
        if (_releaseViewPose.HasValue)
        {
            Pose pose = _releaseViewPose.Value;
            _releaseLocalPose = new Pose(
                transform.InverseTransformPoint(pose.position),
                Quaternion.Inverse(transform.rotation) * pose.rotation);
            _releaseBlendRemaining = duration;
            _releaseViewPose = null;
        }

        if (_releaseBlendRemaining <= 0f)
        {
            RestoreViewTransform();
            return;
        }

        float progress = 1f - _releaseBlendRemaining / duration;
        _graphicalObject.localPosition = Vector3.Lerp(_releaseLocalPose.position, _viewLocalPosition, progress);
        _graphicalObject.localRotation = Quaternion.Slerp(_releaseLocalPose.rotation, _viewLocalRotation, progress);
        _releaseBlendRemaining = Mathf.Max(0f, _releaseBlendRemaining - Time.deltaTime);
    }

    private void LogSmoothingStart()
    {
        if (!_enableSmoothingLogs)
            return;

        Transform view = _graphicalObject;
        bool smootherCreated = _smoother != null;
        bool pauserCreated = NetworkObject.RigidbodyPauser != null;
        Debug.Log(
            $"[GrenadeSmoothing][Start] SyncMode={(_usePrediction ? "Prediction" : "RPC")}, " +
            $"Server={IsServerStarted}, Client={IsClientStarted}, " +
            $"Prediction={NetworkObject.EnablePrediction}, StateForwarding={NetworkObject.EnableStateForwarding}, " +
            $"Smoother={smootherCreated}, Pauser={pauserCreated}, View={(view != null ? view.name : "null")}, " +
            $"RpcReplayIsolation={_rpcReplayPauser != null}, " +
            $"Interpolation={_interpolationTicks}, Adaptive={(_usePrediction ? _adaptiveInterpolation : AdaptiveInterpolationType.Off)}, " +
            $"PhysicsMode={TimeManager.PhysicsMode}, RigidbodyInterpolation={_rb.interpolation}, " +
            $"TickRate={(TimeManager != null ? TimeManager.TickRate : 0)}, " +
            $"TickDelta={(TimeManager != null ? TimeManager.TickDelta : 0d):F4}, " +
            $"FrictionRate={_airFrictionRate:F2}, MaxFriction={_maxFrictionForce:F2}",
            this);
    }

    private void UpdateSmoothingDiagnostics()
    {
        if (!_enableSmoothingLogs || !IsClientStarted)
            return;

        Transform view = _graphicalObject;
        Vector3 rootPosition = transform.position;
        Vector3 viewPosition = view != null ? view.position : rootPosition;

        if (_hasSmoothingPositionSample)
        {
            _maxRootFrameStep = Mathf.Max(_maxRootFrameStep, Vector3.Distance(_lastRootPosition, rootPosition));
            _maxViewFrameStep = Mathf.Max(_maxViewFrameStep, Vector3.Distance(_lastViewPosition, viewPosition));
        }

        _lastRootPosition = rootPosition;
        _lastViewPosition = viewPosition;
        _hasSmoothingPositionSample = true;

        if (Time.unscaledTime < _nextSmoothingLogTime)
            return;

        _nextSmoothingLogTime = Time.unscaledTime + _smoothingLogInterval;
        bool smootherCreated = _smoother != null;
        bool pauserCreated = NetworkObject.RigidbodyPauser != null;
        bool paused = pauserCreated && NetworkObject.RigidbodyPauser.Paused;
        Debug.Log(
            $"[GrenadeSmoothing][Sample] Owner={IsOwner}, Held={IsHeld}, Thrown={_isThrown}, " +
            $"Smoother={smootherCreated}, SmoothingActive={_smoothingActive}, Reconciling={NetworkObject.IsObjectReconciling}, " +
            $"Pauser={pauserCreated}, Paused={paused}, Kinematic={_rb.isKinematic}, " +
            $"RootMaxStep={_maxRootFrameStep:F4}, ViewMaxStep={_maxViewFrameStep:F4}, " +
            $"RootViewOffset={Vector3.Distance(rootPosition, viewPosition):F4}, " +
            $"ViewParent={(view != null && view.parent != null ? view.parent.name : "null")}",
            this);

        _maxRootFrameStep = 0f;
        _maxViewFrameStep = 0f;
    }

    protected override void TimeManager_OnTick()
    {
        if (!_usePrediction)
        {
            if (!_rpcUsesUnityPhysics)
                SimulateRpcPhysics();
            return;
        }

        ProjectileReplicateData rd = new()
        {
            ThrowVelocity = _throwVelocity
        };
        _throwVelocity = Vector3.zero;
        PerformReplicate(rd);
    }

    protected override void TimeManager_OnPostTick()
    {
        if (_presentationHeld && !_rpcUsesUnityPhysics)
            UpdateHeldTransform();
        if (_usePrediction)
            CreateReconcile();
    }

    [Replicate]
    private void PerformReplicate(
        ProjectileReplicateData rd,
        ReplicateState state = ReplicateState.Invalid,
        Channel channel = Channel.Unreliable)
    {
        if (!_usePrediction || IsHeld)
            return;

        if (!IsServerStarted && !IsOwner)
        {
            if (state.ContainsTicked())
            {
                _lastReplicateData.Dispose();
                _lastReplicateData = rd;
            }
            else if (state.IsFuture() && rd.GetTick() - _lastReplicateData.GetTick() <= 1)
            {
                rd.Dispose();
                rd = _lastReplicateData;
                // A throw is a one-time input and must never be predicted twice.
                rd.ThrowVelocity = Vector3.zero;
            }
        }

        if (rd.ThrowVelocity.sqrMagnitude > 0.001f)
        {
            _prediction.AddForce(rd.ThrowVelocity, ForceMode.VelocityChange);
        }

        _prediction.AddForce(new Vector3(0f, -_gravity, 0f), ForceMode.Acceleration);
        _counterForce = -_rb.velocity * _airFrictionRate;
        _counterForce = _counterForce.magnitude > _maxFrictionForce ? _counterForce.normalized * _maxFrictionForce : _counterForce;
        // Apply the same drag during prediction to avoid continuous reconcile corrections.
        _prediction.AddForce(_counterForce);
            
        _prediction.Simulate();
    }

    public override void CreateReconcile()
    {
        if (!_usePrediction)
            return;

        ProjectileReconcileData rd = new()
        {
            Rigidbody = _prediction,
            IsThrown = _isThrown
        };
        PerformReconcile(rd);
    }

    [Reconcile]
    private void PerformReconcile(ProjectileReconcileData rd, Channel channel = Channel.Unreliable)
    {
        if (!_usePrediction)
            return;

        _isThrown = rd.IsThrown;
        _prediction.Reconcile(rd.Rigidbody);
    }

    /// <summary>Server-side pickup entry for weapons, AI, or interaction systems.</summary>
    public void Hold(NetworkObject holder)
    {
        if (!IsServerStarted || !CanHold(holder))
            return;

        RestoreThrowerCollisions();
        _holder.Value = holder;
        _throwVelocity = Vector3.zero;
        _isThrown = false;
        _hasHit = false;
        UpdateHeldTransform();
    }

    /// <summary>Server-side throw entry. Direction is normalized before use.</summary>
    public void Throw(Vector3 direction)
    {
        if (!IsServerStarted || !IsHeld)
            return;

        NetworkObject holder = _holder.Value;
        if (direction.sqrMagnitude < 0.001f)
            direction = holder.transform.forward;

        direction.Normalize();
        Vector3 throwPosition = GetHoldPosition(holder) + direction * _throwSpawnOffset;
        Quaternion throwRotation = GetHoldRotation(holder);
        Vector3 throwVelocity = direction * _throwSpeed;

        _thrower = holder;
        _holder.Value = null;
        _isThrown = true;
        _hasHit = false;

        if (_usePrediction)
        {
            SetLocalPose(throwPosition, throwRotation);
            _throwVelocity = throwVelocity;
            IgnoreThrowerCollisions();
        }
        else
        {
            ThrowObservers(throwPosition, throwRotation, throwVelocity,
                ForceMode.VelocityChange, holder);
        }
    }

    /// <summary>Releases the projectile without throwing velocity or hit activation.</summary>
    public void Drop()
    {
        if (!IsServerStarted || !IsHeld)
            return;

        NetworkObject holder = _holder.Value;
        RestoreThrowerCollisions();
        _thrower = holder;
        _holder.Value = null;
        _throwVelocity = Vector3.zero;
        _isThrown = false;
        _hasHit = false;

        Vector3 dropPosition = GetHoldPosition(holder);
        Quaternion dropRotation = GetHoldRotation(holder);
        if (_usePrediction)
        {
            SetLocalPose(dropPosition, dropRotation);
            IgnoreThrowerCollisions();
        }
        else
        {
            DropObservers(dropPosition, dropRotation, holder);
        }
    }

    [ObserversRpc(RunLocally = true)]
    private void ThrowObservers(Vector3 position, Quaternion rotation, Vector3 force,
        ForceMode forceMode, NetworkObject thrower)
    {
        RestoreThrowerCollisions();
        _thrower = thrower;
        ApplyHolderState(false);
        SetLocalPose(position, rotation);

        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        // Apply after reconcile, immediately before the next real physics step.
        _throwVelocity = force;
        _rpcThrowForceMode = forceMode;
        _isThrown = true;
        _hasHit = false;
        IgnoreThrowerCollisions();
    }

    [ObserversRpc(RunLocally = true)]
    private void DropObservers(Vector3 position, Quaternion rotation, NetworkObject thrower)
    {
        RestoreThrowerCollisions();
        _thrower = thrower;
        ApplyHolderState(false);
        SetLocalPose(position, rotation);

        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _throwVelocity = Vector3.zero;
        _isThrown = false;
        _hasHit = false;
        IgnoreThrowerCollisions();
    }

    private void SetLocalPose(Vector3 position, Quaternion rotation)
    {
        StopFlightSmoothing();
        transform.SetPositionAndRotation(position, rotation);
        _rb.position = position;
        _rb.rotation = rotation;
        RestoreViewTransform();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestHold(NetworkObject holder, NetworkConnection caller = null)
    {
        if (!CallerOwnsHolder(holder, caller) || !WithinPickupRange(holder))
            return;

        Hold(holder);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestThrow(Vector3 direction, NetworkConnection caller = null)
    {
        if (!CallerOwnsHolder(_holder.Value, caller))
            return;

        Throw(direction);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestDrop(NetworkConnection caller = null)
    {
        if (!CallerOwnsHolder(_holder.Value, caller))
            return;

        Drop();
    }

    private bool CanHold(NetworkObject holder)
    {
        if (holder == null || !holder.IsSpawned || holder == NetworkObject)
            return false;

        if (IsHeld || !WithinPickupRange(holder))
            return false;

        return holder.GetComponent<PlayerControllerPrediction>() != null;
    }

    private bool WithinPickupRange(NetworkObject holder)
    {
        if (holder == null)
            return false;

        return (holder.transform.position - transform.position).sqrMagnitude
            <= _pickupDistance * _pickupDistance;
    }

    private static bool CallerOwnsHolder(NetworkObject holder, NetworkConnection caller)
    {
        return holder != null && holder.IsSpawned && caller != null && holder.Owner == caller;
    }

    private void OnHolderChanged(NetworkObject previous, NetworkObject next, bool asServer)
    {
        ApplyHolderState(next != null);
    }

    private void ApplyHolderState(bool held)
    {
        if (_rb == null)
            return;

        if (_presentationHeld != held)
        {
            if (!held && _graphicalObject != null && (_smoother != null || _rpcUsesUnityPhysics))
                _releaseViewPose = new Pose(_graphicalObject.position, _graphicalObject.rotation);
            else
                _releaseViewPose = null;
            _releaseBlendRemaining = 0f;
            StopFlightSmoothing();
            RestoreViewTransform();
            _presentationHeld = held;
        }
        _rb.interpolation = !held && _rpcUsesUnityPhysics
            ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        if (held)
        {
            _throwVelocity = Vector3.zero;
            _isThrown = false;
            _hasHit = false;
            if (!_rb.isKinematic)
            {
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }
        _rb.isKinematic = held;

        foreach (Collider item in _colliders)
            item.enabled = !held;
    }

    private void UpdateHeldTransform()
    {
        NetworkObject holder = _holder.Value;
        if (holder == null || !holder.IsSpawned)
            return;

        Vector3 position = GetHoldPosition(holder);
        Quaternion rotation = GetHoldRotation(holder);
        transform.SetPositionAndRotation(position, rotation);
        _rb.position = position;
        _rb.rotation = rotation;
    }

    private Vector3 GetHoldPosition(NetworkObject holder, bool visual = false)
    {
        PlayerProjectileController controller = holder.GetComponent<PlayerProjectileController>();
        Transform holderView = holder.GetGraphicalObject();
        if (controller != null && controller.HoldAnchor != null)
        {
            Transform anchor = controller.HoldAnchor;
            if (visual || holderView == null || !anchor.IsChildOf(holderView))
                return anchor.position;

            return holder.transform.TransformPoint(holderView.InverseTransformPoint(anchor.position));
        }

        Transform target = visual && holderView != null ? holderView : holder.transform;
        return target.TransformPoint(_localHoldPosition);
    }

    private Quaternion GetHoldRotation(NetworkObject holder, bool visual = false)
    {
        PlayerProjectileController controller = holder.GetComponent<PlayerProjectileController>();
        Transform holderView = holder.GetGraphicalObject();
        if (controller != null && controller.HoldAnchor != null)
        {
            Transform anchor = controller.HoldAnchor;
            Quaternion rotation = anchor.rotation;
            if (!visual && holderView != null && anchor.IsChildOf(holderView))
                rotation = holder.transform.rotation * Quaternion.Inverse(holderView.rotation) * rotation;
            return rotation * Quaternion.Euler(_localHoldEulerAngles);
        }

        Transform target = visual && holderView != null ? holderView : holder.transform;
        return target.rotation * Quaternion.Euler(_localHoldEulerAngles);
    }

    private void IgnoreThrowerCollisions()
    {
        if (_thrower == null)
            return;

        _ignoreThrowerUntil = Time.time + _ignoreThrowerDuration;
        SetThrowerCollisionIgnored(true);
    }

    private void RestoreThrowerCollisions()
    {
        if (_thrower == null)
            return;

        SetThrowerCollisionIgnored(false);
        _thrower = null;
    }

    private void SetThrowerCollisionIgnored(bool ignore)
    {
        if (_thrower == null)
            return;

        Collider[] throwerColliders = _thrower.GetComponentsInChildren<Collider>(true);
        foreach (Collider projectileCollider in _colliders)
        {
            foreach (Collider throwerCollider in throwerColliders)
                Physics.IgnoreCollision(projectileCollider, throwerCollider, ignore);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServerStarted || !_isThrown || _hasHit)
            return;

        PlayerControllerPrediction target =
            collision.collider.GetComponentInParent<PlayerControllerPrediction>();
        if (target == null || !target.IsSpawned || target.NetworkObject == _thrower)
            return;

        Vector3 direction = target.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = _rb.velocity;
            direction.y = 0f;
        }
        if (direction.sqrMagnitude < 0.001f)
            direction = transform.forward;

        Vector3 knockbackVelocity = direction.normalized * _knockbackSpeed
            + Vector3.up * _upwardSpeed;
        target.ApplyHit(knockbackVelocity, _stunDuration);
        _hasHit = true;
    }

    public override void OnStopServer()
    {
        RestoreThrowerCollisions();
        base.OnStopServer();
    }
}
