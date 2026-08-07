using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Validates boss-room revive input, death countdowns and wipe coordination on the server.</summary>
public sealed class BossRespawnPolicy : NetworkBehaviour
{
    private const ulong NoClient = ulong.MaxValue;

    [SerializeField] private BossEncounterManager _encounter;
    private readonly Dictionary<ulong, Coroutine> _pendingRespawns = new();
    private Coroutine _reviveRoutine;

    private readonly NetworkVariable<ulong> _countdownTarget = new(NoClient);
    private readonly NetworkVariable<float> _countdownRemaining = new(0f);
    private readonly NetworkVariable<ulong> _reviver = new(NoClient);
    private readonly NetworkVariable<ulong> _reviveTarget = new(NoClient);
    private readonly NetworkVariable<float> _reviveProgress = new(0f);

    public ulong CountdownTarget => _countdownTarget.Value;
    public float CountdownRemaining => _countdownRemaining.Value;
    public ulong Reviver => _reviver.Value;
    public ulong ReviveTarget => _reviveTarget.Value;
    public float ReviveProgress => _reviveProgress.Value;

    private void Awake()
    {
        if (_encounter == null) _encounter = GetComponent<BossEncounterManager>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer) EventBus.OnPlayerDied += HandlePlayerDied;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) EventBus.OnPlayerDied -= HandlePlayerDied;
        if (IsServer) CancelAllServerRoutines();
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsClient || !IsSpawned || _encounter == null || !_encounter.IsActive) return;
        MonitorLocalReviveInput();
    }

    private void HandlePlayerDied(ulong clientId)
    {
        if (!IsServer || _encounter == null || !_encounter.IsActive) return;
        if (CountDeadPlayers() >= 2)
        {
            CancelAllServerRoutines();
            _encounter.NotifyBothPlayersDeadServer();
            return;
        }

        if (_pendingRespawns.ContainsKey(clientId)) return;
        _pendingRespawns[clientId] = StartCoroutine(AutoRespawnRoutine(clientId));
    }

    private IEnumerator AutoRespawnRoutine(ulong clientId)
    {
        _countdownTarget.Value = clientId;
        float remaining = _encounter.Config != null ? _encounter.Config.AutoRespawnDelay : 10f;
        while (remaining > 0f)
        {
            _countdownRemaining.Value = remaining;
            yield return new WaitForSeconds(0.1f);
            remaining -= 0.1f;
        }

        if (TryGetPlayerHealth(clientId, out NetworkObject playerObject, out PlayerHealth health) && health.IsDead)
        {
            bool teleportConfirmed = true;
            if (_encounter.TryGetRespawnPoint(clientId, out Transform point) &&
                playerObject.TryGetComponent<NGOPlayerSync>(out var sync))
            {
                yield return sync.TeleportAndConfirmWithRetry(
                    point.position,
                    point.rotation,
                    confirmed => teleportConfirmed = confirmed);
            }
            else
            {
                teleportConfirmed = false;
                Debug.LogError($"[BossRespawnPolicy] Auto-respawn has no valid teleport target for owner {clientId}.");
            }

            if (!teleportConfirmed)
            {
                Debug.LogError($"[BossRespawnPolicy] Auto-respawn aborted for owner {clientId}; teleport was not confirmed.");
                _pendingRespawns.Remove(clientId);
                ClearCountdownServer(clientId);
                yield break;
            }

            health.ReviveAtHealthPercent(1f);
        }

        _pendingRespawns.Remove(clientId);
        ClearCountdownServer(clientId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void StartReviveServerRpc(ulong targetId, ServerRpcParams rpcParams = default)
    {
        ulong rescuerId = rpcParams.Receive.SenderClientId;
        if (!CanStartReviveServer(rescuerId, targetId)) return;

        CancelReviveServer();
        _reviver.Value = rescuerId;
        _reviveTarget.Value = targetId;
        _reviveProgress.Value = 0f;
        _reviveRoutine = StartCoroutine(ReviveRoutine(rescuerId, targetId));
    }

    [ServerRpc(RequireOwnership = false)]
    public void CancelReviveServerRpc(ServerRpcParams rpcParams = default)
    {
        if (_reviver.Value == rpcParams.Receive.SenderClientId) CancelReviveServer();
    }

    private IEnumerator ReviveRoutine(ulong rescuerId, ulong targetId)
    {
        float duration = _encounter.Config != null ? _encounter.Config.ReviveHoldDuration : 5f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (!CanStartReviveServer(rescuerId, targetId))
            {
                CancelReviveServer();
                yield break;
            }

            elapsed += Time.deltaTime;
            _reviveProgress.Value = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }

        if (TryGetPlayerHealth(targetId, out _, out PlayerHealth targetHealth))
        {
            CancelPendingRespawn(targetId);
            ClearCountdownServer(targetId);
            float percent = _encounter.Config != null ? _encounter.Config.ReviveHealthPercent : 0.6f;
            targetHealth.ReviveAtHealthPercent(percent);
        }
        CancelReviveServer();
    }

    private bool CanStartReviveServer(ulong rescuerId, ulong targetId)
    {
        if (!IsServer || _encounter == null || !_encounter.IsActive || rescuerId == targetId) return false;
        if (!TryGetPlayerHealth(rescuerId, out NetworkObject rescuerObject, out PlayerHealth rescuerHealth) || rescuerHealth.IsDead) return false;
        if (!TryGetPlayerHealth(targetId, out NetworkObject targetObject, out PlayerHealth targetHealth) || !targetHealth.IsDead) return false;

        float maxDistance = _encounter.Config != null ? _encounter.Config.ReviveDistance : 3f;
        return Vector3.Distance(rescuerObject.transform.position, targetObject.transform.position) <= maxDistance;
    }

    private void MonitorLocalReviveInput()
    {
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject == null) return;
        NetworkObject localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (!localPlayer.TryGetComponent<PlayerInputHandler>(out var input) ||
            !localPlayer.TryGetComponent<PlayerHealth>(out var localHealth) || localHealth.IsDead) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (_reviver.Value == localId && !input.InteractHeld)
        {
            CancelReviveServerRpc();
            return;
        }

        if (!input.InteractPressed || _reviver.Value != NoClient) return;
        if (TryFindNearbyDeadTeammate(localPlayer.transform.position, localId, out ulong targetId))
            StartReviveServerRpc(targetId);
    }

    public bool TryGetLocalReviveCandidate(out ulong targetId)
    {
        targetId = NoClient;
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject == null) return false;
        return TryFindNearbyDeadTeammate(NetworkManager.Singleton.LocalClient.PlayerObject.transform.position,
            NetworkManager.Singleton.LocalClientId, out targetId);
    }

    private bool TryFindNearbyDeadTeammate(Vector3 origin, ulong localId, out ulong targetId)
    {
        targetId = NoClient;
        float maxDistance = _encounter.Config != null ? _encounter.Config.ReviveDistance : 3f;
        foreach (PlayerHealth health in Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (!health.IsSpawned || health.OwnerClientId == localId || !health.IsDead) continue;
            if (Vector3.Distance(origin, health.transform.position) > maxDistance) continue;
            targetId = health.OwnerClientId;
            return true;
        }
        return false;
    }

    private bool TryGetPlayerHealth(ulong clientId, out NetworkObject playerObject, out PlayerHealth health)
    {
        playerObject = null;
        health = null;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
            client.PlayerObject == null || !client.PlayerObject.TryGetComponent(out health)) return false;
        playerObject = client.PlayerObject;
        return true;
    }

    private int CountDeadPlayers()
    {
        int deadCount = 0;
        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            if (client.PlayerObject != null && client.PlayerObject.TryGetComponent<PlayerHealth>(out var health) && health.IsDead) deadCount++;
        return deadCount;
    }

    private void CancelPendingRespawn(ulong clientId)
    {
        if (!_pendingRespawns.TryGetValue(clientId, out Coroutine routine)) return;
        if (routine != null) StopCoroutine(routine);
        _pendingRespawns.Remove(clientId);
    }

    private void ClearCountdownServer(ulong clientId)
    {
        if (_countdownTarget.Value != clientId) return;
        _countdownTarget.Value = NoClient;
        _countdownRemaining.Value = 0f;
    }

    private void CancelReviveServer()
    {
        if (_reviveRoutine != null) StopCoroutine(_reviveRoutine);
        _reviveRoutine = null;
        _reviver.Value = NoClient;
        _reviveTarget.Value = NoClient;
        _reviveProgress.Value = 0f;
    }

    private void CancelAllServerRoutines()
    {
        if (!IsServer) return;
        foreach (Coroutine routine in _pendingRespawns.Values) if (routine != null) StopCoroutine(routine);
        _pendingRespawns.Clear();
        CancelReviveServer();
        _countdownTarget.Value = NoClient;
        _countdownRemaining.Value = 0f;
    }
}
