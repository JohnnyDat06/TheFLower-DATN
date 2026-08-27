using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Keeps the server as the only Sand Boat simulation authority. Existing
/// Boarding and Completion NetworkVariables remain the authority for roles,
/// chase start, and temple completion; this component only synchronizes the
/// continuously changing simulation state and forwards P2 input to the server.
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(NetworkObject))]
[DisallowMultipleComponent]
public sealed class SandBoatNetworkSynchronizer : NetworkBehaviour
{
    [Header("Tham chiếu Sand Boat")]
    [SerializeField, Tooltip("Boarding authoritative hiện có cung cấp role P1/P2 và trạng thái bắt đầu chase.")]
    private SandBoatBoarding _boarding;
    [SerializeField, Tooltip("Nguồn mô phỏng tiến độ Route authoritative trên server và pose hiển thị trên client.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Nguồn khoảng lệch ngang của thuyền; component này chỉ được mô phỏng trên server.")]
    private SandBoatHorizontalOffset _horizontalOffset;
    [SerializeField, Tooltip("Controller lái P1 chạy trên server; client không tự mô phỏng lái thuyền.")]
    private SandBoatSteering _steering;
    [SerializeField, Tooltip("Controller tốc độ nhận input P2 đã được server xác thực.")]
    private SandBoatSpeedController _speedController;
    [SerializeField, Tooltip("Logic khoảng cách và trạng thái bão chỉ được server mô phỏng.")]
    private SandstormChaseController _stormLogic;
    [SerializeField, Tooltip("Nguồn sự kiện va chạm authoritative; chỉ server được tính va chạm gameplay.")]
    private SandBoatCollisionHandler _collisionHandler;
    [SerializeField, Tooltip("Trạng thái fail được lấy từ server và hiển thị nhất quán trên client.")]
    private SandBoatChaseFailController _failController;
    [SerializeField, Tooltip("Giao dịch checkpoint chỉ được chạy trên server; client nhận kết quả qua state đồng bộ.")]
    private SandBoatChaseCheckpoint _checkpoint;

    [Header("Tần suất input mạng")]
    [SerializeField, Min(0.02f), Tooltip("Khoảng thời gian tối thiểu giữa hai gói input tốc độ P2 gửi lên server.")]
    private float _inputSendInterval = 0.05f;
    [SerializeField, Min(0.1f), Tooltip("Khoảng thời gian gửi lại input P2 dù giá trị không đổi để server không giữ input cũ khi mất gói.")]
    private float _inputKeepAliveInterval = 0.25f;

    [Header("Làm mượt bản sao Client")]
    [SerializeField, Min(0.01f), Tooltip("Tốc độ sửa sai tiến độ Route trên client khi state mạng lệch khỏi server.")]
    private float _replicaProgressCorrectionRate = 0.35f;
    [SerializeField, Min(0.01f), Tooltip("Độ nhạy làm mượt khoảng lệch ngang của thuyền trên client.")]
    private float _replicaOffsetSmoothing = 18f;
    [SerializeField, Min(0f), Tooltip("Thời gian ngoại suy tối đa dùng để bù trễ gói state trên client.")]
    private float _maximumPredictionTime = 0.2f;

    private readonly NetworkVariable<SandBoatSimulationSnapshot> _simulationState = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<uint> _collisionSequence = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private PlayerInputHandler _localInput;
    private float _serverP2SpeedInput;
    private float _lastSentP2SpeedInput;
    private float _nextInputSendTime;
    private float _lastInputSentTime;
    private bool _hasSentInput;

    /// <summary>Raised locally once for each server-confirmed obstacle collision.</summary>
    public event Action CollisionConfirmedLocally;

    /// <summary>Latest server-authoritative continuous chase state.</summary>
    public SandBoatSimulationSnapshot SimulationState => _simulationState.Value;

    private void OnValidate()
    {
        _inputSendInterval = Mathf.Max(0.02f, _inputSendInterval);
        _inputKeepAliveInterval = Mathf.Max(0.1f, _inputKeepAliveInterval);
        _replicaProgressCorrectionRate = Mathf.Max(0.01f, _replicaProgressCorrectionRate);
        _replicaOffsetSmoothing = Mathf.Max(0.01f, _replicaOffsetSmoothing);
        _maximumPredictionTime = Mathf.Max(0f, _maximumPredictionTime);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _collisionSequence.OnValueChanged += OnCollisionSequenceChanged;

        if (IsServer)
        {
            if (_collisionHandler != null)
            {
                _collisionHandler.ObstacleHit += OnServerObstacleHit;
            }

            PublishServerState();
            return;
        }

        ConfigureClientReplica();
        ApplyReplicaState(true);
    }

