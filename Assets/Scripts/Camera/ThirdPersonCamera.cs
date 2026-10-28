using FaceSlapper.Network.Prediction;
using UnityEngine;

namespace FaceSlapper.Camera
{
    /// <summary>
    /// 第三人称跟随相机：自动绑定本地玩家，右键拖动旋转，滚轮缩放，并避免穿过场景碰撞体。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("跟随")]
        [SerializeField] private Vector3 _pivotOffset = new(0f, 1.2f, 0f);
        [SerializeField, Min(0f)] private float _smoothTime = 0.08f;

        [Header("旋转")]
        [SerializeField] private float _yaw;
        [SerializeField, Range(5f, 80f)] private float _pitch = 30f;
        [SerializeField] private float _mouseSensitivity = 3f;
        [SerializeField] private float _minPitch = 5f;
        [SerializeField] private float _maxPitch = 75f;

        [Header("缩放")]
        [SerializeField, Min(0.5f)] private float _distance = 6f;
        [SerializeField, Min(0.5f)] private float _minDistance = 2f;
        [SerializeField, Min(0.5f)] private float _maxDistance = 12f;
        [SerializeField] private float _zoomSpeed = 2f;

        [Header("碰撞")]
        [SerializeField] private LayerMask _collisionMask = ~0;
        [SerializeField, Min(0.01f)] private float _collisionRadius = 0.25f;
        [SerializeField, Min(0f)] private float _collisionPadding = 0.15f;
        [SerializeField, Min(0.1f)] private float _minimumCollisionDistance = 0.4f;

        private readonly RaycastHit[] _collisionHits = new RaycastHit[16];
        private Transform _target;
        private Transform _targetRoot;
        private Vector3 _smoothVelocity;
        private bool _isRotating;

        private void Awake()
        {
            _yaw = transform.eulerAngles.y;
            _distance = Mathf.Clamp(_distance, _minDistance, _maxDistance);
        }

        private void OnEnable()
        {
            PlayerControllerPrediction.OnOwner += OnLocalPlayerOwner;

            // 相机后启用时，仍然可以找到已经存在的本地玩家。
            PlayerControllerPrediction[] players = FindObjectsOfType<PlayerControllerPrediction>();
            foreach (PlayerControllerPrediction player in players)
            {
                if (!player.IsOwner)
                    continue;

                SetTarget(player);
                break;
            }
        }

        private void OnDisable()
        {
            PlayerControllerPrediction.OnOwner -= OnLocalPlayerOwner;
            StopRotating();
        }

        private void OnLocalPlayerOwner(PlayerControllerPrediction player)
        {
            SetTarget(player);
        }

        /// <summary>绑定本地玩家，并跟随 FishNet 已平滑的图形节点。</summary>
        public void SetTarget(PlayerControllerPrediction player)
        {
            if (player == null)
            {
                SetTarget((Transform)null);
                return;
            }

            Transform graphicalTarget = player.NetworkObject.GetGraphicalObject();
            _target = graphicalTarget != null ? graphicalTarget : player.transform;
            _targetRoot = player.transform;
            _smoothVelocity = Vector3.zero;
            UpdateCamera(true);
        }

        /// <summary>设置普通跟随目标；传 null 可解除绑定。</summary>
        public void SetTarget(Transform target)
        {
            _target = target;
            _targetRoot = target;
            _smoothVelocity = Vector3.zero;

            if (_target != null)
                UpdateCamera(true);
        }

        private void LateUpdate()
        {
            if (_target == null)
                return;

            UpdateInput();
            UpdateCamera(false);
        }

        private void UpdateInput()
        {
            if (UnityEngine.Input.GetMouseButtonDown(1))
            {
                _isRotating = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (UnityEngine.Input.GetMouseButtonUp(1))
                StopRotating();

            if (_isRotating)
            {
                _yaw += UnityEngine.Input.GetAxis("Mouse X") * _mouseSensitivity;
                _pitch = Mathf.Clamp(
                    _pitch - UnityEngine.Input.GetAxis("Mouse Y") * _mouseSensitivity,
                    _minPitch,
                    _maxPitch);
            }

            float scroll = UnityEngine.Input.mouseScrollDelta.y;
            _distance = Mathf.Clamp(_distance - scroll * _zoomSpeed, _minDistance, _maxDistance);
        }

        private void StopRotating()
        {
            if (!_isRotating)
                return;

            _isRotating = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void UpdateCamera(bool snap)
        {
            Quaternion orbitRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 orbitDirection = orbitRotation * Vector3.back;
            Vector3 pivot = _target.position + _pivotOffset;
            float resolvedDistance = ResolveCollisionDistance(pivot, orbitDirection);
            Vector3 desiredPosition = pivot + orbitDirection * resolvedDistance;

            // 遇墙时立即缩短距离，避免平滑跟随把相机留在墙内。
            if (snap || resolvedDistance < _distance - 0.01f)
            {
                _smoothVelocity = Vector3.zero;
                transform.position = desiredPosition;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(
                    transform.position,
                    desiredPosition,
                    ref _smoothVelocity,
                    _smoothTime);
            }

            Vector3 lookDirection = pivot - transform.position;
            if (lookDirection.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        }

        private float ResolveCollisionDistance(Vector3 pivot, Vector3 orbitDirection)
        {
            int hitCount = Physics.SphereCastNonAlloc(
                pivot,
                _collisionRadius,
                orbitDirection,
                _collisionHits,
                _distance,
                _collisionMask,
                QueryTriggerInteraction.Ignore);

            float resolvedDistance = _distance;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _collisionHits[i];
                if (hit.collider == null)
                    continue;

                // Players may obscure the view, but must never push the camera forward.
                if (hit.collider.GetComponentInParent<PlayerControllerPrediction>() != null)
                    continue;

                // Also support ordinary, non-networked follow targets.
                if (hit.transform == _targetRoot || hit.transform.IsChildOf(_targetRoot))
                    continue;

                float hitDistance = Mathf.Max(
                    _minimumCollisionDistance,
                    hit.distance - _collisionPadding);
                resolvedDistance = Mathf.Min(resolvedDistance, hitDistance);
            }

            return resolvedDistance;
        }
    }
}
