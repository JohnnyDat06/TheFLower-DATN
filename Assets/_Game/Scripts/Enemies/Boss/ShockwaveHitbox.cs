using System;
using UnityEngine;

/// <summary>Owns the moving Shockwave trigger and straight FloorTile query.</summary>
[RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
public sealed class ShockwaveHitbox : MonoBehaviour
{
    [Tooltip("Trigger collider dung lam vung va cham cua Shockwave.")]
    [SerializeField] private BoxCollider _trigger;
    [Tooltip("Rigidbody kinematic giup trigger nhan va cham on dinh khi Shockwave di chuyen.")]
    [SerializeField] private Rigidbody _rigidbody;
    [Tooltip("Nua be rong cua mot cot Tile thang o giua Shockwave co the bi damage.")]
    [SerializeField, Min(0.1f)] private float _floorLineHalfWidth = 1.25f;
    [Tooltip("Nua do sau wave front dung de khong bo sot tam Tile giua hai frame.")]
    [SerializeField, Min(0.1f)] private float _floorFrontHalfDepth = 1.5f;
    [Tooltip("Khoang cach Shockwave phai di them truoc khi damage Tile tiep theo tren cung mot duong thang.")]
    [SerializeField, Min(0.1f)] private float _minimumFloorDamageStepDistance = 3.5f;
    [Tooltip("Mau cyan cua moving Shockwave; duong telegraph truoc impact dung mau do rieng.")]
    [SerializeField] private Color _movingShockwaveColor = new(0.05f, 0.9f, 1f, 0.95f);

    private FloorTileManager _floorTileManager;
    private Vector3 _lastFloorDamagePosition;
    private bool _hasDamagedFloorTile;
    private bool _damagesFloor = true;

    /// <summary>Raised when a collider first enters the moving Shockwave trigger.</summary>
    public event Action<Collider> TriggerEntered;

    /// <summary>Configures a ground-level trigger matching the visible Shockwave band.</summary>
    public void Configure(float width, float depth, bool damagesFloor = true)
    {
        if (_trigger == null) _trigger = GetComponent<BoxCollider>();
        if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();

        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _trigger.isTrigger = true;
        _trigger.center = new Vector3(0f, 0.35f, 0f);
        _trigger.size = new Vector3(width, 0.8f, depth);
        _damagesFloor = damagesFloor;
        _floorTileManager = FindFirstObjectByType<FloorTileManager>();
    }

    private void Start()
    {
        Transform visual = transform.Find("Visual");
        if (visual == null) return;

        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material material in renderer.materials)
            {
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", _movingShockwaveColor);
                if (material.HasProperty("_Color")) material.SetColor("_Color", _movingShockwaveColor);
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", _movingShockwaveColor * 1.2f);
            }
        }
    }

    private void LateUpdate()
    {
        if (!_damagesFloor || _floorTileManager == null) return;
        if (_hasDamagedFloorTile &&
            Vector3.Distance(transform.position, _lastFloorDamagePosition) < _minimumFloorDamageStepDistance)
            return;

        if (!_floorTileManager.TryDamageNextStraightWaveTile(
            transform.position,
            transform.forward,
            _floorLineHalfWidth,
            _floorFrontHalfDepth))
            return;

        _lastFloorDamagePosition = transform.position;
        _hasDamagedFloorTile = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        TriggerEntered?.Invoke(other);
    }
}
