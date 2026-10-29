using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace FaceSlapper.Network.Prediction
{
    /// <summary>
    /// Owner input and server-authoritative pickup/throw/drop actions.
    /// Projectile physics and hit resolution remain on NetworkProjectile.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerProjectileController : NetworkBehaviour
    {
        [Header("Interaction")]
        [SerializeField] private Transform _holdAnchor;
        [SerializeField, Min(0.1f)] private float _pickupRadius = 2.5f;
        [SerializeField] private LayerMask _projectileLayers = ~0;
        [SerializeField, Min(0.1f)] private float _interactionCooldown = 0.15f;

        private readonly SyncVar<NetworkObject> _heldObject = new();
        private readonly Collider[] _overlapResults = new Collider[32];
        private PlayerControllerPrediction _motor;
        private float _nextInteractionTime;

        public Transform HoldAnchor => _holdAnchor;
        public NetworkObject HeldObject => _heldObject.Value;
        public bool HasProjectile => _heldObject.Value != null;

        private void Awake()
        {
            _motor = GetComponent<PlayerControllerPrediction>();
        }

        private void Update()
        {
            if (IsServerStarted)
                ValidateHeldObject();

            if (!IsOwner || !CanInteract())
                return;

            if (UnityEngine.Input.GetKeyDown(KeyCode.E))
                RequestInteract();

            if (HasProjectile && UnityEngine.Input.GetMouseButtonDown(0))
                RequestThrow(GetAimDirection());
        }

        private Vector3 GetAimDirection()
        {
            UnityEngine.Camera playerCamera = UnityEngine.Camera.main;
            return playerCamera != null ? playerCamera.transform.forward : transform.forward;
        }

        private bool CanInteract()
        {
            return _motor != null && !_motor.IsStunned && !_motor.IsCarried;
        }

        [ServerRpc]
        private void RequestInteract()
        {
            if (!CanInteract() || Time.time < _nextInteractionTime)
                return;

            _nextInteractionTime = Time.time + _interactionCooldown;

            if (_heldObject.Value != null)
            {
                DropHeldObject();
                return;
            }

            NetworkProjectile projectile = FindNearestProjectile();
            if (projectile == null)
                return;

            projectile.Hold(NetworkObject);
            if (projectile.IsHeld && projectile.Holder == NetworkObject)
                _heldObject.Value = projectile.NetworkObject;
        }

        [ServerRpc]
        private void RequestThrow(Vector3 direction)
        {
            if (!CanInteract() || Time.time < _nextInteractionTime)
                return;

            _nextInteractionTime = Time.time + _interactionCooldown;

            NetworkProjectile projectile = GetHeldProjectile();
            if (projectile == null)
            {
                _heldObject.Value = null;
                return;
            }

            projectile.Throw(direction);
            if (!projectile.IsHeld)
                _heldObject.Value = null;
        }

        private NetworkProjectile FindNearestProjectile()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position,
                _pickupRadius,
                _overlapResults,
                _projectileLayers,
                QueryTriggerInteraction.Ignore);

            NetworkProjectile nearest = null;
            float nearestDistance = float.MaxValue;
            for (int index = 0; index < count; index++)
            {
                Collider item = _overlapResults[index];
                if (item == null)
                    continue;

                NetworkProjectile candidate = item.GetComponentInParent<NetworkProjectile>();
                if (candidate == null || !candidate.IsSpawned || candidate.IsHeld)
                    continue;

                float distance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (distance >= nearestDistance)
                    continue;

                nearest = candidate;
                nearestDistance = distance;
            }

            return nearest;
        }

        private void DropHeldObject()
        {
            NetworkProjectile projectile = GetHeldProjectile();
            if (projectile != null)
                projectile.Drop();

            _heldObject.Value = null;
        }

        private NetworkProjectile GetHeldProjectile()
        {
            NetworkObject heldObject = _heldObject.Value;
            if (heldObject == null || !heldObject.IsSpawned)
                return null;

            NetworkProjectile projectile = heldObject.GetComponent<NetworkProjectile>();
            return projectile != null && projectile.Holder == NetworkObject ? projectile : null;
        }

        private void ValidateHeldObject()
        {
            if (_heldObject.Value != null && GetHeldProjectile() == null)
                _heldObject.Value = null;
        }

        public override void OnStopServer()
        {
            DropHeldObject();
            base.OnStopServer();
        }
    }
}
