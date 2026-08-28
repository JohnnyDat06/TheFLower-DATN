using System;
using UnityEngine;

/// <summary>
/// Drives local-only Sand Boat particles from the already synchronized chase state.
/// All referenced particle objects are instances of existing project VFX prefabs.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandBoatChaseVFX : MonoBehaviour
{
    [Header("Trạng thái gameplay")]
    [SerializeField, Tooltip("Boarding cho biết khi nào chase đang chạy để bật hoặc dừng particle liên tục.")]
    private SandBoatBoarding _boarding;
    [SerializeField, Tooltip("Movement cung cấp tốc độ và khoảng lệch ngang hiện tại đã đồng bộ từ server.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Nguồn trạng thái bão dùng để tăng mật độ cát bay qua camera khi bão nguy hiểm.")]
    private SandstormChaseController _stormLogic;
    [SerializeField, Tooltip("Nguồn sự kiện va chạm đã được server xác nhận để phát bụi đúng một lần trên mỗi máy.")]
    private SandBoatNetworkSynchronizer _networkSynchronizer;

    [Header("Particle từ asset có sẵn")]
    [SerializeField, Tooltip("Ba particle TFF_Smoke_01A đã đổi màu cát, đặt sát dưới đít thuyền để tạo vệt cát dày liên tục.")]
    private ParticleSystem[] _sandTrailParticles;
    [SerializeField, Tooltip("Particle TFD_Dust_01A ở mé trái, chỉ tăng mạnh khi thuyền dịch sang phải.")]
    private ParticleSystem _leftSteeringDust;
    [SerializeField, Tooltip("Particle TFD_Dust_01A ở mé phải, chỉ tăng mạnh khi thuyền dịch sang trái.")]
    private ParticleSystem _rightSteeringDust;
    [SerializeField, Tooltip("Particle TFD_Dust_01A dạng one-shot phát tại thuyền sau collision authoritative.")]
    private ParticleSystem _collisionDust;
    [SerializeField, Tooltip("Particle TFD_Sand_Strands_02A gắn trước Main Camera để tạo cát bay ngang màn hình.")]
    private ParticleSystem _cameraSand;

    [Header("Cân chỉnh VFX")]
    [SerializeField, Min(0f), Tooltip("Số hạt cát mỗi giây của từng emitter khi thuyền ở tốc độ thấp nhất.")]
    private float _minimumTrailEmission = 75f;
    [SerializeField, Min(0f), Tooltip("Số hạt cát mỗi giây của từng emitter khi thuyền ở tốc độ cao nhất.")]
    private float _maximumTrailEmission = 170f;
    [SerializeField, Min(0.01f), Tooltip("Tốc độ dịch ngang tối thiểu để particle bụi đánh lái bắt đầu rõ ràng.")]
    private float _steeringDustThreshold = 0.25f;
    [SerializeField, Min(0f), Tooltip("Số hạt mỗi giây tối đa của bụi đánh lái.")]
    private float _maximumSteeringEmission = 55f;
    [SerializeField, Min(1), Tooltip("Số hạt bụi phát một lần khi thuyền va chạm đá.")]
    private int _collisionBurstCount = 42;

    private float _previousHorizontalOffset;

    private void OnValidate()
    {
        _minimumTrailEmission = Mathf.Max(0f, _minimumTrailEmission);
        _maximumTrailEmission = Mathf.Max(_minimumTrailEmission, _maximumTrailEmission);
        _steeringDustThreshold = Mathf.Max(0.01f, _steeringDustThreshold);
        _maximumSteeringEmission = Mathf.Max(0f, _maximumSteeringEmission);
        _collisionBurstCount = Mathf.Max(1, _collisionBurstCount);
    }

    private void Awake()
    {
        _previousHorizontalOffset = _movement != null ? _movement.HorizontalOffset : 0f;
        StopContinuousParticles(true);
    }

    private void OnEnable()
    {
        if (_networkSynchronizer != null)
        {
            _networkSynchronizer.CollisionConfirmedLocally += PlayCollisionDust;
        }
    }

    private void OnDisable()
    {
        if (_networkSynchronizer != null)
        {
            _networkSynchronizer.CollisionConfirmedLocally -= PlayCollisionDust;
        }

        StopContinuousParticles(true);
    }

    private void Update()
    {
        bool chaseActive = Application.isPlaying
                           && _boarding != null
                           && _boarding.ChaseStarted
                           && _movement != null;
        if (!chaseActive)
        {
            StopContinuousParticles(false);
            _previousHorizontalOffset = _movement != null ? _movement.HorizontalOffset : 0f;
            return;
        }

        float speedRatio = Mathf.InverseLerp(8f, 24f, _movement.CurrentForwardSpeed);
        float trailEmission = Mathf.Lerp(_minimumTrailEmission, _maximumTrailEmission, speedRatio);
        SetEmission(_sandTrailParticles, trailEmission);

        float horizontalSpeed = (_movement.HorizontalOffset - _previousHorizontalOffset)
                                / Mathf.Max(Time.deltaTime, 0.0001f);
        _previousHorizontalOffset = _movement.HorizontalOffset;
        float steeringStrength = Mathf.InverseLerp(
            _steeringDustThreshold,
            _steeringDustThreshold * 8f,
            Mathf.Abs(horizontalSpeed));
        SetEmission(
            _leftSteeringDust,
            horizontalSpeed > _steeringDustThreshold ? steeringStrength * _maximumSteeringEmission : 0f);
        SetEmission(
            _rightSteeringDust,
            horizontalSpeed < -_steeringDustThreshold ? steeringStrength * _maximumSteeringEmission : 0f);

        if (_cameraSand != null && !_cameraSand.isPlaying)
        {
            _cameraSand.Play(true);
        }

        if (_cameraSand != null)
        {
            ParticleSystem.MainModule main = _cameraSand.main;
            float stormMultiplier = _stormLogic != null && _stormLogic.State == SandstormChaseState.Critical
                ? 1.35f
                : _stormLogic != null && _stormLogic.State == SandstormChaseState.Warning
                    ? 1.15f
                    : 1f;
            main.simulationSpeed = stormMultiplier;
        }
    }

    private void PlayCollisionDust()
    {
        if (_collisionDust == null)
        {
            return;
        }

        _collisionDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _collisionDust.Emit(_collisionBurstCount);
    }

    private void StopContinuousParticles(bool clear)
    {
        SetEmission(_sandTrailParticles, 0f);
        SetEmission(_leftSteeringDust, 0f);
        SetEmission(_rightSteeringDust, 0f);
        if (_cameraSand != null && _cameraSand.isPlaying)
        {
            _cameraSand.Stop(
                true,
                clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private static void SetEmission(ParticleSystem[] particles, float ratePerSecond)
    {
        if (particles == null)
        {
            return;
        }

        foreach (ParticleSystem particle in particles)
        {
            SetEmission(particle, ratePerSecond);
        }
    }

    private static void SetEmission(ParticleSystem particle, float ratePerSecond)
    {
        if (particle == null)
        {
            return;
        }

        ParticleSystem.EmissionModule emission = particle.emission;
        float clampedRate = Mathf.Max(0f, ratePerSecond);
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(clampedRate);
        if (clampedRate > 0f && !particle.isPlaying)
        {
            particle.Play(true);
        }
    }
}
