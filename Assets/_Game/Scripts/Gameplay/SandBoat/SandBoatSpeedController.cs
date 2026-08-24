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
    [SerializeField, Tooltip("SandBoatMovement that receives the resolved forward speed.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Runtime reference to P2, the player allowed to control speed.")]
    private PlayerInputHandler _speedPlayer;
    [SerializeField, Min(0.01f), Tooltip("Lowest allowed boat speed. The boat never stops or reverses below this value.")]
    private float _minForwardSpeed = 8f;
    [SerializeField, Min(0.01f), Tooltip("Default speed when the chase begins. Increase this value to make the boat start faster.")]
    private float _baseForwardSpeed = 15f;
    [SerializeField, Min(0.01f), Tooltip("Maximum speed P2 can reach while holding W.")]
    private float _maxForwardSpeed = 24f;
    [SerializeField, Min(0.01f), Tooltip("Speed gained per second while P2 holds W.")]
    private float _accelerationRate = 8f;
    [SerializeField, Min(0.01f), Tooltip("Speed lost per second while P2 holds S.")]
    private float _brakeRate = 12f;

    [Header("Debug")]
    [SerializeField, Tooltip("Allows the host to temporarily emulate P2 W/S input for Phase 5 manual testing.")]
    private bool _allowHostSpeedDebug;
    [SerializeField, Tooltip("Host player used only when host speed debug is enabled.")]
    private PlayerInputHandler _hostDebugPlayer;

    private float _currentForwardSpeed;
    private float _collisionRecoveryTargetSpeed;
    private float _collisionRecoveryRate;
    private float _collisionRecoveryTimeRemaining;

    /// <summary>Current boat speed after P2 input and the configured bounds are applied.</summary>
    public float CurrentForwardSpeed => _currentForwardSpeed;

    /// <summary>Neutral chase speed where the storm neither catches up nor recovers distance.</summary>
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
