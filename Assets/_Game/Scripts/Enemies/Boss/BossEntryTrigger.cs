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
        if (NetworkManager.Singleton == null) return;
        NetworkObject player = other.GetComponentInParent<NetworkObject>();
        if (player == null || !player.IsPlayerObject) return;

        // Host physics registers every player it sees. A remote player's trigger may
        // instead be detected only on its owning Client, which must request admission.
        if (!NetworkManager.Singleton.IsServer && !player.IsOwner) return;
        BossEncounterManager.Instance?.RequestPlayerEntry(player.OwnerClientId);
    }
}
