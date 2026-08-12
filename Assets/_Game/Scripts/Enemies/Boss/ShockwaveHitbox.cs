using System;
using System.Collections.Generic;
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
    private FloorTileManager _floorTileManager;
    private readonly HashSet<FloorTile> _damagedFloorTiles = new();
    private Vector3 _previousFloorScanPosition;
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
        _floorLineHalfWidth = Mathf.Max(_floorLineHalfWidth, width * 0.5f);
        _damagesFloor = damagesFloor;
        _floorTileManager = FindFirstObjectByType<FloorTileManager>();
        _damagedFloorTiles.Clear();
        _previousFloorScanPosition = transform.position;
    }

    private void LateUpdate()
    {
        if (!_damagesFloor || _floorTileManager == null) return;

        _floorTileManager.DamageStraightWaveSegment(
            _previousFloorScanPosition,
            transform.position,
            transform.forward,
            _floorLineHalfWidth,
            _floorFrontHalfDepth,
            _damagedFloorTiles);
        _previousFloorScanPosition = transform.position;
    }

    private void OnTriggerEnter(Collider other)
    {
        TriggerEntered?.Invoke(other);
    }
}
