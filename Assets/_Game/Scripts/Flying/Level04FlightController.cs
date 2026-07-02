using System.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Level04FlightController : NetworkBehaviour
{
    [SerializeField] private SOFlyingConfig _config;

    private Rigidbody _rigidbody;
    private PlayerController _playerController;
    private PlayerInputHandler _input;
    private PlayerStateMachine _stateMachine;
    private PlayerWingController _wingController;
    private NGOPlayerSync _playerSync;
    private Level04FlightPath _flightPath;

    private readonly NetworkVariable<bool> _flightEnabled = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Vector3 _windAcceleration;
    private float _currentSpeed;
    private float _boostTimer;
    private Vector3 _checkpointPosition;
    private Quaternion _checkpointRotation = Quaternion.identity;
    private bool _hasCheckpoint;
    private int _pathWaypointIndex = -1;
    private float _takeoffTimer;
    private bool _recoveryRequested;
    private float _lastRecoveryServerTime = float.NegativeInfinity;

    public bool FlightEnabled => _flightEnabled.Value;
    public int CurrentWaypointIndex => _pathWaypointIndex;
    public Vector3 CurrentWaypointPosition => _flightPath != null && _pathWaypointIndex >= 0
        ? _flightPath.GetWaypointPosition(_pathWaypointIndex)
        : transform.position;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _playerController = GetComponent<PlayerController>();
        _input = GetComponent<PlayerInputHandler>();
        _stateMachine = GetComponent<PlayerStateMachine>();
        _wingController = GetComponent<PlayerWingController>();
        _playerSync = GetComponent<NGOPlayerSync>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _flightEnabled.OnValueChanged += HandleFlightEnabledChanged;
        ApplyFlightMode(_flightEnabled.Value);
    }

    public override void OnNetworkDespawn()
    {
        _flightEnabled.OnValueChanged -= HandleFlightEnabledChanged;
        ApplyFlightMode(false);
        base.OnNetworkDespawn();
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsOwner || !_flightEnabled.Value || _config == null) return;
        if (_playerSync != null && _playerSync.IsTeleporting) return;

        // NGOPlayerSync restores normal gravity after a teleport. Flight owns the
        // Rigidbody in this mode, so enforce the flight physics every tick.
        _rigidbody.useGravity = false;

        float dt = Time.fixedDeltaTime;
        Vector2 moveInput = _input != null ? _input.MoveInput : Vector2.zero;
        bool wantsForward = moveInput.y > 0.1f;
        float lateralInput = moveInput.x;

        Vector3 pathDirection = GetPathDirection();
        if (pathDirection.sqrMagnitude < 0.01f)
        {
            pathDirection = transform.forward;
        }

        Vector3 pathRight = Vector3.Cross(Vector3.up, pathDirection).normalized;
        if (pathRight.sqrMagnitude < 0.01f) pathRight = transform.right;
        Quaternion steeringRotation =
            Quaternion.AngleAxis(lateralInput * _config.MaximumSteeringOffset, Vector3.up);
        Vector3 playerDirection = (steeringRotation * pathDirection).normalized;
        float steeringInfluence = _config.PlayerSteeringInfluence
            * (1f - _config.PathAssistWeight * 0.5f);
        Vector3 guidedDirection = Vector3.Slerp(
            pathDirection,
            playerDirection,
            Mathf.Clamp01(steeringInfluence)).normalized;

        float bank = -lateralInput * _config.MaxBankAngle;
        Quaternion targetRotation = Quaternion.LookRotation(guidedDirection, Vector3.up)
            * Quaternion.Euler(0f, 0f, bank);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Mathf.Clamp01(_config.RotationResponsiveness * dt));

        float targetSpeed = 0f;
        if (wantsForward)
        {
            targetSpeed = _input != null && _input.IsSprinting
                ? _config.BoostSpeed
                : _config.NormalFlySpeed;
        }

        if (_boostTimer > 0f)
        {
            _boostTimer -= dt;
            if (wantsForward)
            {
                targetSpeed = Mathf.Max(targetSpeed, _config.BoostSpeed);
            }
        }

        float acceleration = wantsForward
            ? _config.Acceleration
            : _config.Acceleration * _config.IdleDecelerationMultiplier;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, acceleration * dt);
        Vector3 desiredVelocity = guidedDirection * _currentSpeed;
        desiredVelocity += pathRight * (lateralInput * _config.LateralMoveSpeed);
        if (wantsForward)
        {
            desiredVelocity += Vector3.ClampMagnitude(
                _windAcceleration,
                _config.WindAssistStrength);
        }
        desiredVelocity.y = Mathf.Max(desiredVelocity.y, -_config.MaxFallSpeed);

        if (_takeoffTimer > 0f && wantsForward)
        {
            _takeoffTimer -= dt;
            desiredVelocity = Vector3.ProjectOnPlane(guidedDirection, Vector3.up).normalized
                * Mathf.Max(_currentSpeed, _config.TakeoffForwardSpeed);
            desiredVelocity += pathRight * (lateralInput * _config.LateralMoveSpeed);
            desiredVelocity.y = Mathf.Max(
                guidedDirection.y * _currentSpeed,
                _config.TakeoffLiftSpeed);
        }

        _rigidbody.linearVelocity = Vector3.Lerp(
            _rigidbody.linearVelocity,
            desiredVelocity,
            Mathf.Clamp01(_config.Acceleration * dt));

        _windAcceleration = Vector3.zero;
        CheckPathRecovery();
    }

    public void SetFlightEnabledServer(bool enabled)
    {
        if (!IsServer) return;
        if (enabled && !_hasCheckpoint)
        {
            _checkpointPosition = transform.position;
            _checkpointRotation = transform.rotation;
            _hasCheckpoint = true;
        }
        _flightEnabled.Value = enabled;
    }

    public void SetCheckpointServer(Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;
        _checkpointPosition = position;
        _checkpointRotation = rotation;
        _hasCheckpoint = true;
    }

    public void RecoverToCheckpointServer()
    {
        if (!IsServer || !_hasCheckpoint) return;
        if (Time.time - _lastRecoveryServerTime
            < (_config != null ? _config.RecoveryRequestCooldown : 2f))
        {
            ClearRecoveryRequestClientRpc(TargetOwner());
            return;
        }
        _lastRecoveryServerTime = Time.time;

        Vector3 target = _checkpointPosition
            + Vector3.up * (_config != null ? _config.RecoveryHeightOffset : 25f);

        ResetPathProgressClientRpc(target, TargetOwner());

        if (_playerSync != null)
        {
            _playerSync.Teleport(target, _checkpointRotation);
        }
        else
        {
            RecoverClientRpc(target, _checkpointRotation, TargetOwner());
        }

        _wingController?.SetStateServer(PlayerWingState.Recovering);
        StartCoroutine(RestoreWingAfterRecovery());
    }

    [ServerRpc]
    private void RequestPathRecoveryServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (!_flightEnabled.Value) return;
        RecoverToCheckpointServer();
    }

    public void ApplyBoostServer(Vector3 direction, float force, float lift)
    {
        if (!IsServer) return;
        Vector3 impulse = direction.normalized * force + Vector3.up * lift;
        ApplyBoostClientRpc(impulse, TargetOwner());
    }

    public void ApplyWind(Vector3 acceleration)
    {
        if (!IsOwner || !_flightEnabled.Value) return;
        _windAcceleration += acceleration;
    }

    [ClientRpc]
    private void ApplyBoostClientRpc(Vector3 impulse, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner || !_flightEnabled.Value) return;
        _rigidbody.linearVelocity += impulse;
        _boostTimer = _config != null ? _config.BoostDuration : 1f;
        _wingController?.PlayBoostLocal(_boostTimer);
    }

    [ClientRpc]
    private void RecoverClientRpc(Vector3 position, Quaternion rotation, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(position, rotation);
        _currentSpeed = 0f;
    }

    [ClientRpc]
    private void ResetPathProgressClientRpc(
        Vector3 position,
        ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;
        ResolveFlightPath();
        _pathWaypointIndex = _flightPath != null
            ? _flightPath.FindClosestWaypointIndex(position)
            : -1;
        _takeoffTimer = 0.35f;
        _recoveryRequested = false;
    }

    [ClientRpc]
    private void ClearRecoveryRequestClientRpc(ClientRpcParams rpcParams = default)
    {
        if (IsOwner) _recoveryRequested = false;
    }

    private void HandleFlightEnabledChanged(bool previous, bool current)
    {
        ApplyFlightMode(current);
    }

    private IEnumerator RestoreWingAfterRecovery()
    {
        yield return new WaitForSeconds(0.75f);
        if (IsServer && _flightEnabled.Value)
        {
            _wingController?.SetStateServer(PlayerWingState.Gliding);
        }
    }

    private void ApplyFlightMode(bool enabled)
    {
        if (!IsOwner) return;

        _playerController?.SetExternalMovementOverride(enabled);
        if (_rigidbody != null)
        {
            _rigidbody.useGravity = !enabled;
            if (enabled)
            {
                ResolveFlightPath();
                _pathWaypointIndex = _flightPath != null
                    ? _flightPath.FindClosestWaypointIndex(transform.position)
                    : -1;
                _takeoffTimer = _config != null ? _config.TakeoffDuration : 1f;
                _currentSpeed = 0f;
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
        }

        if (_stateMachine != null)
        {
            _stateMachine.TransitionTo(enabled ? PlayerStateType.AirGlide : PlayerStateType.Jump);
        }
    }

    private ClientRpcParams TargetOwner()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
    }

    private Vector3 GetPathDirection()
    {
        ResolveFlightPath();
        if (_flightPath == null || _pathWaypointIndex < 0) return transform.forward;

        return _flightPath.GetGuidanceDirection(
            transform.position,
            ref _pathWaypointIndex,
            _config.WaypointReachDistance);
    }

    private void ResolveFlightPath()
    {
        if (_flightPath == null)
        {
            _flightPath = FindFirstObjectByType<Level04FlightPath>();
        }
    }

    private void CheckPathRecovery()
    {
        if (_recoveryRequested || _flightPath == null || _pathWaypointIndex < 0) return;
        if (_flightPath.GetDistanceToPath(transform.position, _pathWaypointIndex)
            <= _config.MaximumPathDeviation)
        {
            return;
        }

        _recoveryRequested = true;
        RequestPathRecoveryServerRpc();
    }
}
