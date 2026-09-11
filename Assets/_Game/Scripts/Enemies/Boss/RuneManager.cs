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
    [Tooltip("Khoảng cách tối thiểu tính từ Boss theo hướng tiến vào arena; các ô ngang hàng hoặc phía sau Boss sẽ không được chọn.")]
    [SerializeField, Min(0f)] private float _minimumDistanceInFrontOfBoss = 1f;

    private SealManager _sealManager;
    private BossArenaReferences _arenaReferences;

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
        _sealManager = GetComponent<SealManager>();
        _arenaReferences = GetComponent<BossArenaReferences>();
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
        if (rune == null || rune.State != RuneState.Charged || !IsServerAuthority()) return;
        if (_floorTileManager == null) _floorTileManager = GetComponent<FloorTileManager>();
        if (_sealManager == null) _sealManager = GetComponent<SealManager>();
        if (_arenaReferences == null) _arenaReferences = GetComponent<BossArenaReferences>();

        _sealManager?.ResetAllSealsForCycle();
        RespawnRunePairOnSafeTiles();
        Debug.Log($"[RuneManager] {rune.name} hết thời gian chờ; Host đã đặt lại cả hai thùng gỗ và hai Seal.", this);
    }

    private void RespawnRunePairOnSafeTiles()
    {
        RefreshRuneReferences();
        List<Vector3> reservedPositions = new();

        foreach (RuneController rune in _runes)
        {
            if (rune == null) continue;

            if (TryFindSafeSpawnPosition(reservedPositions, true, out Vector3 spawnPosition) ||
                TryFindSafeSpawnPosition(reservedPositions, false, out spawnPosition))
            {
                reservedPositions.Add(spawnPosition);
                rune.ResetRuneAt(spawnPosition);
                continue;
            }

            rune.RestoreInitialPosition();
            reservedPositions.Add(rune.transform.position);
            Debug.LogWarning($"[RuneManager] Không còn ô sàn an toàn; {rune.name} trở về vị trí ban đầu.", rune);
        }
    }

    private void RefreshRuneReferences()
    {
        // A deleted Rune leaves a null slot in Unity's serialized array. Rebuild it so
        // the network state always uses only the Runes that still exist in this arena.
        if (_runes == null || _runes.Length == 0 || Array.Exists(_runes, rune => rune == null))
            _runes = GetComponentsInChildren<RuneController>(true);
    }

    private bool TryFindSafeSpawnPosition(
        IReadOnlyList<Vector3> reservedPositions,
        bool enforceSpacing,
        out Vector3 spawnPosition)
    {
        spawnPosition = Vector3.zero;
        FloorTile[] tiles = _floorTileManager != null ? _floorTileManager.Tiles : null;
        if (tiles == null || tiles.Length == 0) return false;

        List<FloorTile> candidates = new();
        foreach (FloorTile tile in tiles)
        {
            if (tile == null || !tile.CanHostBossPickup) continue;

            Vector3 candidatePosition = tile.WorldSurfaceCenter + Vector3.up * _barrelSurfaceOffset;
            if (!IsPositionInFrontOfBoss(candidatePosition)) continue;
            float requiredSpacing = enforceSpacing ? _minimumBarrelSpacing : 0.1f;
            if (IsTooCloseToReservedPosition(reservedPositions, candidatePosition, requiredSpacing)) continue;
            candidates.Add(tile);
        }

        if (candidates.Count == 0) return false;

        FloorTile selectedTile = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        spawnPosition = selectedTile.WorldSurfaceCenter + Vector3.up * _barrelSurfaceOffset;
        return true;
    }

    private bool IsTooCloseToReservedPosition(
        IReadOnlyList<Vector3> reservedPositions,
        Vector3 candidatePosition,
        float requiredSpacing)
    {
        float minimumSqrDistance = requiredSpacing * requiredSpacing;
        foreach (Vector3 reservedPosition in reservedPositions)
        {
            Vector3 offset = Vector3.ProjectOnPlane(reservedPosition - candidatePosition, Vector3.up);
            if (offset.sqrMagnitude < minimumSqrDistance) return true;
        }

        return false;
    }

    private bool IsPositionInFrontOfBoss(Vector3 candidatePosition)
    {
        if (_arenaReferences == null || _arenaReferences.ShockwaveOrigin == null) return false;

        Vector3 arenaForward = Vector3.ProjectOnPlane(_arenaReferences.ShockwaveDirection, Vector3.up).normalized;
        if (arenaForward.sqrMagnitude < 0.0001f) return false;

        Vector3 offsetFromBoss = Vector3.ProjectOnPlane(
            candidatePosition - _arenaReferences.ShockwaveOrigin.position,
            Vector3.up);
        return Vector3.Dot(offsetFromBoss, arenaForward) >= _minimumDistanceInFrontOfBoss;
    }

    private static bool IsServerAuthority() =>
        NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
}
