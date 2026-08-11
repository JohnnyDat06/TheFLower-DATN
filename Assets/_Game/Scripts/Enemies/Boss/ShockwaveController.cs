using System;
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

    /// <summary>Raised for authoritative Shockwaves so BossNetworkState can mirror their visuals.</summary>
    public static event Action<ShockwaveSpawnInfo> ShockwaveSpawned;

    /// <summary>Creates a Phase 4 Shockwave prototype at the supplied scene marker.</summary>
    public static ShockwaveController Spawn(
        Transform origin,
        Vector3 direction,
        float speed,
        float width,
        float maxRange)
    {
        if (origin == null || direction.sqrMagnitude < 0.0001f) return null;

        return Spawn(origin.position, direction, speed, width, maxRange, true, true);
    }

    /// <summary>Creates a Shockwave at an exact position for authoritative or replicated use.</summary>
    public static ShockwaveController Spawn(
        Vector3 position,
        Vector3 direction,
        float speed,
        float width,
        float maxRange,
        bool enableGameplay,
        bool raiseNetworkEvent,
        bool damagesFloor = true)
    {
        if (direction.sqrMagnitude < 0.0001f) return null;

        GameObject shockwaveObject = new("CatSphinxShockwave");
        shockwaveObject.transform.SetPositionAndRotation(
            position,
            Quaternion.LookRotation(direction.normalized, Vector3.up));

        ShockwaveController controller = shockwaveObject.AddComponent<ShockwaveController>();
        if (enableGameplay)
        {
            ShockwaveHitbox hitbox = shockwaveObject.AddComponent<ShockwaveHitbox>();
            shockwaveObject.AddComponent<BossShockwaveDamage>();
            hitbox.Configure(width, VisualDepth, damagesFloor);
        }

        controller.CreateVisual(width);
        controller.Initialize(direction, speed, maxRange);
        if (raiseNetworkEvent)
        {
            ShockwaveSpawned?.Invoke(new ShockwaveSpawnInfo(
                position,
                direction.normalized,
                speed,
                width,
                maxRange));
        }
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
        if (renderer != null) renderer.material.color = new Color(0.05f, 0.9f, 1f, 0.95f);
    }
}

/// <summary>Exact spawn data sent from the authoritative Shockwave to remote visual replicas.</summary>
public readonly struct ShockwaveSpawnInfo
{
    public ShockwaveSpawnInfo(Vector3 position, Vector3 direction, float speed, float width, float maxRange)
    {
        Position = position;
        Direction = direction;
        Speed = speed;
        Width = width;
        MaxRange = maxRange;
    }

    public Vector3 Position { get; }
    public Vector3 Direction { get; }
    public float Speed { get; }
    public float Width { get; }
    public float MaxRange { get; }
}
