using UnityEngine;

/// <summary>
/// Drives the Phase 11 storm visual from the Phase 10 logical storm state.
/// The visual has no collision or fail authority.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandstormVisualController : MonoBehaviour
{
    [Header("Sand Boat References")]
    [SerializeField, Tooltip("Nguồn Route của thuyền, dùng để đặt bão phía sau theo hướng Route hiện tại.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Nguồn khoảng cách bão logic. Component này không ghi vào trạng thái gameplay.")]
    private SandstormChaseController _stormLogic;
    [SerializeField, Tooltip("Terrain dùng để giữ hiệu ứng lốc xoáy trên mặt đất.")]
    private Terrain _terrain;
    [SerializeField, Tooltip("Instance TornadoWithWindEfc trong scene, chỉ dùng làm hiệu ứng hình ảnh của bão.")]
    private Transform _stormVisual;
    [SerializeField, Tooltip("Đích ngoài đường ray mà bão bay tới sau TempleFinish; gán TFD_City_Gate_01 (1) trong scene.")]
    private Transform _templeTransitionTarget;

    [Header("Visual Placement")]
    [SerializeField, Min(0f), Tooltip("Khoảng cách hình ảnh gần nhất phía sau thuyền khi bão đã áp sát.")]
    private float _minimumVisualDistance = 18f;
    [SerializeField, Min(0.01f), Tooltip("Khoảng cách hình ảnh xa nhất phía sau thuyền khi Storm Distance hoàn toàn an toàn.")]
    private float _maximumVisualDistance = 85f;
    [SerializeField, Tooltip("Độ lệch chiều cao áp dụng sau khi hiệu ứng được đặt theo mặt terrain.")]
    private float _groundOffset;
    [SerializeField, Min(0.01f), Tooltip("Giá trị cao hơn giúp lốc xoáy bám theo vị trí mục tiêu của thuyền nhanh hơn.")]
    private float _followSmoothing = 5f;

    [Header("Bão sau TempleFinish")]
    [SerializeField, Min(0.01f), Tooltip("Tốc độ visual bão rời đường ray và bay tới TFD_City_Gate_01 (1) sau TempleFinish.")]
    private float _templeTransitionApproachSpeed = 20f;

    [Header("Visual Intensity")]
    [SerializeField, Min(0.01f), Tooltip("Hệ số scale và particle khi bão ở trạng thái Safe.")]
    private float _safeIntensity = 1f;
    [SerializeField, Min(0.01f), Tooltip("Hệ số scale và particle khi bão ở trạng thái Warning.")]
    private float _warningIntensity = 1.2f;
    [SerializeField, Min(0.01f), Tooltip("Hệ số scale và particle khi bão ở trạng thái Critical.")]
    private float _criticalIntensity = 1.45f;
    [SerializeField, Min(0.01f), Tooltip("Hệ số scale và particle sau khi bão đạt trạng thái Caught.")]
    private float _caughtIntensity = 1.65f;

    private ParticleSystem[] _particleSystems;
    private float[] _baseEmissionRates;
    private Vector3 _baseScale = Vector3.one;
    private bool _showDuringTempleTransition;

    /// <summary>
    /// Keeps the storm visual active after the boat has stopped at TempleFinish.
    /// Phase 19 clears this only when players enter the VaoDen shelter trigger.
    /// </summary>
    public void SetVisibleDuringTempleTransition(bool isVisible)
    {
        _showDuringTempleTransition = isVisible;
    }

    /// <summary>Returns the current storm visual position for server-authoritative transition hazards.</summary>
    public bool TryGetVisualPosition(out Vector3 position)
    {
        position = _stormVisual != null ? _stormVisual.position : default;
        return _stormVisual != null && _stormVisual.gameObject.activeInHierarchy;
    }

    private void Awake()
    {
        if (_stormVisual == null)
        {
            return;
        }

        _baseScale = _stormVisual.localScale;
        _particleSystems = _stormVisual.GetComponentsInChildren<ParticleSystem>(true);
        _baseEmissionRates = new float[_particleSystems.Length];
        for (int index = 0; index < _particleSystems.Length; index++)
        {
            _baseEmissionRates[index] = _particleSystems[index].emission.rateOverTimeMultiplier;
        }

        foreach (AudioSource audioSource in _stormVisual.GetComponentsInChildren<AudioSource>(true))
        {
            audioSource.Stop();
            audioSource.playOnAwake = false;
            audioSource.enabled = false;
        }

        foreach (Collider collider in _stormVisual.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }
    }

    private void OnValidate()
    {
        _minimumVisualDistance = Mathf.Max(0f, _minimumVisualDistance);
        _maximumVisualDistance = Mathf.Max(_minimumVisualDistance, _maximumVisualDistance);
        _followSmoothing = Mathf.Max(0.01f, _followSmoothing);
        _templeTransitionApproachSpeed = Mathf.Max(0.01f, _templeTransitionApproachSpeed);
    }

    private void LateUpdate()
    {
        bool shouldShow = Application.isPlaying
                          && _movement != null
                          && _stormLogic != null
                          && _stormVisual != null
                          && (_movement.IsRouteMovementEnabled || _showDuringTempleTransition);
        if (_stormVisual != null && _stormVisual.gameObject.activeSelf != shouldShow)
        {
            _stormVisual.gameObject.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        SandBoatRouteSample routeSample = _movement.EvaluateRouteAhead(0f);
        float distance = Mathf.Lerp(_minimumVisualDistance, _maximumVisualDistance, _stormLogic.StormDistance);
        if (_showDuringTempleTransition && _templeTransitionTarget != null)
        {
            Vector3 gatePosition = _templeTransitionTarget.position;
            _stormVisual.position = Vector3.MoveTowards(
                _stormVisual.position,
                gatePosition,
                _templeTransitionApproachSpeed * Time.deltaTime);
            Vector3 moveDirection = gatePosition - _stormVisual.position;
            if (moveDirection.sqrMagnitude > 0.0001f)
            {
                _stormVisual.rotation = Quaternion.LookRotation(moveDirection.normalized, Vector3.up);
            }

            ApplyIntensity(GetIntensityForState(_stormLogic.State));
            return;
        }

        Vector3 targetPosition = transform.position - routeSample.Forward * distance;
        if (_terrain != null)
        {
            targetPosition.y = _terrain.SampleHeight(targetPosition) + _terrain.transform.position.y + _groundOffset;
        }

        float followFactor = 1f - Mathf.Exp(-_followSmoothing * Time.deltaTime);
        _stormVisual.position = Vector3.Lerp(_stormVisual.position, targetPosition, followFactor);
        _stormVisual.rotation = Quaternion.LookRotation(routeSample.Forward, Vector3.up);
        ApplyIntensity(GetIntensityForState(_stormLogic.State));
    }

    private float GetIntensityForState(SandstormChaseState state)
    {
        return state switch
        {
            SandstormChaseState.Warning => _warningIntensity,
            SandstormChaseState.Critical => _criticalIntensity,
            SandstormChaseState.Caught => _caughtIntensity,
            _ => _safeIntensity
        };
    }

    private void ApplyIntensity(float intensity)
    {
        _stormVisual.localScale = _baseScale * intensity;
        for (int index = 0; index < _particleSystems.Length; index++)
        {
            ParticleSystem.EmissionModule emission = _particleSystems[index].emission;
            emission.rateOverTimeMultiplier = _baseEmissionRates[index] * intensity;
        }
    }
}
