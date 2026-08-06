using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// PlayerHealth — Quản lý HP player, implement IDamageable.
/// Đồng bộ qua NetworkVariable để host và client đều thấy.
/// SRS §4.1.3
/// </summary>
public class PlayerHealth : NetworkBehaviour, IDamageable
{
    [SerializeField] private SOPlayerConfig _config;
    [SerializeField] private PlayerStateMachine _fsm;
    [SerializeField] private PlayerAnimator _playerAnimator;

    /// <summary>Máu hiện tại được đồng bộ qua mạng.</summary>
    public NetworkVariable<float> NetworkHealth = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Máu hiện tại (tương thích IDamageable).</summary>
    public float CurrentHealth => NetworkHealth.Value;

    /// <summary>Máu tối đa từ SOPlayerConfig.</summary>
    public float MaxHealth => _config != null ? _config.MaxHealth : 100f;

    /// <summary>True nếu đã chết.</summary>
    public bool IsDead => NetworkHealth.Value <= 0f;

    /// <summary>Event cục bộ để HUDController lắng nghe.</summary>
    public event Action<float, float> OnHealthChanged; // (current, max)

    private void Awake()
    {
        if (_config == null)
        {
            Debug.LogError("[PlayerHealth] SOPlayerConfig chưa được gán trong Inspector!");
        }
        if (_fsm == null)
        {
            _fsm = GetComponent<PlayerStateMachine>();
        }
        if (_playerAnimator == null)
        {
            _playerAnimator = GetComponent<PlayerAnimator>();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsServer)
        {
            NetworkHealth.Value = MaxHealth;
        }

        NetworkHealth.OnValueChanged += OnHealthNetworkChanged;

        // Cập nhật UI lần đầu
        OnHealthChanged?.Invoke(NetworkHealth.Value, MaxHealth);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        NetworkHealth.OnValueChanged -= OnHealthNetworkChanged;
    }

    private void OnHealthNetworkChanged(float oldVal, float newVal)
    {
        OnHealthChanged?.Invoke(newVal, MaxHealth);
        
        // Nếu máu giảm (nhưng chưa chết), kích hoạt animation hit trên mọi client
        if (newVal < oldVal && newVal > 0)
        {
            TriggerHitAnimation();
        }
    }

    private void TriggerHitAnimation()
    {
        if (_playerAnimator != null)
        {
            _playerAnimator.TriggerHit();
        }
    }

    /// <summary>Gây sát thương. Chỉ Server mới có quyền thay đổi NetworkVariable.</summary>
    public void TakeDamage(float amount)
    {
        if (!IsServer) return; 
        if (IsDead || amount <= 0f) return;

        NetworkHealth.Value = Mathf.Max(0f, NetworkHealth.Value - amount);

        if (IsDead) HandleDeath();
    }

    /// <summary>Hạ HP về 0 ngay lập tức. Dùng bởi DeathZone.</summary>
    public void InstantKill()
    {
        if (!IsServer)
        {
            if (IsOwner)
            {
                InstantKillServerRpc();
            }
            return;
        }

        ApplyInstantKill();
    }

    [ServerRpc]
    private void InstantKillServerRpc()
    {
        ApplyInstantKill();
    }

    private void ApplyInstantKill()
    {
        if (IsDead) return;
        NetworkHealth.Value = 0f;
        HandleDeath();
    }

    /// <summary>Khôi phục HP tối đa. Gọi bởi RespawnManager sau hồi sinh.</summary>
    public void RestoreFullHealth()
    {
        if (IsServer)
        {
            NetworkHealth.Value = MaxHealth;
        }
        else if (IsOwner)
        {
            RestoreFullHealthServerRpc();
        }
    }

    /// <summary>Restores a server-authoritative amount of health.</summary>
    public void RestoreHealth(float amount)
    {
        if (!IsServer || amount <= 0f || IsDead) return;
        NetworkHealth.Value = Mathf.Min(MaxHealth, NetworkHealth.Value + amount);
    }

    /// <summary>Restores a percentage of MaxHealth on the server.</summary>
    public void RestoreHealthPercent(float percent)
    {
        if (!IsServer || percent <= 0f || IsDead) return;
        NetworkHealth.Value = Mathf.Min(MaxHealth, MaxHealth * Mathf.Clamp01(percent));
    }

    /// <summary>
    /// Returns a dead player to gameplay with a server-authoritative percentage of MaxHealth.
    /// The targeted RPC only changes local presentation/FSM for the owning client.
    /// </summary>
    public void ReviveAtHealthPercent(float percent)
    {
        if (!IsServer || percent <= 0f) return;

        NetworkHealth.Value = Mathf.Max(1f, MaxHealth * Mathf.Clamp01(percent));
        NotifyPlayerRevivedClientRpc(OwnerClientId);
    }

    [ServerRpc]
    private void RestoreFullHealthServerRpc()
    {
        NetworkHealth.Value = MaxHealth;
        Debug.Log($"[PlayerHealth] Server restored health for Player {OwnerClientId} via ServerRpc");
    }

    private void HandleDeath()
    {
        if (!IsServer) return;

        ulong clientId = OwnerClientId;

        // The server is the authority for death-dependent systems such as
        // boss wipe/respawn. Host mode also executes ClientRpc locally, so the
        // client callback below must not publish a second copy on the host.
        EventBus.RaisePlayerDied(clientId);
        
        // Thông báo cho tất cả các client về cái chết này qua mạng
        NotifyPlayerDiedClientRpc(clientId);

#if UNITY_EDITOR || DEBUG_BUILD
        Debug.Log($"[PlayerHealth] Server detected Player {clientId} died.");
#endif
    }

    /// <summary>
    /// Gửi thông báo từ Server xuống tất cả các Client.
    /// Giúp EventBus.OnPlayerDied được kích hoạt đồng bộ trên mọi máy.
    /// </summary>
    [ClientRpc]
    private void NotifyPlayerDiedClientRpc(ulong clientId)
    {
        // Chuyển state máy cục bộ (nếu là owner thì quan trọng nhất)
        if (_fsm != null)
        {
            _fsm.TransitionTo(PlayerStateType.Dead);
        }

        // Dedicated server already published the authoritative event in
        // HandleDeath; clients publish their local presentation event here.
        if (!IsServer)
        {
            EventBus.RaisePlayerDied(clientId);
        }
        
        Debug.Log($"[PlayerHealth] Client {NetworkManager.Singleton.LocalClientId} received death notification for Player {clientId}");
    }

    [ClientRpc]
    private void NotifyPlayerRevivedClientRpc(ulong clientId)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId != clientId) return;

        EventBus.RaisePlayerRespawned(clientId, transform.position);
        if (_fsm != null)
        {
            StartCoroutine(ReturnToIdleAfterRevive());
        }
    }

    private System.Collections.IEnumerator ReturnToIdleAfterRevive()
    {
        _fsm.TransitionTo(PlayerStateType.Respawning);
        yield return new WaitForSeconds(0.5f);
        if (_fsm != null) _fsm.TransitionTo(PlayerStateType.Idle);
    }
}
