using UnityEngine;

/// <summary>
/// Marks a level collider as a Sand Boat collision obstacle. The trigger does not
/// physically block the spline-driven boat; it only reports a single gameplay hit.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandBoatObstacle : MonoBehaviour
{
    [SerializeField, Tooltip("MeshCollider có sẵn của chính mesh đá, dùng làm trigger va chạm chính xác cho Sand Boat.")]
    private MeshCollider _obstacleCollider;

    /// <summary>True only for rocks spawned at runtime by the sandstorm attack.</summary>
    public bool IsStormRock { get; private set; }

    /// <summary>Collider used by the Sand Boat collision handler.</summary>
    public Collider ObstacleCollider => _obstacleCollider;

    /// <summary>Marks this runtime obstacle as a storm rock that slows the boat instead of failing the chase.</summary>
    public void MarkAsStormRock()
    {
        IsStormRock = true;
    }

    private void Reset()
    {
        CacheAndConfigureCollider();
    }

    private void Awake()
    {
        CacheAndConfigureCollider();
    }

    private void OnValidate()
    {
        CacheAndConfigureCollider();
    }

    private void CacheAndConfigureCollider()
    {
        _obstacleCollider ??= FindPreferredMeshCollider();
        if (_obstacleCollider == null)
        {
            Debug.LogWarning(
                "[SandBoatObstacle] Rock requires an existing MeshCollider; no fallback collider was added.",
                this);
            return;
        }

        _obstacleCollider.convex = true;
        _obstacleCollider.isTrigger = true;
    }

    private MeshCollider FindPreferredMeshCollider()
    {
        MeshCollider[] meshColliders = GetComponentsInChildren<MeshCollider>(true);
        foreach (MeshCollider meshCollider in meshColliders)
        {
            if (meshCollider.gameObject.name.Contains("LOD0"))
            {
                return meshCollider;
            }
        }

        return meshColliders.Length > 0 ? meshColliders[0] : null;
    }
}
