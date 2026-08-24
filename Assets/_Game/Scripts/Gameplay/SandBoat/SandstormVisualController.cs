using UnityEngine;

/// <summary>
/// Drives the Phase 11 storm visual from the Phase 10 logical storm state.
/// The visual has no collision or fail authority.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandstormVisualController : MonoBehaviour
{
    [Header("Sand Boat References")]
    [SerializeField, Tooltip("Sand Boat route source used to place the storm behind the current route direction.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Logical storm-distance source. This component never writes gameplay state.")]
    private SandstormChaseController _stormLogic;
    [SerializeField, Tooltip("Terrain used to keep the tornado visual on the ground.")]
    private Terrain _terrain;
    [SerializeField, Tooltip("Scene instance of TornadoWithWindEfc used only as the storm visual.")]
    private Transform _stormVisual;

    [Header("Visual Placement")]
    [SerializeField, Min(0f), Tooltip("Closest visual distance behind the boat when the storm has caught up.")]
    private float _minimumVisualDistance = 18f;
    [SerializeField, Min(0.01f), Tooltip("Farthest visual distance behind the boat when Storm Distance is fully safe.")]
    private float _maximumVisualDistance = 85f;
    [SerializeField, Tooltip("Vertical offset applied after the visual is aligned to the terrain ground.")]
    private float _groundOffset;
    [SerializeField, Min(0.01f), Tooltip("Higher values make the tornado follow the boat's target position more quickly.")]
    private float _followSmoothing = 5f;

    [Header("Visual Intensity")]
    [SerializeField, Min(0.01f), Tooltip("Scale and particle multiplier while the storm is Safe.")]
    private float _safeIntensity = 1f;
    [SerializeField, Min(0.01f), Tooltip("Scale and particle multiplier while the storm is Warning.")]
    private float _warningIntensity = 1.2f;
    [SerializeField, Min(0.01f), Tooltip("Scale and particle multiplier while the storm is Critical.")]
    private float _criticalIntensity = 1.45f;
    [SerializeField, Min(0.01f), Tooltip("Scale and particle multiplier after the storm reaches Caught state.")]
    private float _caughtIntensity = 1.65f;

    private ParticleSystem[] _particleSystems;
    private float[] _baseEmissionRates;
    private Vector3 _baseScale = Vector3.one;

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
            audioSource.enabled = false;
        }

        foreach (Collider collider in _stormVisual.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }
    }

    private void LateUpdate()
    {
        bool shouldShow = Application.isPlaying
                          && _movement != null
                          && _stormLogic != null
                          && _stormVisual != null
                          && _movement.IsRouteMovementEnabled;
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
