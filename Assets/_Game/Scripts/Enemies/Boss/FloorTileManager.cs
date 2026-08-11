using System.Collections.Generic;
using UnityEngine;

/// <summary>Provides Phase 12 standalone debug control for all authored arena FloorTiles.</summary>
public sealed class FloorTileManager : MonoBehaviour
{
    [Tooltip("Tat ca FloorTile cua arena; scene setup tu gan cac tile con cua Arena Floor.")]
    [SerializeField] private FloorTile[] _tiles;

    /// <summary>Returns the authored tile list for the later Phase 13 Shockwave adapter.</summary>
    public FloorTile[] Tiles => _tiles;

    /// <summary>Increases every time all FloorTiles are forcibly restored.</summary>
    public int ResetRevision { get; private set; }

    /// <summary>Damages every not-yet-hit tile crossed between two Shockwave positions.</summary>
    public int DamageStraightWaveSegment(
        Vector3 previousWavePosition,
        Vector3 currentWavePosition,
        Vector3 travelDirection,
        float lineHalfWidth,
        float frontHalfDepth,
        ISet<FloorTile> damagedByThisShockwave)
    {
        if (_tiles == null || travelDirection.sqrMagnitude < 0.0001f) return 0;

        Vector3 forward = Vector3.ProjectOnPlane(travelDirection, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 planarTravel = Vector3.ProjectOnPlane(currentWavePosition - previousWavePosition, Vector3.up);
        float travelledDistance = Mathf.Max(0f, Vector3.Dot(planarTravel, forward));
        int damagedCount = 0;

        foreach (FloorTile tile in _tiles)
        {
            if (tile == null || !tile.gameObject.activeInHierarchy || tile.State == FloorTileState.Fall) continue;
            if (damagedByThisShockwave != null && damagedByThisShockwave.Contains(tile)) continue;

            Vector3 offset = Vector3.ProjectOnPlane(tile.WorldCenter - previousWavePosition, Vector3.up);
            float longitudinalDistance = Vector3.Dot(offset, forward);
            float lateralDistance = Mathf.Abs(Vector3.Dot(offset, right));
            bool crossedByWave = longitudinalDistance >= -frontHalfDepth &&
                                 longitudinalDistance <= travelledDistance + frontHalfDepth &&
                                 lateralDistance <= lineHalfWidth;
            if (!crossedByWave) continue;

            damagedByThisShockwave?.Add(tile);
            if (!tile.TryDamage()) continue;

            damagedCount++;
            Debug.Log($"[FloorTileManager] Straight Shockwave damaged FloorTile {tile.name}.", tile);
        }

        return damagedCount;
    }

    /// <summary>Damages only the nearest eligible tile intersecting one centred straight Shockwave front.</summary>
    public bool TryDamageNextStraightWaveTile(
        Vector3 wavePosition,
        Vector3 travelDirection,
        float lineHalfWidth,
        float frontHalfDepth)
    {
        if (_tiles == null || travelDirection.sqrMagnitude < 0.0001f) return false;

        Vector3 forward = Vector3.ProjectOnPlane(travelDirection, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        FloorTile nearestTile = null;
        float nearestDistance = float.MaxValue;
        foreach (FloorTile tile in _tiles)
        {
            if (tile == null || !tile.gameObject.activeInHierarchy || tile.State == FloorTileState.Fall) continue;

            Vector3 offset = Vector3.ProjectOnPlane(tile.WorldCenter - wavePosition, Vector3.up);
            float lateralDistance = Mathf.Abs(Vector3.Dot(offset, right));
            float forwardDistance = Mathf.Abs(Vector3.Dot(offset, forward));
            if (lateralDistance > lineHalfWidth || forwardDistance > frontHalfDepth) continue;
            if (forwardDistance >= nearestDistance) continue;

            nearestTile = tile;
            nearestDistance = forwardDistance;
        }

        if (nearestTile == null || !nearestTile.TryDamage()) return false;

        Debug.Log($"[FloorTileManager] Straight Shockwave damaged FloorTile {nearestTile.name}.", nearestTile);
        return true;
    }

    /// <summary>Restores every authored arena tile before a new boss phase or a full wipe retry.</summary>
    public void ResetAllTilesForEncounter()
    {
        if (_tiles == null) return;
        foreach (FloorTile tile in _tiles) tile?.ResetTile();
        ResetRevision++;
    }

    [ContextMenu("Debug/Damage First Tile")]
    private void DamageFirstTileForDebug()
    {
        if (_tiles == null || _tiles.Length == 0)
        {
            Debug.LogWarning("[FloorTileManager] No FloorTiles are configured.", this);
            return;
        }

        _tiles[0]?.TryDamage();
    }

    [ContextMenu("Debug/Damage All Tiles")]
    private void DamageAllTilesForDebug()
    {
        if (_tiles == null) return;
        foreach (FloorTile tile in _tiles) tile?.TryDamage();
    }

    [ContextMenu("Debug/Reset All Tiles")]
    private void ResetAllTilesForDebug()
    {
        ResetAllTilesForEncounter();
    }
}
