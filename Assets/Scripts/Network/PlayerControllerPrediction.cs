using System;
using FishNet.Component.Prediction;
using FishNet.Connection;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;

namespace FaceSlapper.Network.Prediction
{
    public class PlayerControllerPrediction : TickNetworkBehaviour
    {
        #region Types.
        /// <summary>
        /// One-time inputs accumulated over frames between ticks.
        /// </summary>
        public struct OneTimeInput
        {
            /// <summary>
            /// True to jump.
            /// </summary>
            public bool Jump;
            public bool ChargePressed;

            /// <summary>
            /// Unset inputs.
            /// </summary>
            public void ResetState()
            {
                Jump = false;
                ChargePressed = false;
            }
        }

        public struct ReplicateData : IReplicateData
        {
            public ReplicateData(Vector2 input, bool run, OneTimeInput oneTimeInputs, bool charging, float cameraYaw)
            {
                OneTimeInputs = oneTimeInputs;
                Input = input;
                Run = run;
                IsCharging = charging;
                CameraYaw = cameraYaw;
                _tick = 0;
            }

            /// <summary>
            /// True if Jump input was pressed for the tick.
            /// </summary>
            public OneTimeInput OneTimeInputs;
            /// <summary>
            /// Current movement directions held.
            /// </summary>
            public Vector2 Input;
            /// <summary>
            /// True if run is held.
            /// </summary>
            public readonly bool Run;

            public readonly bool IsCharging;
            /// <summary>
            /// Camera yaw supplied by the owner, used to make movement deterministic.
            /// </summary>
            public readonly float CameraYaw;
            /// <summary>
            /// Tick is set at runtime. There is no need to manually assign this value.
            /// </summary>
            private uint _tick;

            public void Dispose()
            {
                OneTimeInputs.ResetState();
            }

            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public struct ReconcileData : IReconcileData
        {
            public ReconcileData(
                Vector3 position,
                Quaternion rotation,
                float verticalVelocity,
                float stamina,
                bool isCharging,
                uint stunEndTick,
                Vector3 knockbackVelocity)
            {
                Position = position;
                Rotation = rotation;
                VerticalVelocity = verticalVelocity;
                Stamina = stamina;
                IsCharging = isCharging;
                StunEndTick = stunEndTick;
                KnockbackVelocity = knockbackVelocity;
                _tick = 0;
            }

            public Vector3 Position;
            public Quaternion Rotation;
            /// <remarks>Used to simulate jumps, falls, and upward knockback.</remarks>
            public float VerticalVelocity;
            public float Stamina;
            public bool IsCharging;
            public uint StunEndTick;
            public Vector3 KnockbackVelocity;

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }
        #endregion

        /// <summary>
        /// Invokes whenever NetworkStart is called for owner.
        /// </summary>
        public static event Action<PlayerControllerPrediction> OnOwner;
        /// <summary>
        /// Current stamina remaining.
        /// </summary>
        public float Stamina { get; private set; }
        /// <summary>
        /// Amount of force for jumping.
        /// </summary>
        [Tooltip("Amount of force for jumping.")]
        [SerializeField]
        private float _jumpForce = 30f;
        /// <summary>
        /// How quickly to move.
        /// </summary>
        [Tooltip("How quickly to move.")]
        [SerializeField]
        private float _moveRate = 4f;
        [Tooltip("How quickly the character rotates towards its movement direction, in degrees per second.")]
        [SerializeField]
        private float _rotationSpeed = 720f;
        /// <summary>
        /// Current vertical velocity.
        /// </summary>
        private float _verticalVelocity;
        /// <summary>
        /// One-time inputs accumulated over frames between ticks.
        /// </summary>
        private OneTimeInput _oneTimeInputs = new();
        /// <summary>
        /// Last data which was supplied during replicate outside of reconcile.
        /// </summary>
        private ReplicateData _lastTickedReplicateData = default;
        /// <summary>
        /// Reference to the CharacterController component.
        /// </summary>
        private CharacterController _characterController;
        /// <summary>
        /// maximum amount of stamina allowed.
        /// </summary>
        public const float Maximum_Stamina = 50f;

        public float ChargingSpeed = 20;
        public float DirectionRate = 1f;