    public override void OnNetworkDespawn()
    {
        _collisionSequence.OnValueChanged -= OnCollisionSequenceChanged;
        if (IsServer && _collisionHandler != null)
        {
            _collisionHandler.ObstacleHit -= OnServerObstacleHit;
        }

        _speedController?.ClearAuthoritativeSpeedInput();
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsSpawned)
        {
            return;
        }

        if (IsServer)
        {
            ApplyServerP2Input();
        }
        else
        {
            SubmitLocalP2Input();
        }
    }

    private void LateUpdate()
    {
        if (!IsSpawned)
        {
            return;
        }

        if (IsServer)
        {
            PublishServerState();
        }
        else
        {
            ApplyReplicaState(false);
        }
    }

    private void ConfigureClientReplica()
    {
        _movement?.SetNetworkReplicaMode(true);

        if (_horizontalOffset != null)
        {
            _horizontalOffset.enabled = false;
        }

        if (_steering != null)
        {
            _steering.enabled = false;
        }

        if (_speedController != null)
        {
            _speedController.enabled = false;
        }

        if (_stormLogic != null)
        {
            _stormLogic.SetNetworkReplicaMode(true);
            _stormLogic.enabled = false;
        }

        if (_collisionHandler != null)
        {
            _collisionHandler.enabled = false;
        }

        if (_checkpoint != null)
        {
            _checkpoint.enabled = false;
        }
    }

    private void ApplyServerP2Input()
    {
        if (_speedController == null || _boarding == null)
        {
            return;
        }

        if (!_boarding.ChaseStarted || !_boarding.IsP2Seated)
        {
            _serverP2SpeedInput = 0f;
            _speedController.ClearAuthoritativeSpeedInput();
            return;
        }

        if (NetworkManager == null
            || !NetworkManager.ConnectedClients.ContainsKey(_boarding.P2ClientId))
        {
            _serverP2SpeedInput = 0f;
            _speedController.SetAuthoritativeSpeedInput(0f);
            return;
        }

        _speedController.SetAuthoritativeSpeedInput(_serverP2SpeedInput);
    }

    private void SubmitLocalP2Input()
    {
        if (_boarding == null
            || !_boarding.ChaseStarted
            || !_boarding.IsLocalClientP2
            || NetworkManager == null)
        {
            return;
        }

        ResolveLocalInput();
        if (_localInput == null)
        {
            return;
        }

        float speedInput = Mathf.Clamp(_localInput.MoveInput.y, -1f, 1f);
        float now = Time.unscaledTime;
        bool valueChanged = !_hasSentInput || Mathf.Abs(speedInput - _lastSentP2SpeedInput) >= 0.01f;
        bool keepAliveDue = now - _lastInputSentTime >= _inputKeepAliveInterval;
        if (now < _nextInputSendTime || (!valueChanged && !keepAliveDue))
        {
            return;
        }

        _lastSentP2SpeedInput = speedInput;
        _lastInputSentTime = now;
        _nextInputSendTime = now + _inputSendInterval;
        _hasSentInput = true;
        SubmitP2SpeedInputRpc(speedInput);
    }

    private void ResolveLocalInput()
    {
        if (_localInput != null && _localInput.IsSpawned && _localInput.IsOwner)
        {
            return;
        }

        _localInput = null;
        foreach (PlayerInputHandler inputHandler in FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None))
        {
            if (!inputHandler.IsSpawned || !inputHandler.IsOwner)
            {
                continue;
            }

            _localInput = inputHandler;
            return;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitP2SpeedInputRpc(float speedInput, RpcParams rpcParams = default)
    {
        if (float.IsNaN(speedInput) || float.IsInfinity(speedInput))
        {
            return;
        }

        if (_boarding == null
            || !_boarding.ChaseStarted
            || !_boarding.IsP2Seated
            || rpcParams.Receive.SenderClientId != _boarding.P2ClientId)
        {
            return;
        }

        _serverP2SpeedInput = Mathf.Clamp(speedInput, -1f, 1f);
    }

    private void PublishServerState()
    {
        if (_movement == null || _stormLogic == null)
        {
            return;
        }

        _simulationState.Value = new SandBoatSimulationSnapshot
        {
            Progress = _movement.Progress,
            HorizontalOffset = _movement.HorizontalOffset,
            CurrentSpeed = _movement.CurrentForwardSpeed,
            StormDistance = _stormLogic.StormDistance,
            StormState = _stormLogic.State,
            RouteMovementEnabled = _movement.IsRouteMovementEnabled,
            IsComplete = _movement.IsComplete,
            IsFailed = _failController != null && _failController.IsFailed,
            IsResetting = _checkpoint != null && _checkpoint.IsResetting,
            ServerTime = NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble
        };
    }

    private void ApplyReplicaState(bool forceSnap)
    {
        if (_movement == null)
        {
            return;
        }

        SandBoatSimulationSnapshot snapshot = _simulationState.Value;
        double currentServerTime = NetworkManager != null
            ? NetworkManager.ServerTime.Time
            : Time.timeAsDouble;
        float predictionTime = snapshot.RouteMovementEnabled && !snapshot.IsComplete
            ? Mathf.Clamp((float)(currentServerTime - snapshot.ServerTime), 0f, _maximumPredictionTime)
            : 0f;
        float predictedProgress = snapshot.Progress;
        if (_movement.RouteLength > 0f)
        {
            predictedProgress += snapshot.CurrentSpeed / _movement.RouteLength * predictionTime;
        }

        predictedProgress = Mathf.Clamp01(predictedProgress);
        bool movedBackToCheckpoint = predictedProgress + 0.02f < _movement.Progress;
        float replicaProgress = forceSnap || movedBackToCheckpoint || snapshot.IsResetting
            ? predictedProgress
            : Mathf.MoveTowards(
                _movement.Progress,
                predictedProgress,
                CalculateProgressFollowRate(snapshot.CurrentSpeed) * Time.deltaTime);
        float replicaOffset = forceSnap || snapshot.IsResetting
            ? snapshot.HorizontalOffset
            : Mathf.Lerp(
                _movement.HorizontalOffset,
                snapshot.HorizontalOffset,
                1f - Mathf.Exp(-_replicaOffsetSmoothing * Time.deltaTime));

        _movement.ApplyNetworkState(
            replicaProgress,
            replicaOffset,
            snapshot.CurrentSpeed,
            snapshot.RouteMovementEnabled,
            snapshot.IsComplete);
        _speedController?.ApplyNetworkSpeed(snapshot.CurrentSpeed);
        _stormLogic?.ApplyNetworkState(snapshot.StormDistance, snapshot.StormState);
        _failController?.ApplyNetworkState(snapshot.IsFailed);
    }

    private float CalculateProgressFollowRate(float currentSpeed)
    {
        float routeProgressRate = _movement != null && _movement.RouteLength > 0f
            ? Mathf.Max(0f, currentSpeed) / _movement.RouteLength
            : 0f;
        return routeProgressRate + _replicaProgressCorrectionRate;
    }

    private void OnServerObstacleHit(SandBoatObstacle _)
    {
        if (IsServer)
        {
            _collisionSequence.Value++;
        }
    }

    private void OnCollisionSequenceChanged(uint previousValue, uint currentValue)
    {
        if (currentValue != previousValue)
        {
            CollisionConfirmedLocally?.Invoke();
        }
    }
}

