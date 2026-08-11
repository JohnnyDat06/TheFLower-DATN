using System.Collections;
using UnityEngine;

/// <summary>Runs the Phase 3 Earthquake telegraph and impact without changing FloorTile state.</summary>
public sealed class BossEarthquakeAttack : MonoBehaviour
{
    [Tooltip("Thoi gian red telegraph cho Earthquake truoc impact.")]
    [SerializeField, Range(0.8f, 2f)] private float _telegraphDuration = 1.3f;
    [Tooltip("Van toc cua Shockwave toan phong sau khi Earthquake impact.")]
    [SerializeField, Min(0.1f)] private float _shockwaveSpeed = 14f;
    [Tooltip("Be rong toi thieu cua Shockwave Earthquake; gia tri thuc te se mo rong het be ngang arena.")]
    [SerializeField, Min(0.1f)] private float _minimumShockwaveWidth = 20f;
    [Tooltip("Phan le them o hai canh arena de Shockwave Earthquake phu het FloorTile ngoai cung.")]
    [SerializeField, Min(0f)] private float _arenaEdgePadding = 2f;
    private BossAnimationController _animationController;
    private FloorPatternController _floorPatternController;
    private BossArenaReferences _arenaReferences;
    private FloorTileManager _floorTileManager;
    private Coroutine _routine;

    /// <summary>True while Earthquake owns the combat timeline.</summary>
    public bool IsRunning => _routine != null;

    /// <summary>Telegraph duration replicated to remote peers by BossNetworkState.</summary>
    public float TelegraphDuration => _telegraphDuration;

    /// <summary>Starts one Earthquake when no previous instance is running.</summary>
    public bool TryStart()
    {
        if (_routine != null) return false;
        _routine = StartCoroutine(RunAttack());
        return true;
    }

    /// <summary>Cancels the Earthquake telegraph before the server resets the encounter.</summary>
    public void ResetEncounterState()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        _floorPatternController?.ClearAttackTelegraphs();
        _animationController?.ResetPose();
        enabled = true;
    }

    private void Awake()
    {
        _animationController = GetComponent<BossAnimationController>();
        _floorPatternController = GetComponent<FloorPatternController>();
        _arenaReferences = GetComponent<BossArenaReferences>();
        _floorTileManager = GetComponent<FloorTileManager>();
    }

    private IEnumerator RunAttack()
    {
        _floorPatternController?.ShowEarthquakeTelegraph(_telegraphDuration);
        _animationController?.PlayPawSlam();

        float elapsed = 0f;
        while (elapsed < _telegraphDuration)
        {
            elapsed += Time.deltaTime;
            if (_animationController != null && !_animationController.UsesAuthoredPawSlam)
                _animationController.SetTelegraphProgress(elapsed / _telegraphDuration);
            yield return null;
        }

        _animationController?.ResetPose();
        SpawnArenaWideShockwave();
        Debug.Log("[BossEarthquakeAttack] Earthquake impact spawned one arena-wide Shockwave without affecting FloorTiles.", this);
        _routine = null;
    }

    private void SpawnArenaWideShockwave()
    {
        if (_arenaReferences == null) _arenaReferences = GetComponent<BossArenaReferences>();
        if (_floorTileManager == null) _floorTileManager = GetComponent<FloorTileManager>();
        if (_arenaReferences == null || _arenaReferences.ShockwaveOrigin == null) return;

        Vector3 direction = _arenaReferences.ShockwaveDirection;
        if (direction.sqrMagnitude < 0.0001f) return;

        Vector3 origin = _arenaReferences.ShockwaveOrigin.position;
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        float leftMost = 0f;
        float rightMost = 0f;
        float farthestDistance = 0f;
        FloorTile[] tiles = _floorTileManager != null ? _floorTileManager.Tiles : null;
        if (tiles != null)
        {
            foreach (FloorTile tile in tiles)
            {
                if (tile == null) continue;

                Vector3 offset = Vector3.ProjectOnPlane(tile.WorldCenter - origin, Vector3.up);
                float lateralDistance = Vector3.Dot(offset, right);
                leftMost = Mathf.Min(leftMost, lateralDistance);
                rightMost = Mathf.Max(rightMost, lateralDistance);
                farthestDistance = Mathf.Max(farthestDistance, Vector3.Dot(offset, direction));
            }
        }

        float width = Mathf.Max(_minimumShockwaveWidth, rightMost - leftMost + _arenaEdgePadding * 2f);
        float range = Mathf.Max(1f, farthestDistance + _arenaEdgePadding);
        ShockwaveController.Spawn(
            origin,
            direction,
            _shockwaveSpeed,
            width,
            range,
            true,
            true,
            false);
    }
}