        [Header("Combat Status")]
        [SerializeField, Min(0f)] private float _knockbackDamping = 12f;

        public PlayerStatusController Status { get; } = new();
        private PlayerCarryController _carryController;

        public bool IsCarried => _carryController != null && _carryController.IsCarried;
        public bool IsStunned => Status.IsStunned(TimeManager.LocalTick);

        public class PlayerState
        {
            public bool IsCharging;
            public uint Touch; //where player is touching, ground, wall or none
            public Vector3 Position;
            public Quaternion Rotation;
            public int FrontForcedBy;
            public bool StopPrediction;
        }

        public PlayerState State = new();

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _carryController = GetComponent<PlayerCarryController>();

            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        /// <summary>Server entry point for future weapons and combat abilities.</summary>
        public void ApplyHit(Vector3 knockbackVelocity, float stunDuration)
        {
            if (!IsServerStarted)
                return;

            float safeDuration = Mathf.Max(0f, stunDuration);
            uint durationTicks = (uint)Mathf.CeilToInt(safeDuration / (float)TimeManager.TickDelta);
            Status.ApplyStun(TimeManager.LocalTick, durationTicks);
            Status.ApplyKnockback(knockbackVelocity);

            if (knockbackVelocity.y > 0f)
                _verticalVelocity = Mathf.Max(_verticalVelocity, knockbackVelocity.y);

            State.IsCharging = false;
        }

        internal void SetCarriedState(bool carried)
        {
            _verticalVelocity = 0f;
            _oneTimeInputs.ResetState();

            if (carried)
                State.IsCharging = false;
        }

        internal void ApplyCarriedPosition(Vector3 position)
        {
            _verticalVelocity = 0f;
            transform.position = position;
            State.Position = transform.localPosition;
            State.Rotation = transform.localRotation;
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            if (IsOwner)
                OnOwner?.Invoke(this);
        }

        private void Update()
        {
            SetOneTimeInputs();
        }

        /// <summary>
        /// Checks setting inputs which are one-time(not held).
        /// </summary>
        private void SetOneTimeInputs()
        {
            if (!IsOwner)
                return;

            /* Check to jump. */
            if (UnityEngine.Input.GetKeyDown(KeyCode.Space))
                _oneTimeInputs.Jump = true;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Q))
                _oneTimeInputs.ChargePressed = true;
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(BuildMoveData());
        }

        protected override void TimeManager_OnPostTick()
        {
            /* Typically a CharacterController could send a reconcile within
             * OnTick, after completing all moving logic. However, this demo
             * uses a NetworkTrigger to jump on platforms, which we need the results
             * from before sending a reconcile. The NetworkTrigger/Collider components
             * trace after simulation which occurs between OnTick, and OnPostTick, therefor
             * the trace would have happened by the time OnPostTick is called. */
            CreateReconcile();
        }

        /// <summary>
        /// Returns replicate data to send as the controller.
        /// </summary>
        private ReplicateData BuildMoveData()
        {
            /* Only the controller needs to build move data.
             * This could be the server if the server if no owner, for example
             * such as AI, or the owner of the object. */
            if (!IsOwner)
                return default;

            float horizontal = UnityEngine.Input.GetAxisRaw("Horizontal");
            float vertical = UnityEngine.Input.GetAxisRaw("Vertical");
            // Run when left shift is held.
            bool run = UnityEngine.Input.GetKey(KeyCode.LeftShift);

            // Only the owner reads the camera. The yaw is replicated so the server
            // and other clients calculate exactly the same world-space movement.
            UnityEngine.Camera playerCamera = UnityEngine.Camera.main;
            float cameraYaw = playerCamera != null ? playerCamera.transform.eulerAngles.y : transform.eulerAngles.y;

            ReplicateData md = new(new(horizontal, vertical), run, _oneTimeInputs, State.IsCharging, cameraYaw);

            // Reset one tine inputs since they've been processed for the tick.
            _oneTimeInputs.ResetState();

            return md;
        }

