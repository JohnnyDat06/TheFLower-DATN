using Unity.Netcode;
using UnityEngine;

/// <summary>Server-side trigger that admits connected players to the boss encounter.</summary>
[RequireComponent(typeof(Collider))]
public sealed class BossEntryTrigger : MonoBehaviour
{
    private void Reset()
    {
        Collider trigger = GetComponent<Collider>();
        trigger.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        NetworkObject player = other.GetComponentInParent<NetworkObject>();
        if (player == null || !player.IsPlayerObject) return;
        BossEncounterManager.Instance?.RegisterPlayerEntry(player.OwnerClientId);
    }
}
