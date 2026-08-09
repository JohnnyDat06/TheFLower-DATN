using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Applies server-authoritative, one-time player damage for one Shockwave instance.
/// </summary>
[RequireComponent(typeof(ShockwaveHitbox))]
public sealed class BossShockwaveDamage : MonoBehaviour
{
    [Tooltip("Lượng HP mỗi Player nhận từ một Shockwave.")]
    [SerializeField, Min(0.1f)] private float _damage = 20f;

    private readonly HashSet<ulong> _hitPlayerIds = new();
    private ShockwaveHitbox _hitbox;

    private void Awake()
    {
        _hitbox = GetComponent<ShockwaveHitbox>();
    }

    private void OnEnable()
    {
        if (_hitbox == null) _hitbox = GetComponent<ShockwaveHitbox>();
        if (_hitbox != null) _hitbox.TriggerEntered += HandleTriggerEntered;
    }

    private void OnDisable()
    {
        if (_hitbox != null) _hitbox.TriggerEntered -= HandleTriggerEntered;
    }

    private void HandleTriggerEntered(Collider other)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
        if (health == null || !health.IsSpawned || health.IsDead) return;
        if (!_hitPlayerIds.Add(health.OwnerClientId)) return;

        health.TakeDamage(_damage);
        Debug.Log($"[BossShockwaveDamage] Shockwave hit Player {health.OwnerClientId} for {_damage} damage.", this);
    }
}