        /// <summary>
        /// Creates a reconcile that is sent to clients.
        /// </summary>
        public override void CreateReconcile()
        {
            /* Both the server and client should create reconcile data.
             * The client will use their copy as a fallback if they do not
             * get data from the server, such as a dropped packet.
             *
             * The client will not reconcile unless it receives at least one
             * reconcile packet from the server for the tick. */

            /* You do not have to reconcile every tick if you wish to
             * save bandwidth/perf, or simply feel as though it's not needed
             * for your game type.
             *
             * Even when not reconciling every tick it's still recommended
             * to build the reconcile as client; this cost is very little.*/

            /* This is an example of only sending a reconcile occasionally
             * if the server. Simply uncomment the if statement below to
             * test this behavior. */
            // if (base.IsServerStarted)
            // {
            //     // Exit early if 10 ticks have not passed.
            //     if (base.TimeManager.LocalTick % 10 != 0) return;
            // }

            // Build the data using current information and call the reconcile method.
            ReconcileData rd = new(
                transform.localPosition,
                transform.localRotation,
                _verticalVelocity,
                Stamina,
                State.IsCharging,
                Status.StunEndTick,
                Status.KnockbackVelocity);
            PerformReconcile(rd);
        }

        [Replicate]
        private void PerformReplicate(ReplicateData rd, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            if (IsCarried)
            {
                // Ignore all owner input while carried, but keep updating the predicted transform.
                _carryController.FollowCarrier();
                return;
            }

            if (State.StopPrediction) return;
            // Always use the tickDelta as your delta when performing actions inside replicate.
            float delta = (float)TimeManager.TickDelta;
            bool useDefaultForces = false;

            /* When client only run some checks to
             * further predict the clients future movement.
             * This can keep the object more inlined with real-time by
             * guessing what the clients input might be before we
             * actually receive it.
             *
             * Doing this does risk a chance of graphical jitter in the
             * scenario a de-synchronization occurs, but if only predicting
             * a couple ticks the chances are low. */
            // See https://fish-networking.gitbook.io/docs/manual/guides/prediction/version-2/creating-code/predicting-states
            if (!IsServerStarted && !IsOwner)
            {
                /* If ticked then set last ticked value.
                 * Ticked means the replicate is being run from the tick cycle, more
                 * specifically NOT from a replay/reconcile. */
                if (state.ContainsTicked())
                {
                    /* Dispose of old should it have anything that needs to be cleaned up.
                     * If you are only using value types in your data you do not need to call Dispose.
                     * You must implement dispose manually to cache any non-value types, if you wish. */
                    _lastTickedReplicateData.Dispose();
                    // Set new.
                    _lastTickedReplicateData = rd;
                }
                /* In the future means there is no way the data can be known to this client
                 * yet. For example, the client is running this script locally and due to
                 * how networking works, they have not yet received the latest information from
                 * the server.
                 *
                 * If in the future then we are only going to predict up to
                 * a certain amount of ticks in the future. This is us assuming that the
                 * server (or client which owns this in this case) is going to use the
                 * same input for at least X number of ticks. You can predict none, or as many
                 * as you like, but the more inputs you predict the higher likeliness of guessing
                 * wrong. If you do however predict wrong often smoothing will cover up the mistake. */
                else if (state.IsFuture())
                {
                    /* Predict up to 1 tick more. */
                    if (rd.GetTick() - _lastTickedReplicateData.GetTick() > 1)
                    {
                        useDefaultForces = true;
                    }
                    else
                    {
                        /* If here we are predicting the future. */

                        /* You likely do not need to dispose rd here since it would be default
                         * when state is 'not created'. We are simply doing it for good practice, should your ReplicateData
                         * contain any garbage collection. */
                        rd.Dispose();

                        rd = _lastTickedReplicateData;

                        /* There are some fields you might not want to predict, for example
                         * jump. The odds of a client pressing jump two ticks in a row is unlikely.
                         * The stamina check below would likely prevent such a scenario.
                         *
                         * We're going to unset jump for this reason. */
                        rd.OneTimeInputs.Jump = false;
                        rd.OneTimeInputs.ChargePressed = false;

                        /* Be aware that future predicting is not a one-size fits all
                         * feature. How much you predict into the future, if at all, depends
                         * on your game mechanics and your desired outcome. */
                    }
                }
            }

            Vector3 forces;

            if (useDefaultForces)
            {
                /* Character controllers are a bit problematic with colliders.
                 * If you were to pass Vector3.zero into the move then there's
                 * a chance other colliders will clip through the characterController.
                 * When this is combined with reconciles, it's practically gauranteed
                 * this will happen.
                 *
                 * Because of this issue, if we are 'using default forces' we will apply a
                 * very insignificant amount of force which makes the characterController
                 * update properly. */
                forces = new(0f, -1f, 0f);
            }
            // Calculate forces.
            else
            {
                forces = GetForce(rd, delta);
            }

            if (IsServerStarted && _carryController != null)
                _carryController.ServerTick(State.IsCharging);

            Status.Simulate(delta, _knockbackDamping);
            _characterController.Move(forces * delta);
            State.Position = transform.localPosition;
            State.Rotation = transform.localRotation;
        }

