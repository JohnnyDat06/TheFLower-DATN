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
    [SerializeField, Tooltip("Nguồn chuyển động Route cung cấp tốc độ tiến hiện tại và trạng thái chase đang hoạt động.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Cấu hình tốc độ dùng để xác định tốc độ mặc định; bão vẫn tiến gần nhẹ ở tốc độ này và chỉ lùi xa khi P2 tăng tốc cao hơn.")]
    private SandBoatSpeedController _speedController;

    [Header("Storm Distance")]
    [SerializeField, Range(0f, 1f), Tooltip("Khoảng cách bão chuẩn hóa khi chase bắt đầu. 1 là an toàn nhất; 0 là bị bắt kịp.")]
    private float _initialStormDistance = 0.8f;
    [SerializeField, Min(0.01f), Tooltip("Khoảng cách chuẩn hóa mất mỗi giây khi thuyền chạy ở tốc độ tối thiểu.")]
    private float _stormCatchupRate = 0.12f;
    [SerializeField, Min(0.001f), Tooltip("Khoảng cách chuẩn hóa mất mỗi giây khi thuyền chỉ giữ tốc độ mặc định mà không tăng tốc.")]
    private float _baseSpeedCatchupRate = 0.02f;
    [SerializeField, Min(0.01f), Tooltip("Khoảng cách chuẩn hóa hồi phục mỗi giây khi thuyền tăng tốc cao hơn tốc độ mặc định.")]
    private float _stormRecoveryRate = 0.08f;

    [Header("Storm Thresholds")]
    [SerializeField, Range(0f, 1f), Tooltip("Khoảng cách từ ngưỡng này trở xuống sẽ chuyển sang trạng thái Warning.")]
    private float _warningThreshold = 0.55f;
    [SerializeField, Range(0f, 1f), Tooltip("Tiến độ Route chuẩn hóa tại đó chase chuyển sang Critical. 0.3 nghĩa là 30% Route; khoảng cách bão không điều khiển trạng thái này.")]
    private float _criticalRouteProgress = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("Khoảng cách từ ngưỡng này trở xuống bão sẽ bắt kịp thuyền. Phase 12 xử lý phản hồi thất bại.")]
    private float _caughtThreshold = 0f;

    [Header("Runtime Debug")]
    [SerializeField, Tooltip("Khoảng cách bão chuẩn hóa hiện tại trong Play Mode. 1 là an toàn; 0 là bị bắt kịp.")]
    private float _stormDistance;
    [SerializeField, Tooltip("Trạng thái logic hiện tại của bão trong Play Mode.")]
    private SandstormChaseState _state;
    private bool _wasChaseActive;
    private bool _hasCaught;
    private bool _usesNetworkReplicaState;

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
        _baseSpeedCatchupRate = Mathf.Clamp(_baseSpeedCatchupRate, 0.001f, _stormCatchupRate);
        _stormRecoveryRate = Mathf.Max(0.01f, _stormRecoveryRate);
        _warningThreshold = Mathf.Clamp01(_warningThreshold);
        _criticalRouteProgress = Mathf.Clamp01(_criticalRouteProgress);
        _caughtThreshold = Mathf.Clamp01(_caughtThreshold);
    }

    private void Update()
    {
        if (_usesNetworkReplicaState)
        {
            return;
        }

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

    /// <summary>Applies the server-authoritative storm state to a client replica.</summary>
    public void ApplyNetworkState(float stormDistance, SandstormChaseState state)
    {
        _stormDistance = Mathf.Clamp01(stormDistance);
        _hasCaught = state == SandstormChaseState.Caught;
        SetState(state);
    }

    /// <summary>Prevents a client replica from calculating its own storm distance.</summary>
    public void SetNetworkReplicaMode(bool isReplica)
    {
        _usesNetworkReplicaState = isReplica;
    }

    private void UpdateStormDistance()
    {
        if (_speedController == null)
        {
            return;
        }

        float defaultSpeed = _speedController.BaseForwardSpeed;
        float currentSpeed = _movement.CurrentForwardSpeed;
        float minimumSpeed = _speedController.MinForwardSpeed;
        float maximumSpeed = _speedController.MaxForwardSpeed;

        if (currentSpeed <= defaultSpeed)
        {
            float slowRatio = Mathf.InverseLerp(defaultSpeed, minimumSpeed, currentSpeed);
            float catchupRate = Mathf.Lerp(_baseSpeedCatchupRate, _stormCatchupRate, slowRatio);
            _stormDistance -= catchupRate * Time.deltaTime;
        }
        else
        {
            float fastRatio = Mathf.InverseLerp(defaultSpeed, maximumSpeed, currentSpeed);
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

        if (_movement != null && _movement.Progress >= _criticalRouteProgress)
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
