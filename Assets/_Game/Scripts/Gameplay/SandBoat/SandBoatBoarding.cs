using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Seats both players in the Sand Boat through the existing interaction system and starts the chase once ready.
/// A host-only start is available solely for the configured local manual-test workflow.
/// </summary>
public sealed class SandBoatBoarding : InteractableBase
{
    private const ulong NoClientId = ulong.MaxValue;

    [Header("Sand Boat")]
    [SerializeField] private SandBoatMovement _movement;
    [SerializeField] private SandBoatSteering _steering;
    [SerializeField] private SandBoatSpeedController _speedController;
    [SerializeField] private Transform _playerSeatP1;
    [SerializeField] private Transform _playerSeatP2;
    [SerializeField] private Vector3 _seatRotationOffset = new(0f, 180f, 0f);

    [Header("Debug")]
    [SerializeField, Tooltip("Lets a solo host start the chase after boarding for manual testing.")]
    private bool _allowSoloHostDebug = true;

    private readonly NetworkVariable<ulong> _p1ClientId = new(NoClientId);
    private readonly NetworkVariable<ulong> _p2ClientId = new(NoClientId);
    private readonly NetworkVariable<bool> _chaseStarted = new(false);

    /// <summary>True after the boarding condition has been fulfilled and route movement has started.</summary>
    public bool ChaseStarted => _chaseStarted.Value;

    /// <summary>True when the host player is seated in P1's seat.</summary>
    public bool IsP1Seated => _p1ClientId.Value != NoClientId;

    /// <summary>True when the client player is seated in P2's seat.</summary>
    public bool IsP2Seated => _p2ClientId.Value != NoClientId;

    protected override void Awake()
    {
        base.Awake();
        SetChaseControllersEnabled(false);
        _movement?.SetRouteMovementEnabled(false);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _chaseStarted.OnValueChanged += OnChaseStartedChanged;
        ApplyChaseStarted(_chaseStarted.Value);
    }

    public override void OnNetworkDespawn()
    {
        _chaseStarted.OnValueChanged -= OnChaseStartedChanged;
        base.OnNetworkDespawn();
    }

    public override void Interact(ulong playerId)
    {
        if (!CanInteract || _chaseStarted.Value)
        {
            return;
        }

        RequestBoardServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestBoardServerRpc(RpcParams rpcParams = default)
    {
        if (_chaseStarted.Value)
        {
            return;
        }

        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!CanPlayerInteract(clientId) || !TryGetPlayerObject(clientId, out NetworkObject playerObject))
        {
            return;
        }

        bool isP1 = clientId == NetworkManager.ServerClientId;
        if (isP1 ? IsP1Seated : IsP2Seated)
        {
            return;
        }

        Transform seat = isP1 ? _playerSeatP1 : _playerSeatP2;
        if (seat == null)
        {
            Debug.LogError("[SandBoatBoarding] A required player seat is not assigned.", this);
            return;
        }

        SeatPlayer(playerObject, seat);
        if (isP1)
        {
            _p1ClientId.Value = clientId;
        }
        else
        {
            _p2ClientId.Value = clientId;
        }

        SeatPlayerClientRpc(clientId);

        if (CanStartChase())
        {
            StartChase();
        }
    }

    private void SeatPlayer(NetworkObject playerObject, Transform seat)
    {
        Quaternion seatRotation = GetSeatRotation(seat);
        playerObject.transform.SetPositionAndRotation(seat.position, seatRotation);

        if (playerObject.TryGetComponent(out NGOPlayerSync playerSync))
        {
            playerSync.Teleport(seat.position, seatRotation);
        }
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || !IsSpawned || NetworkManager.Singleton == null)
        {
            return;
        }

        FollowLocalSeatedPlayer(_p1ClientId.Value, _playerSeatP1);
        FollowLocalSeatedPlayer(_p2ClientId.Value, _playerSeatP2);
    }

    private void FollowLocalSeatedPlayer(ulong playerClientId, Transform seat)
    {
        if (playerClientId == NoClientId || seat == null)
        {
            return;
        }

        NetworkObject playerObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(playerClientId);
        if (playerObject == null || !playerObject.IsOwner)
        {
            return;
        }

        // NGOPlayerSync/ClientNetworkTransform can publish a world-space pose after the boarding teleport.
        // Reapply the authored seat pose after those updates so the local owner remains visually on the boat.
        playerObject.transform.SetPositionAndRotation(seat.position, GetSeatRotation(seat));

        if (playerObject.TryGetComponent(out PlayerStateMachine stateMachine))
        {
            stateMachine.enabled = false;
        }
    }

    private Quaternion GetSeatRotation(Transform seat)
    {
        return seat.rotation * Quaternion.Euler(_seatRotationOffset);
    }

    private bool CanStartChase()
    {
        if (IsP1Seated && IsP2Seated)
        {
            return true;
        }

        return _allowSoloHostDebug
               && IsP1Seated
               && NetworkManager.Singleton != null
               && NetworkManager.Singleton.ConnectedClientsList.Count == 1;
    }

    private void StartChase()
    {
        _chaseStarted.Value = true;
        ServerActivate();
    }

    private void OnChaseStartedChanged(bool previousValue, bool newValue)
    {
        ApplyChaseStarted(newValue);
    }

    private void ApplyChaseStarted(bool isStarted)
    {
        _movement?.SetRouteMovementEnabled(isStarted);
        SetChaseControllersEnabled(isStarted);
    }

    private void SetChaseControllersEnabled(bool isEnabled)
    {
        if (_steering != null)
        {
            _steering.enabled = isEnabled;
        }

        if (_speedController != null)
        {
            _speedController.enabled = isEnabled;
        }
    }

    [ClientRpc]
    private void SeatPlayerClientRpc(ulong playerClientId)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId != playerClientId)
        {
            return;
        }

        NetworkObject playerObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(playerClientId);
        if (playerObject == null)
        {
            return;
        }

        if (playerObject.TryGetComponent(out PlayerController playerController))
        {
            playerController.SetExternalMovementOverride(true);
        }

        if (playerObject.TryGetComponent(out PlayerStateMachine stateMachine))
        {
            stateMachine.TransitionTo(PlayerStateType.Idle);
            stateMachine.enabled = false;
        }

        if (playerObject.TryGetComponent(out Rigidbody playerRigidbody))
        {
            playerRigidbody.linearVelocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
            playerRigidbody.isKinematic = true;
        }

        if (playerObject.TryGetComponent(out PlayerInteractor playerInteractor))
        {
            playerInteractor.enabled = false;
        }
    }
}
