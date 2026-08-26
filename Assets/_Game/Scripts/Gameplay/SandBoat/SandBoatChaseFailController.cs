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
    [SerializeField, Tooltip("Logic bão báo khi bão đã bắt kịp thuyền.")]
    private SandstormChaseController _stormLogic;
    [SerializeField, Tooltip("Dừng chuyển động theo Route ngay khi chase thất bại.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Tắt controller đánh lái P1 trong trạng thái chase thất bại.")]
    private SandBoatSteering _steering;
    [SerializeField, Tooltip("Tắt controller tốc độ P2 trong trạng thái chase thất bại.")]
    private SandBoatSpeedController _speedController;

    [Header("Fail Feedback")]
    [SerializeField, Min(0f), Tooltip("Khoảng dừng ngắn sau thất bại trước khi Phase 13 được phép reset chase.")]
    private float _feedbackDuration = 1.5f;

    [Header("Runtime Debug")]
    [SerializeField, Tooltip("Đúng sau khi bão bắt kịp thuyền; trạng thái này chỉ phát một lần mỗi lượt chase.")]
    private bool _isFailed;
    [SerializeField, Tooltip("Đúng sau khi hết khoảng phản hồi thất bại ngắn và checkpoint có thể bắt đầu reset.")]
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
            TriggerFail("[SandBoatChaseFail] The storm caught the boat. Chase controls are locked pending reset.");
        }
    }

    /// <summary>Fails the current chase after the boat hits an authored blocking rock.</summary>
    public void TriggerObstacleFail()
    {
        TriggerFail("[SandBoatChaseFail] The boat hit a blocking rock. Chase controls are locked pending reset.");
    }

    /// <summary>Fails the chase when the moving storm catches a player during the temple transition.</summary>
    public void TriggerTempleStormFail()
    {
        TriggerFail("[SandBoatChaseFail] The storm caught a player before VaoDen. Chase reset is pending.");
    }

    private void TriggerFail(string logMessage)
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

        Debug.Log(logMessage, this);
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
