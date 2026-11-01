using FaceSlapper.Abilities;
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

        private readonly SyncVar<NetworkObject> _heldObject = new();
        private readonly Collider[] _overlapResults = new Collider[32];
        private PlayerControllerPrediction _motor;

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
        }

        private bool CanInteract()
        {
            return _motor != null && !_motor.IsStunned && !_motor.IsCarried;
        }

        internal bool CanExecuteAbility(AbilityCommand command)
        {
            if (!CanInteract())
                return false;

            switch (command.Ability)
            {
                case AbilityId.ProjectileInteract:
                    return _heldObject.Value != null
                        ? GetHeldProjectile() != null
                        : FindNearestProjectile() != null;
                case AbilityId.ProjectileThrow:
                    return GetHeldProjectile() != null;
                default:
                    return false;
            }
        }

        internal bool TryExecuteServerAbility(AbilityCommand command)
        {
            if (!IsServerStarted)
                return false;

            ValidateHeldObject();
            if (!CanExecuteAbility(command))
                return false;

            switch (command.Ability)
            {
                case AbilityId.ProjectileInteract:
                    return TryInteract();
                case AbilityId.ProjectileThrow:
                    return TryThrow(command.AimDirection);
                default:
                    return false;
            }
        }

        private bool TryInteract()
        {
            if (_heldObject.Value != null)
            {
                DropHeldObject();
                return _heldObject.Value == null;
            }

            NetworkProjectile projectile = FindNearestProjectile();
            if (projectile == null)
                return false;

            projectile.Hold(NetworkObject);
            if (projectile.IsHeld && projectile.Holder == NetworkObject)
            {
                _heldObject.Value = projectile.NetworkObject;
                return true;
            }

            return false;
        }

        private bool TryThrow(Vector3 direction)
        {
            NetworkProjectile projectile = GetHeldProjectile();
            if (projectile == null)
            {
                _heldObject.Value = null;
                return false;
            }

            projectile.Throw(direction);
            if (!projectile.IsHeld)
            {
                _heldObject.Value = null;
                return true;
            }

            return false;
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
