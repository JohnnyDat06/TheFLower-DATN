using UnityEngine;

/// <summary>
/// Marks a level collider as a Sand Boat collision obstacle. The trigger does not
/// physically block the spline-driven boat; it only reports a single gameplay hit.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandBoatObstacle : MonoBehaviour
{
    [SerializeField] private BoxCollider _obstacleCollider;

    /// <summary>Collider used by the Sand Boat collision handler.</summary>
    public Collider ObstacleCollider => _obstacleCollider;

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
        _obstacleCollider ??= GetComponent<BoxCollider>();
        if (_obstacleCollider == null)
        {
            _obstacleCollider = gameObject.AddComponent<BoxCollider>();
        }

        _obstacleCollider.isTrigger = true;
        FitTriggerToRenderers();
    }

    private void FitTriggerToRenderers()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds worldBounds = renderers[0].bounds;
        for (int rendererIndex = 1; rendererIndex < renderers.Length; rendererIndex++)
        {
            worldBounds.Encapsulate(renderers[rendererIndex].bounds);
        }

        Vector3 lossyScale = transform.lossyScale;
        _obstacleCollider.center = transform.InverseTransformPoint(worldBounds.center);
        _obstacleCollider.size = new Vector3(
            worldBounds.size.x / Mathf.Max(0.001f, Mathf.Abs(lossyScale.x)),
            worldBounds.size.y / Mathf.Max(0.001f, Mathf.Abs(lossyScale.y)),
            worldBounds.size.z / Mathf.Max(0.001f, Mathf.Abs(lossyScale.z)));
    }
}
