using UnityEngine;

namespace FaceSlapper.VFX
{
    /// <summary>
    /// Drives atmospheric heating from meteor speed and air density.
    /// The VFX root follows velocity without rotating the gameplay object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MeteorAtmosphereVFX : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform _visualRoot;
        [SerializeField] private MeshRenderer _shockArc;
        [SerializeField] private ParticleSystem _flameParticles;
        [SerializeField] private ParticleSystem _smokeParticles;
        [SerializeField] private ParticleSystem _sparkParticles;
        [SerializeField] private TrailRenderer _hotTrail;
        [SerializeField] private Light _glowLight;
        [SerializeField] private Rigidbody _velocitySource;

        [Header("Atmosphere")]
        [SerializeField, Range(0f, 1f)] private float _atmosphereDensity = 1f;
        [SerializeField] private float _minSpeed = 8f;
        [SerializeField] private float _maxSpeed = 45f;
        [SerializeField, Min(0.1f)] private float _heatResponse = 5f;
        [SerializeField, Min(0.1f)] private float _rotationResponse = 12f;

        [Header("Visuals")]
        [SerializeField, Min(0f)] private float _trailWidth = 0.65f;
        [SerializeField, Min(0f)] private float _lightIntensity = 3f;

        private static readonly int HeatId = Shader.PropertyToID("_Heat");

        private MaterialPropertyBlock _arcProperties;
        private Vector3 _arcBaseScale;
        private Vector3 _previousPosition;
        private Vector3 _velocityOverride;
        private bool _hasVelocityOverride;
        private bool _isPlaying = true;
        private float _heat;

        public float Heat => _heat;

        private void Awake()
        {
            _arcProperties = new MaterialPropertyBlock();
            _arcBaseScale = _shockArc != null ? _shockArc.transform.localScale : Vector3.one;
            _previousPosition = transform.position;
            ApplyHeat();
        }

        private void OnEnable()
        {
            _previousPosition = transform.position;
            if (_hotTrail != null)
                _hotTrail.Clear();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            Vector3 velocity = GetVelocity(deltaTime);
            float speed = velocity.magnitude;
            float speedHeat = Mathf.InverseLerp(_minSpeed, _maxSpeed, speed);
            float targetHeat = _isPlaying ? speedHeat * _atmosphereDensity : 0f;

            float heatBlend = 1f - Mathf.Exp(-_heatResponse * deltaTime);
            _heat = Mathf.Lerp(_heat, targetHeat, heatBlend);

            if (_visualRoot != null && speed > 0.1f)
            {
                Vector3 direction = velocity / speed;
                Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.98f
                    ? Vector3.forward
                    : Vector3.up;
                Quaternion targetRotation = Quaternion.LookRotation(direction, up);
                float rotationBlend = 1f - Mathf.Exp(-_rotationResponse * deltaTime);
                _visualRoot.rotation = Quaternion.Slerp(
                    _visualRoot.rotation,
                    targetRotation,
                    rotationBlend);
            }

            ApplyHeat();
        }

        /// <summary>0 means vacuum, 1 means dense atmosphere.</summary>
        public void SetAtmosphereDensity(float density)
        {
            _atmosphereDensity = Mathf.Clamp01(density);
        }

        /// <summary>Use this for predicted or kinematic movement.</summary>
        public void SetVelocity(Vector3 velocity)
        {
            _velocityOverride = velocity;
            _hasVelocityOverride = true;
        }

        public void ClearVelocityOverride()
        {
            _hasVelocityOverride = false;
        }

        public void Play()
        {
            _isPlaying = true;
            PlaySystem(_flameParticles);
            PlaySystem(_smokeParticles);
            PlaySystem(_sparkParticles);
        }

        public void Stop(bool clearImmediately = false)
        {
            _isPlaying = false;
            ParticleSystemStopBehavior stopBehavior = clearImmediately
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting;

            StopSystem(_flameParticles, stopBehavior);
            StopSystem(_smokeParticles, stopBehavior);
            StopSystem(_sparkParticles, stopBehavior);

            if (_hotTrail != null)
            {
                _hotTrail.emitting = false;
                if (clearImmediately)
                    _hotTrail.Clear();
            }
        }

        private Vector3 GetVelocity(float deltaTime)
        {
            Vector3 position = transform.position;
            Vector3 positionDelta = position - _previousPosition;
            _previousPosition = position;

            if (_hasVelocityOverride)
                return _velocityOverride;

            if (_velocitySource != null)
                return _velocitySource.velocity;

            return deltaTime > Mathf.Epsilon ? positionDelta / deltaTime : Vector3.zero;
        }

        private void ApplyHeat()
        {
            float emissionHeat = _isPlaying ? _heat : 0f;
            SetEmission(_flameParticles, emissionHeat);
            SetEmission(_smokeParticles, emissionHeat * 0.65f);
            SetEmission(_sparkParticles, emissionHeat * emissionHeat);

            if (_hotTrail != null)
            {
                _hotTrail.widthMultiplier = _trailWidth * _heat;
                _hotTrail.emitting = _isPlaying && _heat > 0.02f;
            }

            if (_shockArc != null)
            {
                _shockArc.transform.localScale = _arcBaseScale * Mathf.Lerp(0.65f, 1.1f, _heat);
                _shockArc.GetPropertyBlock(_arcProperties);
                _arcProperties.SetFloat(HeatId, _heat);
                _shockArc.SetPropertyBlock(_arcProperties);
            }

            if (_glowLight != null)
                _glowLight.intensity = _lightIntensity * _heat;
        }

        private static void SetEmission(ParticleSystem system, float multiplier)
        {
            if (system == null)
                return;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTimeMultiplier = multiplier;
        }

        private static void PlaySystem(ParticleSystem system)
        {
            if (system != null && !system.isPlaying)
                system.Play(false);
        }

        private static void StopSystem(
            ParticleSystem system,
            ParticleSystemStopBehavior stopBehavior)
        {
            if (system != null)
                system.Stop(false, stopBehavior);
        }
    }
}
