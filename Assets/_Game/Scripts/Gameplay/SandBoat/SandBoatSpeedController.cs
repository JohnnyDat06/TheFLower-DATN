using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Converts P2's vertical movement axis into a bounded forward speed for the Sand Boat.
/// P2 is the non-host player in the current project role convention; P1 has no speed-control path.
/// </summary>
[DefaultExecutionOrder(-60)]
[DisallowMultipleComponent]
public sealed class SandBoatSpeedController : MonoBehaviour
{
    [SerializeField, Tooltip("SandBoatMovement nhận tốc độ tiến đã tính toán.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Tham chiếu runtime tới P2, người chơi được phép điều khiển tốc độ.")]
    private PlayerInputHandler _speedPlayer;
    [SerializeField, Min(0.01f), Tooltip("Tốc độ thuyền thấp nhất. Thuyền không dừng hoặc chạy lùi dưới giá trị này.")]
    private float _minForwardSpeed = 8f;
    [SerializeField, Min(0.01f), Tooltip("Tốc độ mặc định khi chase bắt đầu. Tăng giá trị để thuyền khởi hành nhanh hơn.")]
    private float _baseForwardSpeed = 18f;
    [SerializeField, Min(0.01f), Tooltip("Tốc độ tối đa P2 có thể đạt khi giữ W.")]
    private float _maxForwardSpeed = 24f;
    [SerializeField, Min(0.01f), Tooltip("Tốc độ tăng mỗi giây khi P2 giữ W.")]
    private float _accelerationRate = 8f;
    [SerializeField, Min(0.01f), Tooltip("Tốc độ giảm mỗi giây khi P2 giữ S.")]
    private float _brakeRate = 12f;

    [Header("Debug")]
    [SerializeField, Tooltip("Cho phép host giả lập input W/S của P2 để manual test Phase 5.")]
    private bool _allowHostSpeedDebug;
    [SerializeField, Tooltip("Player host chỉ dùng khi bật debug tốc độ host.")]
    private PlayerInputHandler _hostDebugPlayer;

    private float _currentForwardSpeed;
    private float _collisionRecoveryTargetSpeed;
    private float _collisionRecoveryRate;
    private float _collisionRecoveryTimeRemaining;
    private bool _hasAuthoritativeSpeedInput;
    private float _authoritativeSpeedInput;

    /// <summary>Current boat speed after P2 input and the configured bounds are applied.</summary>
    public float CurrentForwardSpeed => _currentForwardSpeed;

    /// <summary>Default chase speed; the storm still applies passive catchup until P2 accelerates above it.</summary>
    public float BaseForwardSpeed => _baseForwardSpeed;

    /// <summary>Lowest configured forward speed available to P2.</summary>
    public float MinForwardSpeed => _minForwardSpeed;

    /// <summary>Highest configured forward speed available to P2.</summary>
    public float MaxForwardSpeed => _maxForwardSpeed;

    /// <summary>True while a collision slowdown is recovering toward its pre-hit speed.</summary>
    public bool IsRecoveringFromCollision => _collisionRecoveryTimeRemaining > 0f;

    private void OnValidate()
    {
        _minForwardSpeed = Mathf.Max(0.01f, _minForwardSpeed);
        _maxForwardSpeed = Mathf.Max(_minForwardSpeed, _maxForwardSpeed);
        _baseForwardSpeed = Mathf.Clamp(_baseForwardSpeed, _minForwardSpeed, _maxForwardSpeed);
        _accelerationRate = Mathf.Max(0.01f, _accelerationRate);
        _brakeRate = Mathf.Max(0.01f, _brakeRate);
        _currentForwardSpeed = Mathf.Clamp(_currentForwardSpeed, _minForwardSpeed, _maxForwardSpeed);
    }

    private void Awake()
    {
        _currentForwardSpeed = Mathf.Clamp(_baseForwardSpeed, _minForwardSpeed, _maxForwardSpeed);
        ApplySpeed();
    }

    private void Update()
    {
        if (!Application.isPlaying || _movement == null)
        {
            return;
        }

        TryResolveP2Input();
        TryResolveHostDebugInput();
        float speedInput = GetP2SpeedInput();
        UpdateSpeed(speedInput);
        RecoverCollisionSpeed(speedInput);
        ApplySpeed();
    }

    /// <summary>Assigns P2 explicitly; Phase 6 will call this after boarding.</summary>
    public void AssignSpeedPlayer(PlayerInputHandler speedPlayer)
    {
        _speedPlayer = speedPlayer;
    }

    /// <summary>Restores the default speed and clears any collision-recovery state for a checkpoint retry.</summary>
    public void ResetSpeed()
    {
        _currentForwardSpeed = Mathf.Clamp(_baseForwardSpeed, _minForwardSpeed, _maxForwardSpeed);
        _collisionRecoveryTargetSpeed = _currentForwardSpeed;
        _collisionRecoveryRate = 0f;
        _collisionRecoveryTimeRemaining = 0f;
        ApplySpeed();
    }

