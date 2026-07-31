using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// Bridge nhận Animation Events từ Animator (trên Player root)
/// và dispatch xuống các component con.
/// Gắn trên cùng GameObject với Animator.
/// SRS §4.1.2 (T1-7)
/// </summary>
public class PlayerAnimationEventReceiver : NetworkBehaviour
{
    private const float MovementSfxMinDistance = 1.5f;
    private const float MovementSfxMaxDistance = 14f;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    [Header("References")]
    [Tooltip("Fallback nếu cần bind tay một hitbox cụ thể trong Inspector")]
    [SerializeField] private AttackHitbox _attackHitbox;

    [Tooltip("Gắn vào AttackComboController trên Player root")]
    [SerializeField] private AttackComboController _comboController;

    [Header("Audio")]
    [SerializeField] private List<SOAudioClip> _footstepClips;
    [SerializeField] private SOAudioClip _jumpClip;
    [SerializeField] private SOAudioClip _dashClip;

    private readonly List<AttackHitbox> _attackHitboxes = new();
    private PlayerStateMachine _stateMachine;
    private NetworkObject _networkObject;
    private Animator _animator;

    private void Awake()
    {
        CacheAttackHitboxes();

        if (_comboController == null)
            _comboController = GetComponent<AttackComboController>();

        _stateMachine = GetComponent<PlayerStateMachine>();
        _networkObject = GetComponent<NetworkObject>();
        _animator = GetComponent<Animator>();

        if (_attackHitboxes.Count == 0)
            Debug.LogWarning("[AnimEventReceiver] AttackHitbox không tìm thấy trong children!");

        if (_comboController == null)
            Debug.LogWarning("[AnimEventReceiver] AttackComboController không tìm thấy!");
    }

    private void OnEnable()
    {
        if (_stateMachine == null)
            _stateMachine = GetComponent<PlayerStateMachine>();

        if (_stateMachine != null)
            _stateMachine.OnStateChanged += HandleStateChanged;
    }

    private void OnDisable()
    {
        if (_stateMachine != null)
            _stateMachine.OnStateChanged -= HandleStateChanged;
    }

    private void CacheAttackHitboxes()
    {
        _attackHitboxes.Clear();

        // Include inactive children vì một trong hai model sẽ bị tắt theo role.
        var hitboxes = GetComponentsInChildren<AttackHitbox>(true);
        foreach (var hitbox in hitboxes)
        {
            if (hitbox != null && !_attackHitboxes.Contains(hitbox))
                _attackHitboxes.Add(hitbox);
        }

        if (_attackHitbox != null && !_attackHitboxes.Contains(_attackHitbox))
            _attackHitboxes.Add(_attackHitbox);

        if (_attackHitboxes.Count > 0)
            _attackHitbox = _attackHitboxes[0];
    }

    // ─── Gọi bởi Animation Event trên Clips ───────────────

    /// <summary>
    /// Phát âm thanh bước chân.
    /// Animation Event → Function: "PlayFootstep"
    /// </summary>
    public void PlayFootstep()
    {
        if (!CanPlayFootstep()) return;
        if (_footstepClips == null || _footstepClips.Count == 0) return;

        int clipIndex = Random.Range(0, _footstepClips.Count);

        // Only the owner event is authoritative. Play it immediately locally
        // (no round-trip latency), then relay the same clip to remote players.
        if (_networkObject != null && _networkObject.IsSpawned)
        {
            if (!_networkObject.IsOwner) return;

            PlayMovementSfx(_footstepClips[clipIndex]);

            if (IsServer)
                PlayFootstepClientRpc(clipIndex);
            else
                SubmitFootstepServerRpc(clipIndex);

            return;
        }

        PlayMovementSfx(_footstepClips[clipIndex]);
    }

