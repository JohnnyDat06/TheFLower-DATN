using UnityEngine;

/// <summary>Provides Phase 12 standalone debug control for all authored arena FloorTiles.</summary>
public sealed class FloorTileManager : MonoBehaviour
{
    [Tooltip("Tat ca FloorTile cua arena; scene setup tu gan cac tile con cua Arena Floor.")]
    [SerializeField] private FloorTile[] _tiles;

    /// <summary>Returns the authored tile list for the later Phase 13 Shockwave adapter.</summary>
    public FloorTile[] Tiles => _tiles;

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
        if (_tiles == null) return;
        foreach (FloorTile tile in _tiles) tile?.ResetTile();
    }
}
