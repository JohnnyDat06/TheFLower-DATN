using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>Coordinates individual Seal activation without applying the later dual-seal stun rule.</summary>
public sealed class SealManager : MonoBehaviour
{
    [Tooltip("Các Seal thuộc arena; tự tìm các SealController con nếu để trống.")]
    [SerializeField] private SealController[] _seals;

    /// <summary>Raised when one valid Rune-to-Seal interaction succeeds.</summary>
    public event Action<SealController> SealActivated;

    /// <summary>True only when every configured Seal is currently Active.</summary>
    public bool AreAllSealsActive
    {
        get
        {
            RefreshSealReferences();
            return _seals.Length > 0 && Array.TrueForAll(_seals, seal => seal != null && seal.IsActivated);
        }
    }

    private void Awake()
    {
        RefreshSealReferences();
    }

    private void Update()
    {
        if (!IsServerAuthority()) return;
        foreach (SealController seal in _seals) seal?.RefreshReadiness();
    }

    /// <summary>Validates and activates exactly one Ready Seal for the interacting player.</summary>
    public bool TryActivateSeal(SealController seal, ulong playerId)
    {
        if (!IsServerAuthority() || seal == null || !seal.CanInteract || !IsPlayerInRange(playerId, seal.transform)) return false;
        if (!seal.TryActivate()) return false;

        Debug.Log($"[SealManager] Player {playerId} activated {seal.name}.", seal);
        SealActivated?.Invoke(seal);
        return true;
    }

    [ContextMenu("Debug/Reset All Seals")]
    private void ResetAllSealsForDebug()
    {
        ResetAllSealsForCycle();
    }

    /// <summary>Returns all Seals to Inactive after the Phase 9 Core exposure window expires.</summary>
    public void ResetAllSealsForCycle()
    {
        RefreshSealReferences();
        foreach (SealController seal in _seals) seal?.ResetSealForCycle();
    }

    private void RefreshSealReferences()
    {
        if (_seals == null || _seals.Length == 0)
            _seals = GetComponentsInChildren<SealController>(true);
    }

    private static bool IsPlayerInRange(ulong playerId, Transform sealTransform)
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out NetworkClient client) ||
            client.PlayerObject == null)
            return false;

        return Vector3.Distance(client.PlayerObject.transform.position, sealTransform.position) <= 3f;
    }

    private static bool IsServerAuthority() =>
        NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
}
