using FishNet.Object;
using FishNet.Object.Synchronizing;
using System;
using UnityEngine;

namespace FaceSlapper.Network.Prediction
{
    [Serializable]
    public struct CheckBox
    {
        public Vector3 Center;
        public Vector3 HalfExtents;
        public Quaternion Orientation;
    }
    /// <summary>
    /// Server-authoritative charge carrying. The motor invokes FollowCarrier during prediction.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class PlayerCarryController : NetworkBehaviour
    {
        [SerializeField, Min(0.1f)] private float _carryDistance = 1.3f;
        [SerializeField, Range(0f, 1f)] private float _carryFrontDot = 0.5f;
        [SerializeField] private CheckBox _checkBox;
        [SerializeField] private float _bias = 0.5f;
        [SerializeField] private LayerMask CarryLayerMask;
        
        
        private readonly SyncVar<NetworkObject> _carrier = new();
        private PlayerCarryController _carriedPlayer;
        private PlayerControllerPrediction _motor;
        private CharacterController _characterController;
        private Collider[] _colliders;
        private Transform _graphicalObject;
        private Vector3 _viewLocalPosition;
        private Quaternion _viewLocalRotation;
        private bool _presentingCarry;
        private Pose _lastCarryViewPose;

        public bool IsCarried => _carrier.Value != null;

        private void Awake()
        {
            _motor = GetComponent<PlayerControllerPrediction>();
            _characterController = GetComponent<CharacterController>();
            _colliders = GetComponents<Collider>();
            _carrier.OnChange += OnCarrierChanged;
            _graphicalObject = NetworkObject.GetGraphicalObject();
            if (_graphicalObject == transform)
                _graphicalObject = null;
            if (_graphicalObject != null)
            {
                _viewLocalPosition = transform.InverseTransformPoint(_graphicalObject.position);
                _viewLocalRotation = Quaternion.Inverse(transform.rotation) * _graphicalObject.rotation;
            }
        }

        private void OnDestroy()
        {
            ResetCarryPresentation(false);
            _carrier.OnChange -= OnCarrierChanged;
        }

        private void OnDisable()
        {
            ResetCarryPresentation(false);
        }

        public override void OnStopClient()
        {
            ResetCarryPresentation(false);
            base.OnStopClient();
        }

        private void LateUpdate()
        {
            if (!IsClientInitialized)
            {
                ResetCarryPresentation(false);
                return;
            }

            NetworkObject carrier = _carrier.Value;
            if (carrier == null || !carrier.IsSpawned)
            {
                ResetCarryPresentation(true);
                return;
            }
            if (_graphicalObject == null)
                return;

            Transform carrierView = carrier.GetGraphicalObject();
            if (carrierView == null)
                carrierView = carrier.transform;

            // Use the carrier's rendered timeline, not the passenger's independent interpolation delay.
            _graphicalObject.position = carrierView.position + carrierView.forward * _carryDistance
                + transform.TransformVector(_viewLocalPosition);
            _lastCarryViewPose = new Pose(_graphicalObject.position, _graphicalObject.rotation);
            _presentingCarry = true;
        }

        private void ResetCarryPresentation(bool preservePose)
        {
            if (!_presentingCarry)
                return;
            _presentingCarry = false;
            if (_graphicalObject == null)
                return;

            // The player prefab still uses the FishNet 4.x NetworkObject smoother.
#pragma warning disable CS0618
            var smoother = NetworkObject.PredictionSmoother;
#pragma warning restore CS0618
            if (smoother != null)
            {
                smoother.Teleport();
                if (preservePose)
                    _graphicalObject.SetPositionAndRotation(_lastCarryViewPose.position, _lastCarryViewPose.rotation);
            }
            else
            {
                _graphicalObject.SetPositionAndRotation(transform.TransformPoint(_viewLocalPosition),
                    transform.rotation * _viewLocalRotation);
            }
        }

        private void OnCarrierChanged(NetworkObject previous, NetworkObject next, bool asServer)
        {
            bool carried = next != null;
            _motor.SetCarriedState(carried);

            // A carried player must neither move nor obstruct the carrier.
            foreach (Collider item in _colliders)
                item.enabled = !carried;
        }

        private Vector3 GetCheckCenter()
        {
            return transform.position + transform.forward * _bias;
        }

        private Quaternion GetCheckOrientation()
        {
            Quaternion orientation = _checkBox.Orientation;
            float magnitudeSquared = orientation.x * orientation.x
                + orientation.y * orientation.y
                + orientation.z * orientation.z
                + orientation.w * orientation.w;

            // A newly serialized Quaternion may be all zeroes.
            return magnitudeSquared < 0.0001f ? Quaternion.identity : orientation.normalized;
        }

        private void Update()
        {
            if (!IsServerStarted || !_motor.State.IsCharging || IsCarried || _carriedPlayer != null)
                return;

            RaycastHit[] hits = Physics.BoxCastAll(
                GetCheckCenter(),
                _checkBox.HalfExtents,
                transform.forward,
                GetCheckOrientation());
            foreach(var hit in hits)
            {
                if(hit.collider.GetComponent<PlayerCarryController>() != null)
                {
                    PlayerCarryController other = hit.collider.GetComponentInParent<PlayerCarryController>();
                    if (other == null || other == this || !other.IsSpawned)
                        continue;

                    // Do not allow carrying chains or picking up an already carried player.
                    if (other.IsCarried || other._carriedPlayer != null)
                        continue;

                    Vector3 direction = other.transform.position - transform.position;
                    direction.y = 0f;
                    Vector3 forward = transform.forward;
                    forward.y = 0f;

                    if (direction.sqrMagnitude < 0.001f || forward.sqrMagnitude < 0.001f)
                        continue;

                    if (Vector3.Dot(forward.normalized, direction.normalized) < _carryFrontDot)
                        continue;

                    Attach(other);
                }
            }
        }

        private void OnDrawGizmos()
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(GetCheckCenter(), GetCheckOrientation(), Vector3.one);

            // BoxCast uses half extents, while DrawWireCube expects the full size.
            Gizmos.DrawWireCube(Vector3.zero, _checkBox.HalfExtents * 2f);
            Gizmos.matrix = previousMatrix;
        }

        private void Attach(PlayerCarryController target)
        {
            _carriedPlayer = target;
            target._carrier.Value = NetworkObject;
        }

        /// <summary>Called by the motor after its charge state has been simulated.</summary>
        public void ServerTick(bool isCharging)
        {
            if (!IsServerStarted)
                return;

            if (_carriedPlayer != null && !_carriedPlayer.IsSpawned)
            {
                _carriedPlayer = null;
                return;
            }

            if (!isCharging)
                Release();
        }

        public void FollowCarrier()
        {
            NetworkObject carrier = _carrier.Value;
            if (carrier == null || !carrier.IsSpawned)
                return;

            Vector3 targetPosition = carrier.transform.position + carrier.transform.forward * _carryDistance;

            // OnCarrierChanged keeps the controller disabled for the whole carry duration.
            _characterController.enabled = false;
            _motor.ApplyCarriedPosition(targetPosition);
        }

        private void Release()
        {
            if (_carriedPlayer == null)
                return;

            PlayerCarryController target = _carriedPlayer;
            _carriedPlayer = null;

            if (target.IsSpawned)
                target._carrier.Value = null;
        }

        public override void OnStopServer()
        {
            Release();
            base.OnStopServer();
        }
    }
}
