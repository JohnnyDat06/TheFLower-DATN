using System;
using UnityEngine;

/// <summary>
/// Converts the Phase 10 Caught state into a single Sand Boat chase failure.
/// Checkpoint reset is intentionally deferred to Phase 13.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandBoatChaseFailController : MonoBehaviour
{
    [Header("Sand Boat References")]
    [SerializeField, Tooltip("Storm logic that reports when the storm has caught the boat.")]
    private SandstormChaseController _stormLogic;
    [SerializeField, Tooltip("Route movement stopped immediately when the chase fails.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("P1 steering controller disabled while the chase is failed.")]
    private SandBoatSteering _steering;
    [SerializeField, Tooltip("P2 speed controller disabled while the chase is failed.")]
    private SandBoatSpeedController _speedController;

    [Header("Fail Feedback")]
    [SerializeField, Min(0f), Tooltip("Short pause after failure before Phase 13 is allowed to reset the chase.")]
    private float _feedbackDuration = 1.5f;

    [Header("Runtime Debug")]
    [SerializeField, Tooltip("True after the storm catches the boat. This state is raised only once per chase attempt.")]
    private bool _isFailed;
    [SerializeField, Tooltip("True after the short fail-feedback pause has elapsed and a later checkpoint reset may begin.")]
    private bool _isReadyForReset;

    private float _feedbackTimeRemaining;

    /// <summary>Raised once after route movement and chase controls have been locked.</summary>
    public event Action ChaseFailed;

    /// <summary>True while the current chase attempt is failed and awaiting a later reset.</summary>
    public bool IsFailed => _isFailed;

    /// <summary>True when Phase 13 may begin its checkpoint reset flow.</summary>
    public bool IsReadyForReset => _isReadyForReset;

    private void OnEnable()
    {
        if (_stormLogic != null)
        {
            _stormLogic.StateChanged += OnStormStateChanged;
        }
    }

    private void OnDisable()
    {
        if (_stormLogic != null)
        {
            _stormLogic.StateChanged -= OnStormStateChanged;
        }
    }

    private void Update()
    {
        if (!_isFailed || _isReadyForReset)
        {
            return;
        }

        _feedbackTimeRemaining = Mathf.Max(0f, _feedbackTimeRemaining - Time.deltaTime);
        _isReadyForReset = _feedbackTimeRemaining <= 0f;
    }

    private void OnStormStateChanged(SandstormChaseState state)
    {
        if (state == SandstormChaseState.Caught)
        {
            TriggerFail();
        }
    }

    private void TriggerFail()
    {
        if (_isFailed)
        {
            return;
        }

        _isFailed = true;
        _isReadyForReset = _feedbackDuration <= 0f;
        _feedbackTimeRemaining = _feedbackDuration;
        _movement?.SetRouteMovementEnabled(false);

        if (_steering != null)
        {
            _steering.enabled = false;
        }

        if (_speedController != null)
        {
            _speedController.enabled = false;
        }

        Debug.Log("[SandBoatChaseFail] The storm caught the boat. Chase controls are locked pending reset.", this);
        ChaseFailed?.Invoke();
    }

    /// <summary>Clears the fail gate after Phase 13 has restored every chase subsystem.</summary>
    public void ResetFailState()
    {
        _isFailed = false;
        _isReadyForReset = false;
        _feedbackTimeRemaining = 0f;
    }
}
