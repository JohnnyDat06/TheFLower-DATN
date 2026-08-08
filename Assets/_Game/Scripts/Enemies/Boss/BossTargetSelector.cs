using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Selects a valid spawned player for the Cat Sphinx without changing targets mid-telegraph.
/// </summary>
public sealed class BossTargetSelector : MonoBehaviour
{
    private ulong _lastTargetClientId = ulong.MaxValue;

    /// <summary>Returns the next valid player in round-robin order.</summary>
    public bool TrySelectNextTarget(out Transform target)
    {
        target = null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null) return false;

        IReadOnlyList<NetworkClient> connectedClients = networkManager.ConnectedClientsList;
        if (connectedClients.Count == 0) return false;

        int startIndex = FindStartIndex(connectedClients);
        for (int offset = 0; offset < connectedClients.Count; offset++)
        {
            NetworkClient client = connectedClients[(startIndex + offset) % connectedClients.Count];
            if (!IsValidPlayer(client.PlayerObject)) continue;

            _lastTargetClientId = client.ClientId;
            target = client.PlayerObject.transform;
            return true;
        }

        return false;
    }

    private int FindStartIndex(IReadOnlyList<NetworkClient> connectedClients)
    {
        for (int index = 0; index < connectedClients.Count; index++)
        {
            if (connectedClients[index].ClientId == _lastTargetClientId)
                return (index + 1) % connectedClients.Count;
        }

        return 0;
    }

    private static bool IsValidPlayer(NetworkObject playerObject)
    {
        if (playerObject == null || !playerObject.IsSpawned || !playerObject.gameObject.activeInHierarchy)
            return false;

        return !playerObject.TryGetComponent(out PlayerHealth health) || !health.IsDead;
    }
}
