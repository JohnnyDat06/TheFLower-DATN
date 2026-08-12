using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>Coordinates independent Rune charges for the current boss arena.</summary>
public sealed class RuneManager : MonoBehaviour
{
    [Tooltip("Các Rune thuộc arena; tự tìm các RuneController con nếu để trống.")]
    [SerializeField] private RuneController[] _runes;

    /// <summary>Raised whenever a Rune enters the Charged state.</summary>
    public event Action<RuneController> RuneCharged;

    /// <summary>Stable authored Rune order used by BossNetworkState.</summary>
    public RuneController[] Runes
    {
        get
        {
            RefreshRuneReferences();
            return _runes;
        }
    }

    private void Awake()
    {
        RefreshRuneReferences();
    }

    /// <summary>Charges one Rune after a server-authoritative Shockwave overlap.</summary>
    public bool TryChargeRune(RuneController rune)
    {
        if (rune == null || !IsServerAuthority() || !rune.TryCharge()) return false;

        Debug.Log($"[RuneManager] {rune.name} charged by Shockwave.", rune);
        RuneCharged?.Invoke(rune);
        return true;
    }

    [ContextMenu("Debug/Reset All Runes")]
    private void ResetAllRunesForDebug()
    {
        ResetAllRunesForCycle();
    }

    /// <summary>Resets every Rune after an exposed Core closes without a Core hit.</summary>
    public void ResetAllRunesForCycle()
    {
        RefreshRuneReferences();
        foreach (RuneController rune in _runes) rune?.ResetRune();
    }

    private void RefreshRuneReferences()
    {
        // A deleted Rune leaves a null slot in Unity's serialized array. Rebuild it so
        // the network state always uses only the Runes that still exist in this arena.
        if (_runes == null || _runes.Length == 0 || Array.Exists(_runes, rune => rune == null))
            _runes = GetComponentsInChildren<RuneController>(true);
    }

    private static bool IsServerAuthority() =>
        NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
}
