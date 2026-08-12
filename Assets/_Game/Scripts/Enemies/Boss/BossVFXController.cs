using UnityEngine;

/// <summary>
/// Spawns local presentation-only boss VFX from already synchronized combat visual events.
/// It does not create damage, collisions, gameplay state or network messages.
/// </summary>
public sealed class BossVFXController : MonoBehaviour
{
    [Tooltip("Prefab bui cat spawn tai diem bat dau cua moi Shockwave cua Cat Sphinx.")]
    [SerializeField] private BossDustShockwaveVFX _dustShockwavePrefab;
    [Tooltip("Do nang VFX len khoi mat san de tranh bi san da che khuất.")]
    [SerializeField, Range(-0.05f, 0.1f)] private float _dustShockwaveGroundOffset = 0.01f;

    private void OnEnable()
    {
        ShockwaveController.ShockwaveVisualSpawned += HandleShockwaveVisualSpawned;
    }

    private void OnDisable()
    {
        ShockwaveController.ShockwaveVisualSpawned -= HandleShockwaveVisualSpawned;
    }

    private void HandleShockwaveVisualSpawned(ShockwaveSpawnInfo spawnInfo)
    {
        if (_dustShockwavePrefab == null) return;

        Vector3 spawnPosition = spawnInfo.Position + Vector3.up * _dustShockwaveGroundOffset;
        Quaternion spawnRotation = spawnInfo.Direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(spawnInfo.Direction, Vector3.up)
            : Quaternion.identity;
        BossDustShockwaveVFX shockwaveVfx = Instantiate(_dustShockwavePrefab, spawnPosition, spawnRotation);
        shockwaveVfx.ConfigureTravel(
            spawnInfo.Direction,
            spawnInfo.Speed,
            spawnInfo.Width,
            spawnInfo.MaxRange);
    }
}
