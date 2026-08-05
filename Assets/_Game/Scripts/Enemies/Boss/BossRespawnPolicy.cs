using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Boss-room death countdown and wipe coordination.</summary>
public sealed class BossRespawnPolicy : NetworkBehaviour
{
    [SerializeField] private BossEncounterManager _encounter;
    private readonly Dictionary<ulong, Coroutine> _pendingRespawns = new();

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
        foreach (Coroutine routine in _pendingRespawns.Values)
        {
            if (routine != null) StopCoroutine(routine);
        }
        _pendingRespawns.Clear();
        base.OnNetworkDespawn();
    }

    private void HandlePlayerDied(ulong clientId)
    {
        if (!IsServer || _encounter == null || !_encounter.IsActive) return;
        if (CountDeadPlayers() >= 2)
        {
            _encounter.NotifyBothPlayersDeadServer();
            return;
        }

        if (_pendingRespawns.ContainsKey(clientId)) return;
        _pendingRespawns[clientId] = StartCoroutine(AutoRespawnRoutine(clientId));
    }

    private IEnumerator AutoRespawnRoutine(ulong clientId)
    {
        yield return new WaitForSeconds(_encounter.Config != null ? _encounter.Config.AutoRespawnDelay : 10f);

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) &&
            client.PlayerObject != null &&
            client.PlayerObject.TryGetComponent<PlayerHealth>(out var health) && health.IsDead)
        {
            if (_encounter.TryGetRespawnPoint(clientId, out Transform point) &&
                client.PlayerObject.TryGetComponent<NGOPlayerSync>(out var sync))
            {
                sync.Teleport(point.position, point.rotation);
            }

            health.RestoreFullHealth();
            NotifyRespawnedClientRpc(clientId);
        }

        _pendingRespawns.Remove(clientId);
    }

    /// <summary>Validates and immediately revives a dead teammate.</summary>
    public bool TryReviveServer(ulong rescuerId, ulong targetId)
    {
        if (!IsServer || _encounter == null || !_encounter.IsActive || rescuerId == targetId) return false;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(rescuerId, out var rescuer) ||
            !NetworkManager.Singleton.ConnectedClients.TryGetValue(targetId, out var target) ||
            rescuer.PlayerObject == null || target.PlayerObject == null) return false;
        if (!rescuer.PlayerObject.TryGetComponent<PlayerHealth>(out var rescuerHealth) || rescuerHealth.IsDead ||
            !target.PlayerObject.TryGetComponent<PlayerHealth>(out var targetHealth) || !targetHealth.IsDead) return false;

        float maxDistance = _encounter.Config != null ? _encounter.Config.ReviveDistance : 3f;
        if (Vector3.Distance(rescuer.PlayerObject.transform.position, target.PlayerObject.transform.position) > maxDistance)
            return false;

        if (_pendingRespawns.TryGetValue(targetId, out Coroutine pending))
        {
            if (pending != null) StopCoroutine(pending);
            _pendingRespawns.Remove(targetId);
        }

        float healthPercent = _encounter.Config != null ? _encounter.Config.ReviveHealthPercent : 0.6f;
        targetHealth.RestoreHealthPercent(healthPercent);
        NotifyRespawnedClientRpc(targetId);
        return true;
    }

    private int CountDeadPlayers()
    {
        int deadCount = 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null && client.PlayerObject.TryGetComponent<PlayerHealth>(out var health) && health.IsDead)
                deadCount++;
        }
        return deadCount;
    }

    [ClientRpc]
    private void NotifyRespawnedClientRpc(ulong clientId)
    {
        EventBus.RaisePlayerRespawned(clientId, Vector3.zero);
    }
}
