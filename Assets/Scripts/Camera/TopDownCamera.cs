using FaceSlapper.Core;
using FaceSlapper.Input;
using UnityEngine;

namespace FaceSlapper.Camera
{
    /// <summary>
    /// 第三人称俯视角相机（类猛兽派对）：
    /// 固定偏航角，不随鼠标旋转；滚轮调节视野距离（同时微调俯仰角）；
    /// 平滑跟随本地玩家，经 EventBus 的 LocalPlayerSpawnedEvent 自动绑定目标。
    /// </summary>
    public class TopDownCamera : MonoBehaviour
    {
        [Header("跟随")]
        [SerializeField] private float _smoothTime = 0.12f;
        [SerializeField] private Vector3 _lookAtOffset = new Vector3(0f, 1f, 0f);

        [Header("缩放")]
        [SerializeField] private float _distance = 12f;
        [SerializeField] private float _minDistance = 6f;
        [SerializeField] private float _maxDistance = 22f;
        [SerializeField] private float _zoomSpeed = 2.5f;

        [Header("俯仰（随缩放插值）")]
        [SerializeField] private float _minPitch = 45f;
        [SerializeField] private float _maxPitch = 65f;

        private Transform _target;
        private Vector3 _smoothVelocity;

        private void OnEnable()
        {
            EventBus.Subscribe<LocalPlayerSpawnedEvent>(OnLocalPlayerSpawned);
            EventBus.Subscribe<LocalPlayerDespawnedEvent>(OnLocalPlayerDespawned);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<LocalPlayerSpawnedEvent>(OnLocalPlayerSpawned);
            EventBus.Unsubscribe<LocalPlayerDespawnedEvent>(OnLocalPlayerDespawned);
        }

        private void OnLocalPlayerSpawned(LocalPlayerSpawnedEvent e) { }

        private void OnLocalPlayerDespawned(LocalPlayerDespawnedEvent e) => _target = null;

        /// <summary>手动设置跟随目标（可选，默认自动绑定本地玩家）。</summary>
        public void SetTarget(Transform target) => _target = target;

        private void LateUpdate()
        {
            if (_target == null)
            {
            }

            float scroll = 0f;
            if (GameManager.HasInstance)
            {
                InputComponent input = GameManager.Instance.Get<InputComponent>();
                if (input != null) scroll = input.Current.ScrollDelta;
            }

            _distance = Mathf.Clamp(_distance - scroll * _zoomSpeed, _minDistance, _maxDistance);
            float t = Mathf.InverseLerp(_minDistance, _maxDistance, _distance);
            float pitch = Mathf.Lerp(_minPitch, _maxPitch, t);

            Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
            Vector3 desired = _target.position + _lookAtOffset + rotation * Vector3.back * _distance;

            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _smoothVelocity, _smoothTime);
            transform.rotation = rotation;
        }
    }
}
