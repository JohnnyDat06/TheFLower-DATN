using UnityEngine;

/// <summary>
/// Moves one visible Shockwave band through the boss arena.
/// </summary>
public sealed class ShockwaveController : MonoBehaviour
{
    private const float VisualDepth = 0.6f;

    private Vector3 _travelDirection;
    private Vector3 _spawnPosition;
    private float _speed;
    private float _maxRange;
    private bool _isInitialized;

    /// <summary>Creates a Phase 4 Shockwave prototype at the supplied scene marker.</summary>
    public static ShockwaveController Spawn(
        Transform origin,
        Vector3 direction,
        float speed,
        float width,
        float maxRange)
    {
        if (origin == null || direction.sqrMagnitude < 0.0001f) return null;

        GameObject shockwaveObject = new("CatSphinxShockwave");
        shockwaveObject.transform.SetPositionAndRotation(
            origin.position,
            Quaternion.LookRotation(direction.normalized, Vector3.up));

        ShockwaveController controller = shockwaveObject.AddComponent<ShockwaveController>();
        ShockwaveHitbox hitbox = shockwaveObject.AddComponent<ShockwaveHitbox>();
        shockwaveObject.AddComponent<BossShockwaveDamage>();
        hitbox.Configure(width, VisualDepth);
        controller.CreateVisual(width);
        controller.Initialize(direction, speed, maxRange);
        return controller;
    }

    private void Update()
    {
        if (!_isInitialized) return;

        transform.position += _travelDirection * (_speed * Time.deltaTime);
        if (Vector3.Distance(_spawnPosition, transform.position) >= _maxRange)
            Destroy(gameObject);
    }

    private void Initialize(Vector3 direction, float speed, float maxRange)
    {
        _travelDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        _spawnPosition = transform.position;
        _speed = Mathf.Max(0.1f, speed);
        _maxRange = Mathf.Max(0.1f, maxRange);
        _isInitialized = _travelDirection.sqrMagnitude > 0.0001f;
    }

    private void CreateVisual(float width)
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual";
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        visual.transform.localScale = new Vector3(width, 0.12f, VisualDepth);

        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null) Destroy(visualCollider);

        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = new Color(1f, 0.15f, 0.1f, 0.9f);
    }
}
