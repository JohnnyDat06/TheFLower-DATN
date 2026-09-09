using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Coordinates independent Rune charges for the current boss arena.</summary>
public sealed class RuneManager : MonoBehaviour
{
    [Tooltip("Các Rune thuộc arena; tự tìm các RuneController con nếu để trống.")]
    [SerializeField] private RuneController[] _runes;
    [Tooltip("Bộ quản lý các ô sàn dùng để tìm vị trí an toàn khi thùng gỗ xuất hiện lại.")]
    [SerializeField] private FloorTileManager _floorTileManager;
    [Tooltip("Khoảng nâng model thùng gỗ lên trên bề mặt ô sàn khi xuất hiện lại.")]
    [SerializeField, Min(0f)] private float _barrelSurfaceOffset = 0.03f;
    [Tooltip("Khoảng cách tối thiểu giữa hai thùng gỗ khi chọn vị trí xuất hiện lại.")]
    [SerializeField, Min(0f)] private float _minimumBarrelSpacing = 2f;

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
        if (_floorTileManager == null) _floorTileManager = GetComponent<FloorTileManager>();
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
        foreach (RuneController rune in _runes) rune?.RestoreInitialPosition();
    }

    /// <summary>Cho một thùng hết thời gian chờ xuất hiện lại trên ô sàn còn an toàn.</summary>
    public void RespawnTimedOutRune(RuneController rune)
    {
        if (rune == null || !IsServerAuthority()) return;
        if (_floorTileManager == null) _floorTileManager = GetComponent<FloorTileManager>();

        if (TryFindSafeSpawnPosition(rune, true, out Vector3 spawnPosition) ||
            TryFindSafeSpawnPosition(rune, false, out spawnPosition))
        {
            rune.ResetRuneAt(spawnPosition);
            return;
        }

        rune.RestoreInitialPosition();
        Debug.LogWarning($"[RuneManager] Không còn ô sàn an toàn; {rune.name} trở về vị trí ban đầu.", rune);
    }

    private void RefreshRuneReferences()
    {
        // A deleted Rune leaves a null slot in Unity's serialized array. Rebuild it so
        // the network state always uses only the Runes that still exist in this arena.
        if (_runes == null || _runes.Length == 0 || Array.Exists(_runes, rune => rune == null))
            _runes = GetComponentsInChildren<RuneController>(true);
    }

    private bool TryFindSafeSpawnPosition(
        RuneController rune,
        bool enforceSpacing,
        out Vector3 spawnPosition)
    {
        spawnPosition = rune.transform.position;
        FloorTile[] tiles = _floorTileManager != null ? _floorTileManager.Tiles : null;
        if (tiles == null || tiles.Length == 0) return false;

        List<FloorTile> candidates = new();
        foreach (FloorTile tile in tiles)
        {
            if (tile == null || !tile.CanHostBossPickup) continue;

            Vector3 candidatePosition = tile.WorldSurfaceCenter + Vector3.up * _barrelSurfaceOffset;
            if (enforceSpacing && IsTooCloseToAnotherRune(rune, candidatePosition)) continue;
            candidates.Add(tile);
        }

        if (candidates.Count == 0) return false;

        FloorTile selectedTile = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        spawnPosition = selectedTile.WorldSurfaceCenter + Vector3.up * _barrelSurfaceOffset;
        return true;
    }

    private bool IsTooCloseToAnotherRune(RuneController rune, Vector3 candidatePosition)
    {
        float minimumSqrDistance = _minimumBarrelSpacing * _minimumBarrelSpacing;
        foreach (RuneController otherRune in _runes)
        {
            if (otherRune == null || otherRune == rune) continue;

            Vector3 offset = Vector3.ProjectOnPlane(otherRune.transform.position - candidatePosition, Vector3.up);
            if (offset.sqrMagnitude < minimumSqrDistance) return true;
        }

        return false;
    }

    private static bool IsServerAuthority() =>
        NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
}