    [ServerRpc]
    private void SubmitFootstepServerRpc(int clipIndex)
    {
        if (_footstepClips == null || _footstepClips.Count == 0) return;
        if (clipIndex < 0 || clipIndex >= _footstepClips.Count) return;
        PlayFootstepClientRpc(clipIndex);
    }

    [ClientRpc]
    private void PlayFootstepClientRpc(int clipIndex)
    {
        // The owner already played the Animation Event immediately.
        if (_networkObject != null && _networkObject.IsOwner) return;
        if (_footstepClips == null || clipIndex < 0 || clipIndex >= _footstepClips.Count)
            return;

        PlayMovementSfx(_footstepClips[clipIndex]);
    }

    private bool CanPlayFootstep()
    {
        if (!CanPlayNetworkedMovementAudio()) return false;

        // Gate with the actual Blend Tree parameter that produced the event.
        // This avoids stale FSM timing on Client while still blocking events
        // emitted during the final blend back to Idle.
        return _animator == null || _animator.GetFloat(SpeedHash) > 0.05f;
    }

    private void HandleStateChanged(PlayerStateType previousState, PlayerStateType nextState)
    {
        if (!CanPlayNetworkedMovementAudio()) return;

        switch (nextState)
        {
            case PlayerStateType.Jump:
                // Air dash/glide return to Jump only to resume falling physics;
                // that transition is not a new jump input and must stay silent.
                if (previousState is PlayerStateType.DashInAir or PlayerStateType.AirGlide)
                    break;

                PlayMovementSfx(_jumpClip);
                break;

            case PlayerStateType.DoubleJump:
            case PlayerStateType.WallJump:
                PlayMovementSfx(_jumpClip);
                break;

            case PlayerStateType.DashInAir:
            case PlayerStateType.DashOnGround:
                PlayMovementSfx(_dashClip);
                break;
        }
    }

    private void PlayMovementSfx(SOAudioClip clip)
    {
        int playbackScope = gameObject.GetInstanceID();
        bool isLocallyControlled = IsLocallyControlled();

        // The local player's camera/listener may be positioned differently on
        // Host and Client. Keep self audio 2D so the owner always hears it.
        if (isLocallyControlled)
        {
            AudioManager.Instance.PlaySFX(clip, playbackScope: playbackScope);
            return;
        }

        // Other players remain positional and are silent outside the range.
        AudioManager.Instance.PlaySFX(
            clip,
            transform.position,
            MovementSfxMinDistance,
            MovementSfxMaxDistance,
            AudioRolloffMode.Linear,
            playbackScope);
    }

    private bool CanPlayNetworkedMovementAudio()
    {
        // State and animator data are already replicated. Let every local proxy
        // play its avatar's 3D SFX once so nearby host/client players hear it.
        return _networkObject == null || _networkObject.IsSpawned;
    }

    private bool IsLocallyControlled()
    {
        return _networkObject == null
            || !_networkObject.IsSpawned
            || _networkObject.IsOwner;
    }

    /// <summary>
    /// Kích hoạt hitbox — gọi tại frame cú đấm bắt đầu tiếp xúc.
    /// Animation Event → Function: "EnableHitbox"
    /// </summary>
    public void EnableHitbox()
    {
        for (int i = 0; i < _attackHitboxes.Count; i++)
        {
            _attackHitboxes[i]?.EnableHitbox();
        }
    }

    /// <summary>
    /// Tắt hitbox — gọi tại frame cú đấm kết thúc tiếp xúc.
    /// Animation Event → Function: "DisableHitbox"
    /// </summary>
    public void DisableHitbox()
    {
        for (int i = 0; i < _attackHitboxes.Count; i++)
        {
            _attackHitboxes[i]?.DisableHitbox();
        }
    }

    /// <summary>
    /// Mở combo window — gọi tại 60-70% thời lượng clip Attack1 và Attack2.
    /// Animation Event → Function: "OpenComboWindow"
    /// </summary>
    public void OpenComboWindow()
    {
        _comboController?.OpenComboWindow();
    }
}
