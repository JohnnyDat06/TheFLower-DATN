using System;
using UnityEngine;

/// <summary>
/// Maintains the authoritative local storm-pressure distance for the Sand Boat chase.
/// Phase 10 is logic-only: visual storm prefabs are deliberately handled later.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandstormChaseController : MonoBehaviour
{
    [Header("Sand Boat References")]
    [SerializeField, Tooltip("Route movement source that provides the current forward speed and active chase state.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Speed configuration used to determine the neutral speed where the storm neither gains nor loses distance.")]
    private SandBoatSpeedController _speedController;

    [Header("Storm Distance")]
    [SerializeField, Range(0f, 1f), Tooltip("Normalized storm distance when a chase begins. 1 is safest; 0 means caught.")]
    private float _initialStormDistance = 0.8f;
    [SerializeField, Min(0.01f), Tooltip("Normalized distance lost per second at minimum speed below the neutral speed.")]
    private float _stormCatchupRate = 0.12f;
    [SerializeField, Min(0.01f), Tooltip("Normalized distance recovered per second at maximum speed above the neutral speed.")]
    private float _stormRecoveryRate = 0.08f;

    [Header("Storm Thresholds")]
    [SerializeField, Range(0f, 1f), Tooltip("Distance at or below which the state becomes Warning.")]
    private float _warningThreshold = 0.55f;
    [SerializeField, Range(0f, 1f), Tooltip("Distance at or below which the state becomes Critical.")]
    private float _criticalThreshold = 0.25f;
    [SerializeField, Range(0f, 1f), Tooltip("Distance at or below which the storm catches the boat. Phase 12 handles the fail response.")]
    private float _caughtThreshold = 0f;

    [Header("Runtime Debug")]
    [SerializeField, Tooltip("Current normalized storm distance during Play Mode. 1 is safe; 0 is caught.")]
    private float _stormDistance;
    [SerializeField, Tooltip("Current logical storm state during Play Mode.")]
    private SandstormChaseState _state;
    private bool _wasChaseActive;
    private bool _hasCaught;

    /// <summary>Raised once when the logical storm state crosses a threshold.</summary>
    public event Action<SandstormChaseState> StateChanged;

    /// <summary>Current normalized distance from the storm: 1 is safe and 0 is caught.</summary>
    public float StormDistance => _stormDistance;

    /// <summary>Current logical pressure state; Phase 11 will use it for visual feedback.</summary>
    public SandstormChaseState State => _state;

    private void Awake()
    {
        ResetStormDistance();
    }

    private void OnValidate()
    {
        _initialStormDistance = Mathf.Clamp01(_initialStormDistance);
        _stormCatchupRate = Mathf.Max(0.01f, _stormCatchupRate);
        _stormRecoveryRate = Mathf.Max(0.01f, _stormRecoveryRate);
        _warningThreshold = Mathf.Clamp01(_warningThreshold);
        _criticalThreshold = Mathf.Clamp(_criticalThreshold, _caughtThreshold, _warningThreshold);
        _caughtThreshold = Mathf.Clamp(_caughtThreshold, 0f, _criticalThreshold);
    }

    private void Update()
    {
        bool isChaseActive = Application.isPlaying
                             && _movement != null
                             && _movement.IsRouteMovementEnabled;

        if (isChaseActive && !_wasChaseActive)
        {
            ResetStormDistance();
        }

        _wasChaseActive = isChaseActive;
        if (!isChaseActive || _hasCaught)
        {
            return;
        }

        UpdateStormDistance();
        SetState(EvaluateState());
    }

    /// <summary>Restores the logical storm distance for the beginning of a chase attempt.</summary>
    public void ResetStormDistance()
    {
        _stormDistance = _initialStormDistance;
        _hasCaught = false;
        SetState(EvaluateState());
    }

    private void UpdateStormDistance()
    {
        if (_speedController == null)
        {
            return;
        }

        float neutralSpeed = _speedController.BaseForwardSpeed;
        float currentSpeed = _movement.CurrentForwardSpeed;
        float minimumSpeed = _speedController.MinForwardSpeed;
        float maximumSpeed = _speedController.MaxForwardSpeed;

        if (currentSpeed < neutralSpeed)
        {
            float slowRatio = Mathf.InverseLerp(neutralSpeed, minimumSpeed, currentSpeed);
            _stormDistance -= _stormCatchupRate * slowRatio * Time.deltaTime;
        }
        else if (currentSpeed > neutralSpeed)
        {
            float fastRatio = Mathf.InverseLerp(neutralSpeed, maximumSpeed, currentSpeed);
            _stormDistance += _stormRecoveryRate * fastRatio * Time.deltaTime;
        }

        _stormDistance = Mathf.Clamp01(_stormDistance);
    }

    private SandstormChaseState EvaluateState()
    {
        if (_stormDistance <= _caughtThreshold)
        {
            return SandstormChaseState.Caught;
        }

        if (_stormDistance <= _criticalThreshold)
        {
            return SandstormChaseState.Critical;
        }

        return _stormDistance <= _warningThreshold
            ? SandstormChaseState.Warning
            : SandstormChaseState.Safe;
    }

    private void SetState(SandstormChaseState nextState)
    {
        if (_state == nextState)
        {
            return;
        }

        _state = nextState;
        if (_state == SandstormChaseState.Caught)
        {
            _hasCaught = true;
        }

        StateChanged?.Invoke(_state);
    }
}

/// <summary>Logical storm pressure states used by the Sand Boat chase.</summary>
public enum SandstormChaseState
{
    Safe,
    Warning,
    Critical,
    Caught
}