    /// <summary>
    /// Reduces the current speed once, then restores it smoothly over the supplied duration.
    /// Braking continues to take precedence over automatic recovery.
    /// </summary>
    public void ApplyCollisionSpeedPenalty(float speedPenalty, float recoveryTime)
    {
        float clampedPenalty = Mathf.Max(0f, speedPenalty);
        if (clampedPenalty <= 0f)
        {
            return;
        }

        float speedBeforeHit = _currentForwardSpeed;
        _currentForwardSpeed = Mathf.Max(_minForwardSpeed, speedBeforeHit - clampedPenalty);
        _collisionRecoveryTargetSpeed = Mathf.Max(_currentForwardSpeed, speedBeforeHit);
        _collisionRecoveryTimeRemaining = Mathf.Max(0.01f, recoveryTime);
        _collisionRecoveryRate = (_collisionRecoveryTargetSpeed - _currentForwardSpeed)
                                 / _collisionRecoveryTimeRemaining;
    }

    /// <summary>Uses the P2 input value that the server accepted from the owning client.</summary>
    public void SetAuthoritativeSpeedInput(float speedInput)
    {
        _hasAuthoritativeSpeedInput = true;
        _authoritativeSpeedInput = Mathf.Clamp(speedInput, -1f, 1f);
    }

    /// <summary>Returns speed input handling to the existing local/debug path.</summary>
    public void ClearAuthoritativeSpeedInput()
    {
        _hasAuthoritativeSpeedInput = false;
        _authoritativeSpeedInput = 0f;
    }

    /// <summary>Updates client-side read-only speed presentation from the server snapshot.</summary>
    public void ApplyNetworkSpeed(float forwardSpeed)
    {
        _currentForwardSpeed = Mathf.Clamp(forwardSpeed, _minForwardSpeed, _maxForwardSpeed);
        ApplySpeed();
    }

    private void UpdateSpeed(float speedInput)
    {
        if (speedInput > 0f)
        {
            _currentForwardSpeed = Mathf.MoveTowards(
                _currentForwardSpeed,
                _maxForwardSpeed,
                _accelerationRate * speedInput * Time.deltaTime);
        }
        else if (speedInput < 0f)
        {
            _currentForwardSpeed = Mathf.MoveTowards(
                _currentForwardSpeed,
                _minForwardSpeed,
                _brakeRate * -speedInput * Time.deltaTime);
        }
    }

    private void RecoverCollisionSpeed(float speedInput)
    {
        if (_collisionRecoveryTimeRemaining <= 0f || speedInput < 0f)
        {
            return;
        }

        _currentForwardSpeed = Mathf.MoveTowards(
            _currentForwardSpeed,
            _collisionRecoveryTargetSpeed,
            _collisionRecoveryRate * Time.deltaTime);
        _collisionRecoveryTimeRemaining = Mathf.Max(
            0f,
            _collisionRecoveryTimeRemaining - Time.deltaTime);
    }

    private void ApplySpeed()
    {
        if (_movement != null)
        {
            _movement.SetForwardSpeed(_currentForwardSpeed);
        }
    }

    private void TryResolveP2Input()
    {
        if (IsP2(_speedPlayer))
        {
            return;
        }

        foreach (PlayerInputHandler inputHandler in FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None))
        {
            if (!IsP2(inputHandler))
            {
                continue;
            }

            _speedPlayer = inputHandler;
            return;
        }
    }

    private float GetP2SpeedInput()
    {
        if (_hasAuthoritativeSpeedInput)
        {
            return _authoritativeSpeedInput;
        }

        if (IsP2(_speedPlayer) && _speedPlayer.IsOwner)
        {
            return Mathf.Clamp(_speedPlayer.MoveInput.y, -1f, 1f);
        }

        return CanHostEmulateP2Input()
            ? Mathf.Clamp(_hostDebugPlayer.MoveInput.y, -1f, 1f)
            : 0f;
    }

    private void TryResolveHostDebugInput()
    {
        if (!_allowHostSpeedDebug || IsP1(_hostDebugPlayer))
        {
            return;
        }

        foreach (PlayerInputHandler inputHandler in FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None))
        {
            if (!IsP1(inputHandler))
            {
                continue;
            }

            _hostDebugPlayer = inputHandler;
            return;
        }
    }

    private bool CanHostEmulateP2Input()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return _allowHostSpeedDebug
               && manager != null
               && manager.IsHost
               && IsP1(_hostDebugPlayer)
               && _hostDebugPlayer.IsOwner;
    }

    private static bool IsP2(PlayerInputHandler inputHandler)
    {
        if (inputHandler == null || !inputHandler.IsSpawned)
        {
            return false;
        }

        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && inputHandler.OwnerClientId != NetworkManager.ServerClientId;
    }

    private static bool IsP1(PlayerInputHandler inputHandler)
    {
        if (inputHandler == null || !inputHandler.IsSpawned)
        {
            return false;
        }

        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && inputHandler.OwnerClientId == NetworkManager.ServerClientId;
    }
}