        private Vector3 GetForce(ReplicateData rd, float delta)
        {
            bool canControl = State.FrontForcedBy <= 0 && !Status.IsStunned(rd.GetTick());
            if (!canControl)
                State.IsCharging = false;

            // Keep a small downward force while grounded instead of accumulating gravity.
            if (_characterController.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;

            _verticalVelocity += Physics.gravity.y * delta * 3f;
            _verticalVelocity = Mathf.Max(_verticalVelocity, -40f);

            if (canControl && rd.OneTimeInputs.ChargePressed)
                State.IsCharging = !State.IsCharging;

            if (State.IsCharging)
            {
                Vector3 chargeForce = transform.forward * ChargingSpeed;
                chargeForce += transform.right * (rd.Input.x * DirectionRate);
                chargeForce += Status.KnockbackVelocity;
                chargeForce.y = _verticalVelocity;
                return chargeForce;
            }

            const float regainedStamina = 25f;
            ModifyStamina(regainedStamina * delta);

            // Stun removes control, not gravity, knockback, or reconciliation.
            Vector2 input = canControl ? Vector2.ClampMagnitude(rd.Input, 1f) : Vector2.zero;
            Quaternion cameraRotation = Quaternion.Euler(0f, rd.CameraYaw, 0f);
            Vector3 moveDirection = cameraRotation * new Vector3(input.x, 0f, input.y);
            RotateTowards(moveDirection, delta);

            float runMultiplier;
            const float runStamina = 50f;
            if (canControl && rd.Run && TryRemoveStamina(runStamina * delta))
                runMultiplier = 1.5f;
            else
                runMultiplier = 1f;

            const byte jumpStamina = 30;
            if (canControl && rd.OneTimeInputs.Jump && TryRemoveStamina(jumpStamina))
                _verticalVelocity = _jumpForce;

            Vector3 forces = moveDirection * (_moveRate * runMultiplier);
            forces += Status.KnockbackVelocity;
            forces.y = _verticalVelocity;
            return forces;
        }

        private void RotateTowards(Vector3 moveDirection, float delta)
        {
            if (moveDirection.sqrMagnitude < 0.001f)
                return;

            Quaternion targetRotation = Quaternion.LookRotation(moveDirection.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                _rotationSpeed * delta);
        }

        [Reconcile]
        private void PerformReconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
        {
            if (State.StopPrediction) return;

            _verticalVelocity = rd.VerticalVelocity;
            Stamina = rd.Stamina;
            State.IsCharging = rd.IsCharging;
            Status.Reconcile(rd.StunEndTick, rd.KnockbackVelocity);

            /* Disable the CharacterController before applying the authoritative
             * transform so its physics state is updated at the same time. */
            _characterController.enabled = false;
            transform.localPosition = rd.Position;
            transform.localRotation = rd.Rotation;
            _characterController.enabled = !IsCarried;
        }

        /// <summary>
        /// Modifies stamina by adding or removing stamina.
        /// </summary>
        private void ModifyStamina(float value)
        {
            float next = Stamina + value;
            Stamina = Mathf.Clamp(next, 0f, Maximum_Stamina);
        }

        /// <summary>
        /// Removes stamina if enough stamina is available.
        /// </summary>
        /// <returns>>True if stamina was available and removed.</returns>
        private bool TryRemoveStamina(float value)
        {
            if (Stamina < value)
                return false;

            Stamina -= value;
            return true;
        }
    }
}