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
    public enum ProjectileMotionState : byte
    {
        Free,
        Held,
        Thrown
    }

    public struct ProjectilePredictionState
    {
        public ProjectileMotionState Motion;
        public uint Sequence;
        public NetworkObject Holder;
        public NetworkObject Thrower;
        public uint IgnoreCollisionSteps;
    }

    [FishNet.CodeGenerating.CustomComparer]
    public static bool ComparePredictionStates(ProjectilePredictionState a, ProjectilePredictionState b)
    {
        return a.Motion == b.Motion && a.Sequence == b.Sequence
            && a.Holder == b.Holder && a.Thrower == b.Thrower
            && a.IgnoreCollisionSteps == b.IgnoreCollisionSteps;
    }

    public struct ProjectileReplicateData : IReplicateData
    {
        public ProjectilePredictionState State;
        public bool HasTransition;
        public bool HasHeldPose;
        public Vector3 Position;
        public Quaternion Rotation;
        /// <summary>One-shot velocity change applied on the throw tick.</summary>
        public Vector3 ThrowVelocity;
        private uint _tick;

        public void Dispose()
        {
            State = default;
            ThrowVelocity = Vector3.zero;
        }

        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }

    public struct ProjectileReconcileData : IReconcileData
    {
        public PredictionRigidbody Rigidbody;
        public ProjectilePredictionState State;
        private uint _tick;

        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }

    private struct PredictionSample
    {
        public bool Valid;
        public uint Tick;
        public Vector3 Position;
        public Vector3 Velocity;
    }

    [Header("Synchronization")]
    [Tooltip("Select before starting the network. Enabled: FishNet prediction. Disabled: RPC events and local physics.")]
    [SerializeField] private bool _usePrediction = true;

    [Header("Presentation")]
    [SerializeField] private Transform _graphicalObject;

    [Header("Prediction Smoothing")]
    [Tooltip("Position damping time in seconds, not a fixed delay. Larger values soften corrections but increase visual lag.")]
    [SerializeField, Min(0.01f)] private float _positionSmoothTime = 0.12f;
    [Tooltip("Quaternion damping time in seconds. Larger values soften rotation changes but increase angular lag.")]
    [SerializeField, Min(0.01f)] private float _rotationSmoothTime = 0.12f;
    [Tooltip("Snap and reset damping when the physics root moves this many world units between rendered frames. 0 disables snapping. This is not a visual-error limit.")]
    [SerializeField, Min(0f)] private float _teleportDistance;

    [Header("RPC Presentation")]
    [SerializeField, Range(1, 8)] private byte _interpolationTicks = 2;

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
    private ProjectilePredictionState _predictionState;
    private ProjectileReplicateData _pendingPredictionData;
    private uint _nextPredictionSequence;
    private NetworkObject _ignoredPredictionThrower;
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
    private bool _presentationReady;
    private TickSmootherController _smoother;
    private bool _smoothingActive;
    private bool _presentationHeld;
    private bool _rpcUsesUnityPhysics;
    private Vector3 _viewLocalPosition;
    private Quaternion _viewLocalRotation;
    private Vector3 _viewLocalScale;
    private Pose? _releaseViewPose;
    private Pose? _heldViewPose;
    private Vector3 _viewPositionVelocity;
    private Vector4 _viewRotationVelocity;
    private Vector3 _lastSmoothingRootPosition;
    private Pose _releaseLocalPose;
    private float _releaseBlendRemaining;
    private RigidbodyPauser _rpcReplayPauser;
    private PredictionManager _rpcReplayManager;
    private PredictionManager _predictionDiagnosticsManager;
    private PredictionSample[] _predictionSamples;
    private Vector3 _beforeReplayPosition;
    private Vector3 _beforeReplayVelocity;
    private bool _measuringReplay;
    private bool _receivedReconcile;
    private float _maxHistoricalPositionError;
    private float _maxHistoricalVelocityError;
    private float _maxReplayPositionCorrection;
    private float _maxReplayVelocityCorrection;

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
        SetPredictionCollisionIgnore(null);
        StopPredictionDiagnostics();
        StopRpcReplayIsolation();
        StopPresentation();
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
        if (_usePrediction)
        {
            ApplyPredictionBodyState();
            ApplyPresentationState(_predictionState.Motion == ProjectileMotionState.Held);
        }
        else
        {
            ApplyHolderState(IsHeld);
        }
    }

    public override void OnStartClient()
    {
        if (_usePrediction)
        {
            _predictionDiagnosticsManager = PredictionManager;
            _predictionDiagnosticsManager.OnPreReconcile += BeginPredictionReconcile;
            _predictionDiagnosticsManager.OnPostReconcile += EndPredictionReconcile;
            _predictionDiagnosticsManager.OnPostReplicateReplay += RecordPredictionReplay;
        }

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

        if (_usePrediction)
        {
            _presentationReady = true;
        }
        else if (!_rpcUsesUnityPhysics)
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
            spectator.AdaptiveInterpolationValue = AdaptiveInterpolationType.Off;
            _smoother = new TickSmootherController();
            _smoother.Initialize(settings, movement, spectator);
        }
        LogSmoothingStart();
    }

    public override void OnStopClient()
    {
        StopPredictionDiagnostics();
        StopRpcReplayIsolation();
        StopPresentation();
    }

    public override void OnStopNetwork()
    {
        if (_usePrediction)
            StopPresentation();
        _throwVelocity = Vector3.zero;
        SetPredictionCollisionIgnore(null);
        _predictionState = default;
        _pendingPredictionData = default;
        _nextPredictionSequence = 0;
        if (_usePrediction)
            _prediction.ClearPendingForces();
        _isThrown = false;
        _hasHit = false;
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

    private void StopPredictionDiagnostics()
    {
        if (_predictionDiagnosticsManager != null)
        {
            _predictionDiagnosticsManager.OnPreReconcile -= BeginPredictionReconcile;
            _predictionDiagnosticsManager.OnPostReconcile -= EndPredictionReconcile;
            _predictionDiagnosticsManager.OnPostReplicateReplay -= RecordPredictionReplay;
            _predictionDiagnosticsManager = null;
        }
        _predictionSamples = null;
        _measuringReplay = false;
        _receivedReconcile = false;
    }

    private void RecordPredictionSample(uint tick)
    {
        if (!_enableSmoothingLogs)
            return;
        _predictionSamples ??= new PredictionSample[256];
        _predictionSamples[tick % (uint)_predictionSamples.Length] = new PredictionSample
        {
            Valid = true,
            Tick = tick,
            Position = transform.position,
            Velocity = _rb.velocity
        };
    }

    private void BeginPredictionReconcile(uint clientTick, uint serverTick)
    {
        _receivedReconcile = false;
        _measuringReplay = _enableSmoothingLogs;
        if (!_measuringReplay)
            return;
        _beforeReplayPosition = transform.position;
        _beforeReplayVelocity = _rb.velocity;
    }

    private void RecordPredictionReplay(uint clientTick, uint serverTick)
    {
        if (_receivedReconcile)
            RecordPredictionSample(clientTick);
    }

    private void EndPredictionReconcile(uint clientTick, uint serverTick)
    {
        if (_measuringReplay && _receivedReconcile)
        {
            _maxReplayPositionCorrection = Mathf.Max(_maxReplayPositionCorrection,
                Vector3.Distance(_beforeReplayPosition, transform.position));
            _maxReplayVelocityCorrection = Mathf.Max(_maxReplayVelocityCorrection,
                Vector3.Distance(_beforeReplayVelocity, _rb.velocity));
        }
        _measuringReplay = false;
        _receivedReconcile = false;
    }

    private void MeasureHistoricalCorrection()
    {
        if (!_enableSmoothingLogs || _predictionSamples == null)
            return;
        uint tick = PredictionManager.ClientStateTick;
        PredictionSample sample = _predictionSamples[tick % (uint)_predictionSamples.Length];
        if (!sample.Valid || sample.Tick != tick)
            return;
        _maxHistoricalPositionError = Mathf.Max(_maxHistoricalPositionError,
            Vector3.Distance(sample.Position, transform.position));
        _maxHistoricalVelocityError = Mathf.Max(_maxHistoricalVelocityError,
            Vector3.Distance(sample.Velocity, _rb.velocity));
    }

    private void StopPresentation()
    {
        _presentationReady = false;
        StopFlightSmoothing();
        _smoother?.OnDestroy();
        _smoother = null;
        _releaseViewPose = null;
        _heldViewPose = null;
        _releaseBlendRemaining = 0f;
        RestoreViewTransform();
    }

    private void OnDisable()
    {
        if (!_usePrediction)
            return;
        StopFlightSmoothing();
        _releaseViewPose = null;
        _heldViewPose = null;
    }

    private void StopFlightSmoothing()
    {
        if (!_usePrediction)
        {
            if (_smoothingActive)
                _smoother.StopSmoother();
            _smoothingActive = false;
            return;
        }

        _smoothingActive = false;
        _viewPositionVelocity = Vector3.zero;
        _viewRotationVelocity = Vector4.zero;
        RestoreViewTransform();
    }

    private void RestoreViewTransform()
    {
        if (_graphicalObject == null || _graphicalObject == transform)
            return;

        if (_usePrediction && _graphicalObject.parent != transform)
            _graphicalObject.SetParent(transform, false);
        _graphicalObject.localPosition = _viewLocalPosition;
        _graphicalObject.localRotation = _viewLocalRotation;
        _graphicalObject.localScale = _viewLocalScale;
    }

    private void Update()
    {
        if (!_usePrediction && _thrower != null
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
        if (_usePrediction && IsSpawned)
            ApplyPresentationState(_predictionState.Motion == ProjectileMotionState.Held);

        if (_presentationHeld)
        {
            NetworkObject holder = _usePrediction && IsSpawned ? _predictionState.Holder : _holder.Value;
            if (_graphicalObject != null && _graphicalObject != transform
                && holder != null && holder.IsSpawned)
            {
                Quaternion rotation = GetHoldRotation(holder, true);
                Vector3 offset = Vector3.Scale(_viewLocalPosition, transform.lossyScale);
                _graphicalObject.SetPositionAndRotation(
                    GetHoldPosition(holder, true) + rotation * offset,
                    rotation * _viewLocalRotation);
                _heldViewPose = new Pose(_graphicalObject.position, _graphicalObject.rotation);
            }
        }
        else if (_presentationReady)
            UpdateFlightSmoothing(Time.deltaTime);
        else if (_smoother != null && !_smoothingActive)
        {
            RestoreViewTransform();
            Vector3 worldScale = _graphicalObject.lossyScale;
            _smoother.StartSmoother();
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

    private void UpdateFlightSmoothing(float deltaTime)
    {
        if (!_presentationReady || _presentationHeld || _graphicalObject == null)
            return;

        Vector3 targetPosition = transform.TransformPoint(_viewLocalPosition);
        Quaternion targetRotation = transform.rotation * _viewLocalRotation;
        if (!_smoothingActive)
        {
            RestoreViewTransform();
            _graphicalObject.SetParent(null, true);
            Pose pose = _releaseViewPose ?? new Pose(targetPosition, targetRotation);
            _graphicalObject.SetPositionAndRotation(pose.position, pose.rotation);
            _releaseViewPose = null;
            _viewPositionVelocity = Vector3.zero;
            _viewRotationVelocity = Vector4.zero;
            _lastSmoothingRootPosition = transform.position;
            _smoothingActive = true;
            return;
        }

        if (deltaTime <= 0f)
            return;

        bool teleport = _teleportDistance > 0f
            && (transform.position - _lastSmoothingRootPosition).sqrMagnitude
                > _teleportDistance * _teleportDistance;
        _lastSmoothingRootPosition = transform.position;
        if (teleport)
        {
            _graphicalObject.SetPositionAndRotation(targetPosition, targetRotation);
            _viewPositionVelocity = Vector3.zero;
            _viewRotationVelocity = Vector4.zero;
            return;
        }

        Vector3 position = Vector3.SmoothDamp(_graphicalObject.position, targetPosition,
            ref _viewPositionVelocity, Mathf.Max(0.01f, _positionSmoothTime), Mathf.Infinity, deltaTime);
        Quaternion current = _graphicalObject.rotation;
        if (Quaternion.Dot(current, targetRotation) < 0f)
            targetRotation = new Quaternion(-targetRotation.x, -targetRotation.y, -targetRotation.z, -targetRotation.w);

        float smoothTime = Mathf.Max(0.01f, _rotationSmoothTime);
        Vector4 rotation = new(
            Mathf.SmoothDamp(current.x, targetRotation.x, ref _viewRotationVelocity.x, smoothTime, Mathf.Infinity, deltaTime),
            Mathf.SmoothDamp(current.y, targetRotation.y, ref _viewRotationVelocity.y, smoothTime, Mathf.Infinity, deltaTime),
            Mathf.SmoothDamp(current.z, targetRotation.z, ref _viewRotationVelocity.z, smoothTime, Mathf.Infinity, deltaTime),
            Mathf.SmoothDamp(current.w, targetRotation.w, ref _viewRotationVelocity.w, smoothTime, Mathf.Infinity, deltaTime));
        float magnitude = rotation.magnitude;
        rotation /= magnitude;
        // Keep the derivative tangent to the unit quaternion after normalization.
        _viewRotationVelocity = (_viewRotationVelocity
            - rotation * Vector4.Dot(_viewRotationVelocity, rotation)) / magnitude;
        _graphicalObject.SetPositionAndRotation(position,
            new Quaternion(rotation.x, rotation.y, rotation.z, rotation.w));
    }

    private void LogSmoothingStart()
    {
        if (!_enableSmoothingLogs)
            return;

        Transform view = _graphicalObject;
        bool pauserCreated = NetworkObject.RigidbodyPauser != null;
        Debug.Log(
            $"[GrenadeSmoothing][Start] SyncMode={(_usePrediction ? "Prediction" : "RPC")}, " +
            $"Server={IsServerStarted}, Client={IsClientStarted}, " +
            $"Prediction={NetworkObject.EnablePrediction}, StateForwarding={NetworkObject.EnableStateForwarding}, " +
            $"ScriptSmoothing={_presentationReady}, Smoother={_smoother != null}, Pauser={pauserCreated}, View={(view != null ? view.name : "null")}, " +
            $"RpcReplayIsolation={_rpcReplayPauser != null}, " +
            $"PositionSmoothTime={_positionSmoothTime:F3}, RotationSmoothTime={_rotationSmoothTime:F3}, TeleportDistance={_teleportDistance:F2}, " +
            $"RpcInterpolation={_interpolationTicks}, " +
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
        bool pauserCreated = NetworkObject.RigidbodyPauser != null;
        bool paused = pauserCreated && NetworkObject.RigidbodyPauser.Paused;
        Debug.Log(
            $"[GrenadeSmoothing][Sample] Owner={IsOwner}, Held={IsHeld}, Thrown={_isThrown}, " +
            $"ScriptSmoothing={_presentationReady}, Smoother={_smoother != null}, SmoothingActive={_smoothingActive}, Reconciling={NetworkObject.IsObjectReconciling}, " +
            $"Pauser={pauserCreated}, Paused={paused}, Kinematic={_rb.isKinematic}, " +
            $"RootMaxStep={_maxRootFrameStep:F4}, ViewMaxStep={_maxViewFrameStep:F4}, " +
            $"RootViewOffset={Vector3.Distance(rootPosition, viewPosition):F4}, " +
            $"HistoryPositionError={_maxHistoricalPositionError:F4}, HistoryVelocityError={_maxHistoricalVelocityError:F4}, " +
            $"ReplayPositionCorrection={_maxReplayPositionCorrection:F4}, ReplayVelocityCorrection={_maxReplayVelocityCorrection:F4}, " +
            $"ViewParent={(view != null && view.parent != null ? view.parent.name : "null")}",
            this);

        _maxRootFrameStep = 0f;
        _maxViewFrameStep = 0f;
        _maxHistoricalPositionError = 0f;
        _maxHistoricalVelocityError = 0f;
        _maxReplayPositionCorrection = 0f;
        _maxReplayVelocityCorrection = 0f;
    }

    protected override void TimeManager_OnTick()
    {
        if (!_usePrediction)
        {
            if (!_rpcUsesUnityPhysics)
                SimulateRpcPhysics();
            return;
        }

        PerformReplicate(IsServerStarted ? BuildPredictionData() : default);
    }

    protected override void TimeManager_OnPostTick()
    {
        if (!_usePrediction && _presentationHeld && !_rpcUsesUnityPhysics)
            UpdateHeldTransform();
        if (_usePrediction)
        {
            if (IsClientStarted)
                RecordPredictionSample(TimeManager.LocalTick);
            CreateReconcile();
        }
    }

    [Replicate]
    private void PerformReplicate(
        ProjectileReplicateData rd,
        ReplicateState state = ReplicateState.Invalid,
        Channel channel = Channel.Unreliable)
    {
        if (!_usePrediction)
            return;

        bool created = state.ContainsCreated();
        if (created && rd.HasTransition
            && unchecked((int)(rd.State.Sequence - _predictionState.Sequence)) > 0)
        {
            _predictionState = rd.State;
            _isThrown = _predictionState.Motion == ProjectileMotionState.Thrown;
            _hasHit = false;
            ApplyPredictionBodyState();
            SetPredictionPose(rd.Position, rd.Rotation);
            if (_predictionState.Motion != ProjectileMotionState.Held)
            {
                _prediction.ClearVelocities();
                _prediction.AddForce(rd.ThrowVelocity, ForceMode.VelocityChange);
            }
        }

        if (_predictionState.Motion == ProjectileMotionState.Held)
        {
            if (created && rd.HasHeldPose && rd.State.Sequence == _predictionState.Sequence)
                SetPredictionPose(rd.Position, rd.Rotation);
            SetPredictionCollisionIgnore(null);
            return;
        }

        // The remaining step count is reconciled, so expiry rewinds with physics.
        SetPredictionCollisionIgnore(_predictionState.IgnoreCollisionSteps > 0 ? _predictionState.Thrower : null);
        if (_predictionState.IgnoreCollisionSteps > 0)
            _predictionState.IgnoreCollisionSteps--;

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
            State = _predictionState
        };
        PerformReconcile(rd);
    }

    [Reconcile]
    private void PerformReconcile(ProjectileReconcileData rd, Channel channel = Channel.Unreliable)
    {
        if (!_usePrediction)
            return;

        _predictionState = rd.State;
        _isThrown = _predictionState.Motion == ProjectileMotionState.Thrown;
        _prediction.Reconcile(rd.Rigidbody);
        ApplyPredictionBodyState();
        SetPredictionCollisionIgnore(_predictionState.IgnoreCollisionSteps > 0 ? _predictionState.Thrower : null);
        _receivedReconcile = true;
        MeasureHistoricalCorrection();
    }

    private void QueuePredictionTransition(ProjectileMotionState motion, NetworkObject actor, Vector3 velocity)
    {
        _pendingPredictionData = new ProjectileReplicateData
        {
            HasTransition = true,
            HasHeldPose = motion == ProjectileMotionState.Held,
            State = new ProjectilePredictionState
            {
                Motion = motion,
                Sequence = ++_nextPredictionSequence,
                Holder = motion == ProjectileMotionState.Held ? actor : null,
                Thrower = motion == ProjectileMotionState.Held ? null : actor,
                IgnoreCollisionSteps = motion == ProjectileMotionState.Held
                    ? 0 : (uint)Mathf.CeilToInt(_ignoreThrowerDuration / (float)TimeManager.TickDelta)
            },
            Position = GetHoldPosition(actor),
            Rotation = GetHoldRotation(actor),
            ThrowVelocity = velocity
        };
    }

    private ProjectileReplicateData BuildPredictionData()
    {
        ProjectileReplicateData rd = _pendingPredictionData;
        _pendingPredictionData = default;
        if (!rd.HasTransition)
        {
            if (_predictionState.Motion != ProjectileMotionState.Held)
                return default;
            rd.State = _predictionState;
            rd.HasHeldPose = true;
        }

        NetworkObject actor = rd.State.Motion == ProjectileMotionState.Held ? rd.State.Holder : rd.State.Thrower;
        if (actor != null)
        {
            rd.Position = GetHoldPosition(actor);
            rd.Rotation = GetHoldRotation(actor);
        }
        if (rd.State.Motion == ProjectileMotionState.Thrown)
            rd.Position += rd.ThrowVelocity.normalized * _throwSpawnOffset;
        return rd;
    }

    private void ApplyPredictionBodyState()
    {
        bool held = _predictionState.Motion == ProjectileMotionState.Held;
        if (held && !_rb.isKinematic)
            _prediction.ClearVelocities();
        _rb.isKinematic = held;
        foreach (Collider item in _colliders)
            item.enabled = !held;
    }

    private void SetPredictionPose(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
        _rb.position = position;
        _rb.rotation = rotation;
    }

    private void SetPredictionCollisionIgnore(NetworkObject thrower)
    {
        if (_ignoredPredictionThrower == thrower)
            return;
        SetCollisionIgnored(_ignoredPredictionThrower, false);
        _ignoredPredictionThrower = thrower;
        SetCollisionIgnored(_ignoredPredictionThrower, true);
    }

    /// <summary>Server-side pickup entry for weapons, AI, or interaction systems.</summary>
    public void Hold(NetworkObject holder)
    {
        if (!IsServerStarted || !CanHold(holder))
            return;

        if (_usePrediction)
        {
            QueuePredictionTransition(ProjectileMotionState.Held, holder, Vector3.zero);
            _holder.Value = holder;
            return;
        }

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
        if (_usePrediction)
        {
            QueuePredictionTransition(ProjectileMotionState.Thrown, holder, direction * _throwSpeed);
            _holder.Value = null;
            return;
        }

        Vector3 throwPosition = GetHoldPosition(holder) + direction * _throwSpawnOffset;
        Quaternion throwRotation = GetHoldRotation(holder);
        Vector3 throwVelocity = direction * _throwSpeed;

        _thrower = holder;
        _holder.Value = null;
        _isThrown = true;
        _hasHit = false;

        ThrowObservers(throwPosition, throwRotation, throwVelocity,
            ForceMode.VelocityChange, holder);
    }

    /// <summary>Releases the projectile without throwing velocity or hit activation.</summary>
    public void Drop()
    {
        if (!IsServerStarted || !IsHeld)
            return;

        NetworkObject holder = _holder.Value;
        if (_usePrediction)
        {
            QueuePredictionTransition(ProjectileMotionState.Free, holder, Vector3.zero);
            _holder.Value = null;
            return;
        }

        RestoreThrowerCollisions();
        _thrower = holder;
        _holder.Value = null;
        _throwVelocity = Vector3.zero;
        _isThrown = false;
        _hasHit = false;

        Vector3 dropPosition = GetHoldPosition(holder);
        Quaternion dropRotation = GetHoldRotation(holder);
        DropObservers(dropPosition, dropRotation, holder);
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
        if (!_usePrediction)
            ApplyHolderState(next != null);
    }

    private void ApplyPresentationState(bool held)
    {
        if (_presentationHeld != held)
        {
            if (!held && _graphicalObject != null && (_presentationReady || _smoother != null || _rpcUsesUnityPhysics))
                _releaseViewPose = _usePrediction && _heldViewPose.HasValue
                    ? _heldViewPose.Value : new Pose(_graphicalObject.position, _graphicalObject.rotation);
            else
                _releaseViewPose = null;
            _heldViewPose = null;
            _releaseBlendRemaining = 0f;
            StopFlightSmoothing();
            RestoreViewTransform();
            _presentationHeld = held;
        }
    }

    private void ApplyHolderState(bool held)
    {
        if (_rb == null)
            return;

        ApplyPresentationState(held);
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
        SetCollisionIgnored(_thrower, ignore);
    }

    private void SetCollisionIgnored(NetworkObject thrower, bool ignore)
    {
        if (thrower == null)
            return;

        Collider[] throwerColliders = thrower.GetComponentsInChildren<Collider>(true);
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
        NetworkObject ignoredThrower = _usePrediction ? _ignoredPredictionThrower : _thrower;
        if (target == null || !target.IsSpawned || target.NetworkObject == ignoredThrower)
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
