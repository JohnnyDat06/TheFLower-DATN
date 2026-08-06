using Unity.Netcode;
using UnityEngine;

/// <summary>
/// DeathZone — Khu vực giết chết Player ngay lập tức khi chạm vào.
/// Dùng IDamageableEnemy interface — không biết gì về PlayerHealth cụ thể.
/// SRS §11.3
/// </summary>
public class DeathZone : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        // The server validates lethal overlaps. During scene loading an owner can
        // still have a stale local pose, which must never be allowed to kill the
        // authoritative player before PlayerSpawner finishes its teleport.
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (!other.CompareTag(Constants.Tags.PLAYER)) return;

        if (other.TryGetComponent<IDamageable>(out var damageable))
        {
            damageable.InstantKill();
        }
    }
}
