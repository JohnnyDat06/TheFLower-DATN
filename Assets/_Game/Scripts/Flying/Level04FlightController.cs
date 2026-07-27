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

    private readonly NetworkVariable<bool> _flightEnabled = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Vector3 _windAcceleration;
    private Vector3 _activeBoostDirection;
    private Vector3 _smoothedFlightDirection;
    private float _currentSpeed;
    private float _boostTimer;
    private float _flapTimer;
    private float _flapLiftVelocity;
    private Vector3 _checkpointPosition;
    private Quaternion _checkpointRotation = Quaternion.identity;
    private bool _hasCheckpoint;
    private float _takeoffTimer;
    private float _lastRecoveryServerTime = float.NegativeInfinity;

#if UNITY_EDITOR
    private bool _debugInputOverride;
    private Vector2 _debugMoveInput;
    private bool _debugClimbInput;
    private bool _debugDescendInput;
#endif

    public bool FlightEnabled => _flightEnabled.Value;
    public int CurrentWaypointIndex => -1;
    public Vector3 CurrentWaypointPosition => transform.position;
    public float CurrentSpeed => _currentSpeed;
    public Vector3 CurrentFlightDirection => _smoothedFlightDirection;

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
        Vector2 moveInput = ReadMoveInput();
        bool wantsForward = moveInput.y > 0.1f;
        bool wantsBrake = moveInput.y < -0.1f;
        bool wantsDescend = ReadDescendInput();
        bool wantsClimb = ReadClimbInput();
        float lateralInput = moveInput.x;

        Vector3 cameraDirection = CameraManager.Instance != null
            ? CameraManager.Instance.FlightSteeringDirection
            : transform.forward;
        if (cameraDirection.sqrMagnitude < 0.01f)
        {
            cameraDirection = transform.forward;
        }
        cameraDirection = ClampFlightDirectionPitch(cameraDirection);

        Vector3 currentDirection = ResolveCurrentFlightDirection();
        Vector3 cameraGuidedDirection = Vector3.Slerp(
            currentDirection,
            cameraDirection,
            _config.CameraSteeringWeight).normalized;
        Vector3 requestedDirection =
            Quaternion.AngleAxis(
                lateralInput * _config.KeyboardTurnAngle,
                Vector3.up)
            * cameraGuidedDirection;

        if (wantsBrake)
        {
            requestedDirection = Vector3.Slerp(
                requestedDirection,
                currentDirection,
                _config.BrakeDirectionHold).normalized;
        }

        if (_boostTimer > 0f && _activeBoostDirection.sqrMagnitude > 0.01f)
        {
            requestedDirection = Vector3.Slerp(
                requestedDirection,
                _activeBoostDirection,
                _config.RingGuidanceWeight).normalized;
        }

        requestedDirection = ClampFlightDirectionPitch(requestedDirection);

        float steeringResponsiveness = _config.DirectionResponsiveness;
        if (requestedDirection.y < currentDirection.y)
        {
            steeringResponsiveness *= _config.DiveSteeringMultiplier;
        }

        float directionBlend = 1f - Mathf.Exp(-steeringResponsiveness * dt);
        _smoothedFlightDirection = Vector3.Slerp(
            currentDirection,
            requestedDirection,
            directionBlend).normalized;
        _smoothedFlightDirection =
            ClampFlightDirectionPitch(_smoothedFlightDirection);

        Vector3 flightRight =
            Vector3.Cross(Vector3.up, _smoothedFlightDirection).normalized;
        if (flightRight.sqrMagnitude < 0.01f) flightRight = transform.right;

        float bank = -lateralInput * _config.MaxBankAngle;
        Vector3 modelDirection = Vector3.Slerp(
            _smoothedFlightDirection,
            requestedDirection,
            _config.ModelCameraTiltWeight).normalized;
        modelDirection = ClampFlightDirectionPitch(modelDirection);
        Quaternion targetRotation =
            Quaternion.LookRotation(modelDirection, Vector3.up)
            * Quaternion.Euler(0f, 0f, bank);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Mathf.Clamp01(_config.RotationResponsiveness * dt));

        float targetSpeed;
        if (wantsForward)
        {
            targetSpeed = _input != null && _input.IsSprinting
                ? _config.BoostSpeed
                : _config.NormalFlySpeed;
        }
        else if (wantsBrake)
        {
            targetSpeed = _config.BrakeSpeed;
        }
        else
        {
            targetSpeed = _currentSpeed > 0.1f
                ? _config.GlideSpeed
                : 0f;
        }

        if (_boostTimer > 0f)
        {
            _boostTimer -= dt;
            if (wantsForward)
            {
                targetSpeed = Mathf.Max(targetSpeed, _config.BoostSpeed);
            }
            if (_boostTimer <= 0f)
            {
                _activeBoostDirection = Vector3.zero;
            }
        }

        if (IsGalaxyGateSpeedState())
        {
            targetSpeed *= _config.GalaxyGateSpeedMultiplier;
        }

        float diveAmount = Mathf.Clamp01(-_smoothedFlightDirection.y);
        float climbAmount = Mathf.Clamp01(_smoothedFlightDirection.y);
        targetSpeed += diveAmount * _config.DiveSpeedBonus;
        targetSpeed -= climbAmount * _config.ClimbSpeedPenalty;
        targetSpeed = Mathf.Max(targetSpeed, 0f);

        float acceleration = wantsBrake
            ? _config.BrakeDeceleration
            : _config.Acceleration;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, acceleration * dt);
        Vector3 desiredVelocity = _smoothedFlightDirection * _currentSpeed;
        desiredVelocity += flightRight
            * (lateralInput * _config.LateralMoveSpeed);
        if (wantsForward)
        {
            desiredVelocity += Vector3.ClampMagnitude(
                _windAcceleration,
                _config.WindAssistStrength);
        }

        if (_takeoffTimer > 0f && wantsForward)
        {
            _takeoffTimer -= dt;
            _currentSpeed = Mathf.Max(_currentSpeed, _config.TakeoffForwardSpeed);
            desiredVelocity = _smoothedFlightDirection * _currentSpeed;
        }

        UpdateWingBeat(wantsForward, dt);
        float levelFlightAmount = 1f - Mathf.Abs(_smoothedFlightDirection.y);
        desiredVelocity.y -= _config.GlideSinkSpeed * levelFlightAmount;
        desiredVelocity.y += _flapLiftVelocity;

        if (wantsClimb)
        {
            desiredVelocity.y += _config.ClimbSpeed;
        }
        if (wantsDescend)
        {
            desiredVelocity.y -= _config.DescendSpeed;
        }
        desiredVelocity.y = Mathf.Max(desiredVelocity.y, -_config.MaxFallSpeed);

        float velocityBlend = 1f - Mathf.Exp(
            -_config.VelocityResponsiveness * dt);
        _rigidbody.linearVelocity = Vector3.Lerp(
            _rigidbody.linearVelocity,
            desiredVelocity,
            velocityBlend);

        _windAcceleration = Vector3.zero;
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
        _activeBoostDirection = impulse.normalized;
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
        _takeoffTimer = 0.35f;
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
                _takeoffTimer = _config != null ? _config.TakeoffDuration : 1f;
                _currentSpeed = 0f;
                _smoothedFlightDirection = transform.forward;
                _flapTimer = 0f;
                _flapLiftVelocity = 0f;
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
        }

        if (_stateMachine != null)
        {
            _stateMachine.TransitionTo(enabled ? PlayerStateType.AirGlide : PlayerStateType.Jump);
        }

        if (CameraManager.Instance != null)
        {
            CameraManager.Instance.SwitchCamera(
                enabled ? CameraPreset.FlyDown : CameraPreset.ThirdPerson);
        }
    }

    private ClientRpcParams TargetOwner()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
    }

    private Vector3 ResolveCurrentFlightDirection()
    {
        if (_smoothedFlightDirection.sqrMagnitude > 0.01f)
        {
            return _smoothedFlightDirection.normalized;
        }

        if (_rigidbody != null && _rigidbody.linearVelocity.sqrMagnitude > 0.25f)
        {
            return _rigidbody.linearVelocity.normalized;
        }

        return transform.forward.sqrMagnitude > 0.01f
            ? transform.forward.normalized
            : Vector3.forward;
    }

    private Vector3 ClampFlightDirectionPitch(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return transform.forward;

        direction.Normalize();
        float maximumVertical = Mathf.Sin(
            Mathf.Clamp(_config.MaxPitch, 0f, 89f) * Mathf.Deg2Rad);
        float vertical = Mathf.Clamp(direction.y, -maximumVertical, maximumVertical);

        Vector3 planar = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (planar.sqrMagnitude < 0.0001f)
        {
            planar = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (planar.sqrMagnitude < 0.0001f) planar = Vector3.forward;
        }

        float planarMagnitude = Mathf.Sqrt(Mathf.Max(0f, 1f - vertical * vertical));
        return planar.normalized * planarMagnitude + Vector3.up * vertical;
    }

    private void UpdateWingBeat(bool wantsForward, float dt)
    {
        _flapLiftVelocity = Mathf.MoveTowards(
            _flapLiftVelocity,
            0f,
            _config.FlapLiftDecay * dt);

        if (!wantsForward)
        {
            _flapTimer = Mathf.Min(
                _flapTimer,
                _config.FlapInterval * 0.35f);
            return;
        }

        _flapTimer -= dt;
        if (_flapTimer > 0f) return;

        _flapTimer = _config.FlapInterval;
        _flapLiftVelocity = Mathf.Min(
            _flapLiftVelocity + _config.FlapLiftVelocity,
            _config.MaxFlapLiftVelocity);
    }

    private static bool IsGalaxyGateSpeedState()
    {
        if (Level04FlowManager.Instance == null) return false;

        Level04Phase phase = Level04FlowManager.Instance.Phase;
        return phase is Level04Phase.GalaxyGate
            or Level04Phase.TimeWarpAscent;
    }

    private Vector2 ReadMoveInput()
    {
#if UNITY_EDITOR
        if (_debugInputOverride) return _debugMoveInput;
#endif
        return _input != null ? _input.MoveInput : Vector2.zero;
    }

    private bool ReadClimbInput()
    {
#if UNITY_EDITOR
        if (_debugInputOverride) return _debugClimbInput;
#endif
        return _input != null && _input.JumpHeld;
    }

    private bool ReadDescendInput()
    {
#if UNITY_EDITOR
        if (_debugInputOverride) return _debugDescendInput;
#endif
        return _input != null && _input.IsCrouching;
    }

#if UNITY_EDITOR
    public void SetDebugFlightInput(
        Vector2 moveInput,
        bool climb = false,
        bool descend = false)
    {
        _debugInputOverride = true;
        _debugMoveInput = Vector2.ClampMagnitude(moveInput, 1f);
        _debugClimbInput = climb;
        _debugDescendInput = descend;
    }

    public void ClearDebugFlightInput()
    {
        _debugInputOverride = false;
        _debugMoveInput = Vector2.zero;
        _debugClimbInput = false;
        _debugDescendInput = false;
    }
#endif

}
