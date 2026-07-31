using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// Bridge nhận Animation Events từ Animator (trên Player root)
/// và dispatch xuống các component con.
/// Gắn trên cùng GameObject với Animator.
/// SRS §4.1.2 (T1-7)
/// </summary>
public class PlayerAnimationEventReceiver : MonoBehaviour
{
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
    private PlayerInputHandler _inputHandler;
    private NetworkObject _networkObject;

    private void Awake()
    {
        CacheAttackHitboxes();

        if (_comboController == null)
            _comboController = GetComponent<AttackComboController>();

        _stateMachine = GetComponent<PlayerStateMachine>();
        _inputHandler = GetComponent<PlayerInputHandler>();
        _networkObject = GetComponent<NetworkObject>();

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
        int index = Random.Range(0, _footstepClips.Count);
        AudioManager.Instance.PlaySFX(_footstepClips[index]);
    }

    private bool CanPlayFootstep()
    {
        if (!CanPlayLocalMovementAudio()) return false;

        // Locomotion uses a Blend Tree, so Walk/Run animation events can still
        // arrive briefly while blending back to Idle. Require live movement
        // input and a grounded locomotion state before accepting the event.
        if (_inputHandler != null && !_inputHandler.IsMoving) return false;
        if (_stateMachine == null) return true;

        return _stateMachine.CurrentStateType is PlayerStateType.Walk
            or PlayerStateType.Run
            or PlayerStateType.CrouchWalk;
    }

    private void HandleStateChanged(PlayerStateType previousState, PlayerStateType nextState)
    {
        if (!CanPlayLocalMovementAudio()) return;

        switch (nextState)
        {
            case PlayerStateType.Jump:
                // Air dash/glide return to Jump only to resume falling physics;
                // that transition is not a new jump input and must stay silent.
                if (previousState is PlayerStateType.DashInAir or PlayerStateType.AirGlide)
                    break;

                AudioManager.Instance.PlaySFX(_jumpClip);
                break;

            case PlayerStateType.DoubleJump:
            case PlayerStateType.WallJump:
                AudioManager.Instance.PlaySFX(_jumpClip);
                break;

            case PlayerStateType.DashInAir:
            case PlayerStateType.DashOnGround:
                AudioManager.Instance.PlaySFX(_dashClip);
                break;
        }
    }

    private bool CanPlayLocalMovementAudio()
    {
        // Offline/test prefabs have no NetworkObject. Networked players only play
        // their owner's movement SFX, avoiding duplicate playback from proxies.
        return _networkObject == null || (_networkObject.IsSpawned && _networkObject.IsOwner);
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