/// <summary>Continuous server state replicated to every Sand Boat client.</summary>
public struct SandBoatSimulationSnapshot : INetworkSerializable, IEquatable<SandBoatSimulationSnapshot>
{
    public float Progress;
    public float HorizontalOffset;
    public float CurrentSpeed;
    public float StormDistance;
    public SandstormChaseState StormState;
    public bool RouteMovementEnabled;
    public bool IsComplete;
    public bool IsFailed;
    public bool IsResetting;
    public double ServerTime;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Progress);
        serializer.SerializeValue(ref HorizontalOffset);
        serializer.SerializeValue(ref CurrentSpeed);
        serializer.SerializeValue(ref StormDistance);
        serializer.SerializeValue(ref StormState);
        serializer.SerializeValue(ref RouteMovementEnabled);
        serializer.SerializeValue(ref IsComplete);
        serializer.SerializeValue(ref IsFailed);
        serializer.SerializeValue(ref IsResetting);
        serializer.SerializeValue(ref ServerTime);
    }

    public bool Equals(SandBoatSimulationSnapshot other)
    {
        return Progress.Equals(other.Progress)
               && HorizontalOffset.Equals(other.HorizontalOffset)
               && CurrentSpeed.Equals(other.CurrentSpeed)
               && StormDistance.Equals(other.StormDistance)
               && StormState == other.StormState
               && RouteMovementEnabled == other.RouteMovementEnabled
               && IsComplete == other.IsComplete
               && IsFailed == other.IsFailed
               && IsResetting == other.IsResetting
               && ServerTime.Equals(other.ServerTime);
    }
}
