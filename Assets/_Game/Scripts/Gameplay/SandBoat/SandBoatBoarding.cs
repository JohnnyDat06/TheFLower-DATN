using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Seats both players in the Sand Boat through the existing interaction system and starts the chase once ready.
/// A host-only start is available solely for the configured local manual-test workflow.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class SandBoatBoarding : InteractableBase
{
    private const ulong NoClientId = ulong.MaxValue;

    [Header("Sand Boat")]
    [SerializeField, Tooltip("Movement của Sand Boat được bật sau khi đủ điều kiện boarding.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Controller lái dành riêng cho P1 và chỉ được mô phỏng trên server.")]
    private SandBoatSteering _steering;
    [SerializeField, Tooltip("Controller tốc độ dành riêng cho P2 và chỉ được mô phỏng trên server.")]
    private SandBoatSpeedController _speedController;
    [SerializeField, Tooltip("Vị trí ngồi của P1/Host trên thuyền.")]
    private Transform _playerSeatP1;
    [SerializeField, Tooltip("Vị trí ngồi của P2/Client trên thuyền.")]
    private Transform _playerSeatP2;
    [SerializeField, Tooltip("Góc xoay cộng thêm để nhân vật nhìn đúng về phía mũi thuyền khi ngồi.")]
    private Vector3 _seatRotationOffset = new(0f, 180f, 0f);

    [Header("Debug")]
    [SerializeField, Tooltip("Cho phép host chơi một mình bắt đầu chase sau khi lên thuyền để manual test.")]
    private bool _allowSoloHostDebug = true;

    private readonly NetworkVariable<ulong> _p1ClientId = new(NoClientId);
    private readonly NetworkVariable<ulong> _p2ClientId = new(NoClientId);
    private readonly NetworkVariable<bool> _chaseStarted = new(false);
    private readonly NetworkVariable<bool> _chaseFinalized = new(false);
    private Coroutine _releasePlayersRoutine;

    /// <summary>True after the boarding condition has been fulfilled and route movement has started.</summary>
    public bool ChaseStarted => _chaseStarted.Value;

    /// <summary>True after Phase 19 has permanently released players from the completed chase.</summary>
    public bool ChaseFinalized => _chaseFinalized.Value;

    /// <summary>True when the host player is seated in P1's seat.</summary>
    public bool IsP1Seated => _p1ClientId.Value != NoClientId;

    /// <summary>True when the client player is seated in P2's seat.</summary>
    public bool IsP2Seated => _p2ClientId.Value != NoClientId;

    /// <summary>Network client ID authoritative đang giữ role P2.</summary>
    public ulong P2ClientId => _p2ClientId.Value;

    /// <summary>True khi máy hiện tại chính là client đã được server gán role P2.</summary>
    public bool IsLocalClientP2 => NetworkManager.Singleton != null
                                   && _p2ClientId.Value != NoClientId
                                   && NetworkManager.Singleton.LocalClientId == _p2ClientId.Value;

    /// <summary>
    /// Raised locally on every peer after a checkpoint retry has restored the chase.
    /// UI listeners can replay local-only presentation without introducing another network state.
    /// </summary>
    public event Action ChaseRestartedLocally;

    protected override void Awake()
    {
        base.Awake();
        SetChaseControllersEnabled(false);
        _movement?.SetRouteMovementEnabled(false);
    }

    private void OnEnable()
    {
        // NetworkTransform/NetworkRigidbody may apply a received world pose after
        // LateUpdate. Re-apply the local seated player's visual pose immediately
        // before rendering so a client never shows itself snapping behind the boat.
        Application.onBeforeRender += ApplySeatedPlayerVisualsBeforeRender;
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= ApplySeatedPlayerVisualsBeforeRender;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _chaseStarted.OnValueChanged += OnChaseStartedChanged;
        _chaseFinalized.OnValueChanged += OnChaseFinalizedChanged;
        ApplyChaseStarted(_chaseStarted.Value);
    }

    public override void OnNetworkDespawn()
    {
        _chaseStarted.OnValueChanged -= OnChaseStartedChanged;
        _chaseFinalized.OnValueChanged -= OnChaseFinalizedChanged;
        base.OnNetworkDespawn();
    }

    public override void Interact(ulong playerId)
    {
        if (!CanInteract || _chaseStarted.Value || _chaseFinalized.Value)
        {
            return;
        }

        RequestBoardServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestBoardServerRpc(RpcParams rpcParams = default)
    {
        if (_chaseStarted.Value || _chaseFinalized.Value)
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
        ApplySeatPose(playerObject, seat);
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying
            || !IsSpawned
            || NetworkManager.Singleton == null
            || !_chaseStarted.Value
            || _chaseFinalized.Value)
        {
            return;
        }

        FollowSeatedPlayerReplica(_p1ClientId.Value, _playerSeatP1);
        FollowSeatedPlayerReplica(_p2ClientId.Value, _playerSeatP2);
    }

    private void ApplySeatedPlayerVisualsBeforeRender()
    {
        if (!Application.isPlaying
            || !IsSpawned
            || NetworkManager.Singleton == null
            || IsServer
            || !_chaseStarted.Value
            || _chaseFinalized.Value)
        {
            return;
        }

        // Cáº£ P1/Host vÃ  P2/Client Ä‘á»u pháº£i báº¡m Ä‘Ãºng gháº¿ trÃªn chiáº¿c thuyá»n replica
        // cá»§a client. Báº£n sao P1 lÃ  remote object nÃªn NetworkTransform cÃ³ thá»ƒ Ä‘áº¿n muá»™n
        // hÆ¡n pose thuyá»n; chÃ»m pose render-only nÃ y ngÄƒn nÃ³ giÃ¢t lá»n trÃªn mÃ n Client
        // mÃ  khÃ´ng thay Ä‘á»•i authority hoáº·c giá»¯a state máº¡ng.
        ApplySeatVisualForClient(_p1ClientId.Value, _playerSeatP1);
        ApplySeatVisualForClient(_p2ClientId.Value, _playerSeatP2);
    }

    private void ApplySeatVisualForClient(ulong playerClientId, Transform seat)
    {
        if (playerClientId == NoClientId || seat == null)
        {
            return;
        }

        NetworkObject playerObject = FindSpawnedPlayerObject(playerClientId);
        if (playerObject != null)
        {
            ApplySeatVisualPose(playerObject, seat);
        }
    }

    private static NetworkObject FindSpawnedPlayerObject(ulong playerClientId)
    {
        foreach (NetworkObject networkObject in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
        {
            if (networkObject.IsSpawned
                && networkObject.IsPlayerObject
                && networkObject.OwnerClientId == playerClientId)
            {
                return networkObject;
            }
        }

        return null;
    }

    private void FollowSeatedPlayerReplica(ulong playerClientId, Transform seat)
    {
        if (playerClientId == NoClientId || seat == null)
        {
            return;
        }

        // NGO chỉ cho client truy vấn PlayerObject của chính nó; server mới có
        // quyền truy vấn và cố định pose cho cả hai bản sao người chơi.
        if (!IsServer && NetworkManager.Singleton.LocalClientId != playerClientId)
        {
            return;
        }

        NetworkObject playerObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(playerClientId);
        if (playerObject == null || (!playerObject.IsOwner && !IsServer))
        {
            return;
        }

        // Chạy sau pose thuyền và NetworkTransform để local owner lẫn bản sao
        // remote trên server luôn dùng đúng seat của thuyền authoritative.
        ApplySeatPose(playerObject, seat);

        if (!playerObject.IsOwner)
        {
            return;
        }

        if (playerObject.TryGetComponent(out PlayerStateMachine stateMachine))
        {
            stateMachine.enabled = false;
        }

        if (playerObject.TryGetComponent(out PlayerAnimator playerAnimator))
        {
            playerAnimator.SetExternalAnimationOverride(true);
        }
    }

    private Quaternion GetSeatRotation(Transform seat)
    {
        return seat.rotation * Quaternion.Euler(_seatRotationOffset);
    }

    private void ApplySeatPose(NetworkObject playerObject, Transform seat)
    {
        if (playerObject == null || seat == null)
        {
            return;
        }

        Quaternion seatRotation = GetSeatRotation(seat);
        if (playerObject.TryGetComponent(out Rigidbody playerRigidbody))
        {
            playerRigidbody.position = seat.position;
            playerRigidbody.rotation = seatRotation;
        }

        playerObject.transform.SetPositionAndRotation(seat.position, seatRotation);
    }

    private void ApplySeatVisualPose(NetworkObject playerObject, Transform seat)
    {
        if (playerObject == null || seat == null)
        {
            return;
        }

        playerObject.transform.SetPositionAndRotation(seat.position, GetSeatRotation(seat));
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

    /// <summary>
    /// Server-only retry handoff used by Phase 13 after the boat state has been reset.
    /// Seated players remain in their assigned roles and are teleported back to their seats.
    /// </summary>
    public void ResumeChaseAfterReset()
    {
        if (!IsServer)
        {
            return;
        }

        _chaseFinalized.Value = false;
        _chaseStarted.Value = true;
        ReseatPlayer(_p1ClientId.Value, _playerSeatP1);
        ReseatPlayer(_p2ClientId.Value, _playerSeatP2);
        ApplyChaseStarted(true);
        ResumeChaseAfterResetClientRpc();
    }

    private void ReseatPlayer(ulong clientId, Transform seat)
    {
        if (clientId == NoClientId || seat == null || !TryGetPlayerObject(clientId, out NetworkObject playerObject))
        {
            return;
        }

        SeatPlayer(playerObject, seat);
        SeatPlayerClientRpc(clientId);
    }

    private void OnChaseStartedChanged(bool previousValue, bool newValue)
    {
        ApplyChaseStarted(newValue);
    }

    private void OnChaseFinalizedChanged(bool previousValue, bool newValue)
    {
        ApplyChaseStarted(_chaseStarted.Value);
    }

    private void ApplyChaseStarted(bool isStarted)
    {
        _movement?.SetRouteMovementEnabled(isStarted);
        bool canSimulateChase = NetworkManager.Singleton == null
                                || !IsSpawned
                                || IsServer;
        SetChaseControllersEnabled(isStarted && canSimulateChase);
        SetInteractable(!isStarted && !_chaseFinalized.Value);
    }

    /// <summary>
    /// Server-only Phase 19 handoff. It stops chase input but deliberately
    /// leaves players at their current seats on the stationary boat, then
    /// restores their normal controls so they can jump out themselves.
    /// </summary>
    public void EndChaseAndReleasePlayersOnBoat()
    {
        if (!IsServer || _chaseFinalized.Value)
        {
            return;
        }

        _chaseFinalized.Value = true;
        _chaseStarted.Value = false;
        ApplyChaseStarted(false);

        if (_releasePlayersRoutine != null)
        {
            StopCoroutine(_releasePlayersRoutine);
        }

        _releasePlayersRoutine = StartCoroutine(ReleasePlayersOnBoatRoutine());
    }

    private IEnumerator ReleasePlayersOnBoatRoutine()
    {
        // Client-owned NetworkTransform keeps an old world pose while the chase
        // is locked. Confirm the final seat teleport on each owner before physics
        // and normal movement are restored, otherwise P2 can snap onto terrain.
        yield return TeleportPlayerToSeatForRelease(_p1ClientId.Value, _playerSeatP1);
        yield return TeleportPlayerToSeatForRelease(_p2ClientId.Value, _playerSeatP2);

        RestorePlayerControlClientRpc(_p1ClientId.Value);
        RestorePlayerControlClientRpc(_p2ClientId.Value);
        _releasePlayersRoutine = null;
    }

    private IEnumerator TeleportPlayerToSeatForRelease(ulong clientId, Transform seat)
    {
        if (clientId == NoClientId || seat == null || !TryGetPlayerObject(clientId, out NetworkObject playerObject))
        {
            yield break;
        }

        Quaternion seatRotation = GetSeatRotation(seat);
        if (playerObject.TryGetComponent(out NGOPlayerSync playerSync))
        {
            bool teleportConfirmed = false;
            yield return playerSync.TeleportAndWaitForOwner(
                seat.position,
                seatRotation,
                confirmed => teleportConfirmed = confirmed);

            if (teleportConfirmed)
            {
                yield break;
            }
        }

        // Fallback for a misconfigured Player prefab. This preserves the server
        // replica at the seat even when NGOPlayerSync is unavailable.
        ApplySeatPose(playerObject, seat);
    }

    [ClientRpc]
    private void ResumeChaseAfterResetClientRpc()
    {
        ApplyChaseStarted(true);
        ChaseRestartedLocally?.Invoke();
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

        if (playerObject.TryGetComponent(out NGOPlayerSync playerSync))
        {
            playerSync.SetExternalSimulationOverride(true);
        }

        Transform seat = playerClientId == NetworkManager.ServerClientId
            ? _playerSeatP1
            : _playerSeatP2;
        ApplySeatPose(playerObject, seat);

        if (playerObject.TryGetComponent(out PlayerController playerController))
        {
            playerController.SetExternalMovementOverride(true);
        }

        if (playerObject.TryGetComponent(out PlayerStateMachine stateMachine))
        {
            stateMachine.TransitionTo(PlayerStateType.Idle);
            stateMachine.enabled = false;
        }

        if (playerObject.TryGetComponent(out PlayerAnimator playerAnimator))
        {
            playerAnimator.SetExternalAnimationOverride(true);
        }

        if (playerObject.TryGetComponent(out PlayerInteractor playerInteractor))
        {
            playerInteractor.ClearCurrentTarget();
            playerInteractor.enabled = false;
        }
    }

    [ClientRpc]
    private void RestorePlayerControlClientRpc(ulong playerClientId)
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
            playerController.SetExternalMovementOverride(false);
        }

        if (playerObject.TryGetComponent(out PlayerStateMachine stateMachine))
        {
            stateMachine.enabled = true;
            stateMachine.TransitionTo(PlayerStateType.Idle);
        }

        if (playerObject.TryGetComponent(out PlayerAnimator playerAnimator))
        {
            playerAnimator.SetExternalAnimationOverride(false);
        }

        if (playerObject.TryGetComponent(out PlayerInteractor playerInteractor))
        {
            playerInteractor.enabled = true;
        }

        if (playerObject.TryGetComponent(out NGOPlayerSync playerSync))
        {
            playerSync.SetExternalSimulationOverride(false);
        }
    }
}
