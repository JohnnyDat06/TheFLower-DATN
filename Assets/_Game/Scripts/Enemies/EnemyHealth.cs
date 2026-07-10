using System;
using Unity.Netcode;
using UnityEngine;
using System.Collections;

/// <summary>
/// Lớp cơ sở (Base Class) chung cho mọi Enemy trong game.
/// Quản lý thanh máu đồng bộ qua mạng (NGO).
/// Áp dụng mẫu thiết kế Template Method: class con tự kiểm soát trạng thái nhưng Health thì base quản lý.
/// </summary>
public abstract class EnemyHealth : NetworkBehaviour, IDamageableEnemy
{
    [Header("Enemy Stats")]
    [SerializeField] protected int _maxHealth = 100;
    public int MaxHealth => _maxHealth;

    [Header("UI References")]
    [SerializeField] protected GameObject _healthBarUI;

    [Header("Death Fall Settings")]
    [SerializeField] private float _gravity = 9.81f;
    [SerializeField] private LayerMask _groundLayer;

    [Header("Audio Settings")]
    [SerializeField] protected SOAudioClip _hurtSFX;
    [SerializeField] protected SOAudioClip _deathSFX;

    // Biến đồng bộ qua mạng. Server có quyền Ghi, mọi Client đều tự động Đọc (Sync).
    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(
        100, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    // Sự kiện nội bộ trên client để UI thanh máu giật/đỏ lên hoặc AI phản ứng cục bộ
    public event Action<int, int> OnHealthChanged; // (oldValue, newValue)
    public event Action OnDeath;

    protected bool _isDead = false;

    public override void OnNetworkSpawn()
    {
        // Khởi tạo máu ban đầu (Chỉ con chủ - Server mới được gán giá trị khởi điểm)
        if (IsServer)
        {
            CurrentHealth.Value = _maxHealth;
        }

        CurrentHealth.OnValueChanged += HandleHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        CurrentHealth.OnValueChanged -= HandleHealthChanged;
    }

    private void HandleHealthChanged(int oldVal, int newVal)
    {
        OnHealthChanged?.Invoke(oldVal, newVal);
        
        // Quái hết máu
        if (newVal <= 0 && oldVal > 0)
        {
            PlayDeathSFX();
            Die();
        }
        else if (newVal < oldVal)
        {
            PlayHurtSFX();
        }
    }

    private void PlayHurtSFX()
    {
        if (_hurtSFX != null)
        {
            AudioManager.Instance.PlaySFX(_hurtSFX, transform.position);
        }
    }

    private void PlayDeathSFX()
    {
        if (_deathSFX != null)
        {
            AudioManager.Instance.PlaySFX(_deathSFX, transform.position);
        }
    }

    /// <summary>
    /// Kích hoạt từ Hitbox của Player.
    /// Thiết kế theo NGO Client-Authoritative Hit Detection (Client tự dò va chạm -> Request Server trừ máu)
    /// </summary>
    public virtual void TakeDamage(int damage, Vector3 hitPoint, Vector3 hitNormal, ulong instigatorClientId)
    {
        // Tránh quái đã chết vẫn ăn chém
        if (CurrentHealth.Value <= 0 || _isDead) return;

        if (IsServer)
        {
            ApplyDamage(damage, instigatorClientId);
        }
        else
        {
            // Client gọi RPC lên Server báo "Tao chém trúng quái này rồi!"
            TakeDamageServerRpc(damage, instigatorClientId);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TakeDamageServerRpc(int damage, ulong instigatorClientId)
    {
        ApplyDamage(damage, instigatorClientId);
    }

    /// <summary>
    /// Cốt lõi logic trừ máu vĩnh viễn (CHỈ CHẠY TRÊN SERVER)
    /// </summary>
    protected virtual void ApplyDamage(int damage, ulong instigatorClientId)
    {
        if (CurrentHealth.Value <= 0 || _isDead) return;

        // Server update state
        CurrentHealth.Value = Mathf.Max(0, CurrentHealth.Value - damage);
        
        // Cập nhật các AI / Stun / Aggro ở Server
        OnDamagedServerSide(damage, instigatorClientId);
    }

    /// <summary>
    /// Bắt buộc đám Enemy con (Slime, Skeleton, Boss) phải tự override lại mình làm gì khi bị đập.
    /// </summary>
    protected abstract void OnDamagedServerSide(int damage, ulong instigatorClientId);

    /// <summary>
    /// Xử lý Die.
    /// </summary>
    protected virtual void Die()
    {
        if (_isDead) return;
        _isDead = true;

        OnDeath?.Invoke();

        // 1. Phát anim chết (Trigger "Die")
        if (TryGetComponent<Animator>(out var anim))
        {
            anim.SetTrigger("IsDead");
        }

        // 2. Ẩn thanh máu UI (Kiểm tra kỹ trước khi tắt)
        if (_healthBarUI != null)
        {
            _healthBarUI.SetActive(false);
        }

        // 3. Tắt các logic cục bộ (Dành cho cả Client và Server)
        DisableLogic();

        // 4. Bắt đầu rơi bằng code (Chỉ thực hiện trên Server để đồng bộ vị trí chuẩn)
        if (IsServer)
        {
            StartCoroutine(DeathFallCoroutine());
            
            // 5. Đợi 3 giây rồi mới Despawn (Chỉ chạy trên Server)
            Invoke(nameof(DespawnEnemy), 3f);
        }
    }

    private void DisableLogic()
    {
        // Tắt AI
        if (TryGetComponent<Unity.Behavior.BehaviorGraphAgent>(out var agent))
        {
            agent.enabled = false;
        }

        // Tắt NavMeshAgent (Dừng dẫn đường)
        var nav = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (nav != null)
        {
            if (nav.isOnNavMesh) nav.isStopped = true;
            nav.enabled = false;
        }

        // Tắt script di chuyển
        var move = GetComponent<EnemyMovement>();
        if (move != null)
        {
            move.enabled = false;
        }

        // Tắt script chiến đấu
        var combat = GetComponent<EnemyCombatBase>();
        if (combat != null)
        {
            combat.enabled = false;
        }

        // Tắt Root Motion để không bị trượt theo anim
        var anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.applyRootMotion = false;
        }

        // Đổi Layer sang "Ignore Raycast" để Player đi xuyên qua
        gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");

        // Tắt va chạm hoàn toàn hoặc chuyển sang Trigger
        var col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }
    }

    private IEnumerator DeathFallCoroutine()
    {
        float verticalVelocity = 0;
        float fallDuration = 2.5f; // Giới hạn thời gian rơi tối đa
        float elapsed = 0;

        while (elapsed < fallDuration)
        {
            elapsed += Time.deltaTime;
            verticalVelocity -= _gravity * Time.deltaTime;
            Vector3 move = new Vector3(0, verticalVelocity * Time.deltaTime, 0);

            // Kiểm tra mặt đất bằng Raycast
            // Bắn tia từ giữa quái xuống dưới
            float rayLength = 0.2f; 
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, rayLength, _groundLayer))
            {
                // Chạm đất -> Dừng rơi và bám sát mặt đất
                transform.position = hit.point;
                yield break;
            }

            transform.position += move;
            yield return null;
        }
    }

    private void DespawnEnemy()
    {
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }
}
