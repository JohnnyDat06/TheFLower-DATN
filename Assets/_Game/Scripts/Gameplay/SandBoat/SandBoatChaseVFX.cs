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
    [SerializeField, Tooltip("Particle bụi đánh lái bên trái được giữ để tương thích scene cũ và luôn bị tắt.")]
    private ParticleSystem _leftSteeringDust;
    [SerializeField, Tooltip("Particle bụi đánh lái bên phải được giữ để tương thích scene cũ và luôn bị tắt.")]
    private ParticleSystem _rightSteeringDust;
    [SerializeField, Tooltip("Particle TFD_Dust_01A dạng one-shot phát tại thuyền sau collision authoritative.")]
    private ParticleSystem _collisionDust;
    [SerializeField, Tooltip("Particle TFD_Sand_Strands_02A gắn trước Main Camera để tạo cát bay ngang màn hình.")]
    private ParticleSystem _cameraSand;

    [Header("Cân chỉnh VFX")]
    [SerializeField, ColorUsage(true, true), Tooltip("Màu sáng nhân riêng lên material của bụi cát sau thuyền; không làm thay đổi material khói gốc trong project.")]
    private Color _sandTrailMaterialTint = new Color(1.35f, 0.82f, 0.38f, 1f);
    [SerializeField, Min(0f), Tooltip("Số hạt cát mỗi giây của từng emitter khi thuyền ở tốc độ thấp nhất.")]
    private float _minimumTrailEmission = 75f;
    [SerializeField, Min(0f), Tooltip("Số hạt cát mỗi giây của từng emitter khi thuyền ở tốc độ cao nhất.")]
    private float _maximumTrailEmission = 170f;

    [SerializeField, Min(1), Tooltip("Số hạt bụi phát một lần khi thuyền va chạm đá.")]
    private int _collisionBurstCount = 42;

    private void OnValidate()
    {
        _minimumTrailEmission = Mathf.Max(0f, _minimumTrailEmission);
        _maximumTrailEmission = Mathf.Max(_minimumTrailEmission, _maximumTrailEmission);
        _collisionBurstCount = Mathf.Max(1, _collisionBurstCount);

        ApplySandTrailAppearance();
    }

    private void Awake()
    {
        ApplySandTrailAppearance();
        StopContinuousParticles(true);
    }

    private void OnEnable()
    {
        ApplySandTrailAppearance();

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
            return;
        }

        float speedRatio = Mathf.InverseLerp(8f, 24f, _movement.CurrentForwardSpeed);
        float trailEmission = Mathf.Lerp(_minimumTrailEmission, _maximumTrailEmission, speedRatio);
        SetEmission(_sandTrailParticles, trailEmission);

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
        DisableParticle(_leftSteeringDust, clear);
        DisableParticle(_rightSteeringDust, clear);
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

    private void ApplySandTrailAppearance()
    {
        if (_sandTrailParticles == null)
        {
            return;
        }

        foreach (ParticleSystem particle in _sandTrailParticles)
        {
            if (particle == null)
            {
                continue;
            }

            ParticleSystemRenderer particleRenderer = particle.GetComponent<ParticleSystemRenderer>();
            if (particleRenderer == null)
            {
                continue;
            }

            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            particleRenderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", _sandTrailMaterialTint);
            properties.SetColor("_Color", _sandTrailMaterialTint);
            particleRenderer.SetPropertyBlock(properties);
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

    private static void DisableParticle(ParticleSystem particle, bool clear)
    {
        if (particle == null)
        {
            return;
        }

        ParticleSystem.EmissionModule emission = particle.emission;
        emission.enabled = false;
        particle.Stop(
            true,
            clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
    }
}
