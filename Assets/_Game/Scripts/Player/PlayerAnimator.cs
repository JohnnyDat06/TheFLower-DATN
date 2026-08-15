using UnityEngine;
using Networking.LobbySystem;

/// <summary>
/// PlayerAnimator — Class duy nhất được phép chạm vào Unity Animator.
/// Đọc state từ PlayerStateMachine mỗi frame → set Animator params.
/// Root Motion = OFF. Tất cả parameter dùng StringToHash.
/// SRS §4.1.4
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerAnimator : MonoBehaviour
{
    [SerializeField] private PlayerStateMachine _fsm;
    [SerializeField, Min(0f)] private float _speedDampTime = 0.08f;
    [SerializeField, Min(0f)] private float _freeFallDelay = 2.0f;

    private Animator _animator;
    private PlayerController _controller;
    private Rigidbody _rb;
    private PlayerStateType _previousState;
    private float _fallingTimer;
    private bool _wasJumping; 
    private LobbyCharacterAppearance _appearance;

    // Animator Parameter Name Constants — dùng hash để tránh typo, tăng hiệu năng
    private static readonly int SPEED                = Animator.StringToHash("Speed");
    private static readonly int IS_GROUNDED          = Animator.StringToHash("IsGrounded");
    private static readonly int IS_CROUCHING         = Animator.StringToHash("IsCrouching");
    private static readonly int IS_GLIDING           = Animator.StringToHash("IsGliding");
    private static readonly int PLAYER_DEATH         = Animator.StringToHash("Player_Death");
    private static readonly int JUMP_BOOL            = Animator.StringToHash("Jump");
    private static readonly int FREE_FALL_BOOL       = Animator.StringToHash("FreeFall");
    private static readonly int DJUMP_TRIGGER        = Animator.StringToHash("DoubleJumpTrigger");
    private static readonly int WJUMP_TRIGGER        = Animator.StringToHash("WallJumpTrigger");
    private static readonly int SLIDE_TRIGGER        = Animator.StringToHash("SlideTrigger");
    private static readonly int RESPAWN_TRIGGER      = Animator.StringToHash("RespawnTrigger");
    private static readonly int VERTICAL_SPEED       = Animator.StringToHash("VerticalSpeed");
    private static readonly int DASH_TRIGGER         = Animator.StringToHash("DashTrigger");
    private static readonly int GROUND_DASH_TRIGGER  = Animator.StringToHash("GroundDashTrigger");
    private static readonly int ATTACK1_TRIGGER      = Animator.StringToHash("Attack1Trigger");
    private static readonly int ATTACK2_TRIGGER      = Animator.StringToHash("Attack2Trigger");
    private static readonly int ATTACK3_TRIGGER      = Animator.StringToHash("Attack3Trigger");
    private static readonly int ATTACK_COUNT         = Animator.StringToHash("AttackCount");
    private static readonly int PLAYER_HIT           = Animator.StringToHash("Player_Hit");

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _animator.applyRootMotion = false; // BẮT BUỘC — vị trí do Rigidbody điều khiển

        if (_fsm == null)
        {
            _fsm = GetComponent<PlayerStateMachine>();
        }

        _controller = GetComponent<PlayerController>();
        _rb = GetComponent<Rigidbody>();
        _appearance = GetComponent<LobbyCharacterAppearance>();

        _previousState = PlayerStateType.Idle;
    }

    private void Update()
    {
        if (_fsm == null || _animator == null) return;

        var currentState = _fsm.CurrentStateType;

        float targetSpeed = StateToSpeed(currentState);
        _animator.SetFloat(SPEED, targetSpeed, _speedDampTime, Time.deltaTime);

        bool isGrounded = _controller != null ? _controller.IsGrounded : IsGroundedState(currentState);
        
        float verticalSpeed = _rb != null ? _rb.linearVelocity.y : 0f;
        
        // Multiplayer Proxy Client sẽ bị NetworkTransform thiết lập vị trí mà không sinh vận tốc Rigidbody.
        // Căn cứ hoàn toàn vào FSM State được đồng bộ qua mạng để chạy Animation chuẩn xác.
        // Bật Root Motion riêng biệt cho các đòn tấn công để player tiến lên theo animation
        bool isAttacking = currentState is PlayerStateType.Attack1 or PlayerStateType.Attack2 or PlayerStateType.Attack3;
        _animator.applyRootMotion = isAttacking;

        bool isJumpRising  = currentState is PlayerStateType.Jump or PlayerStateType.DoubleJump or PlayerStateType.WallJump;
        
        // Logic phân biệt rơi tự do (Walk Off) và rơi sau khi nhảy (Jump Loop)
        if (isGrounded || currentState == PlayerStateType.WallHang)
        {
            _wasJumping = false;
            _fallingTimer = 0f;
        }
        else if (isJumpRising)
        {
            _wasJumping = true;
        }

        bool isFallingCondition = !isGrounded && verticalSpeed <= 0.01f;
        bool isFreeFalling = false;

        if (isFallingCondition)
        {
            if (_wasJumping)
            {
                // Nếu đang trong chuỗi nhảy (Jump -> Fall), animation chạy bình thường (liền mạch)
                isFreeFalling = true;
            }
            else
            {
                // Nếu rơi tự do từ trên cao (không nhảy), đợi delay rồi mới bật FreeFall
                _fallingTimer += Time.deltaTime;
                if (_fallingTimer >= _freeFallDelay)
                {
                    isFreeFalling = true;
                }
            }
        }
        else
        {
            _fallingTimer = 0f;
        }

        if (currentState == PlayerStateType.WallHang || currentState == PlayerStateType.WallJump)
        {
             if (currentState == PlayerStateType.WallHang)
             {
                 isJumpRising = false;
                 isFreeFalling = false;
             }
        }

        _animator.SetBool(IS_GROUNDED, isGrounded);
        _animator.SetBool(IS_CROUCHING, currentState is PlayerStateType.CrouchIdle or PlayerStateType.CrouchWalk);
        _animator.SetBool(IS_GLIDING,   currentState == PlayerStateType.AirGlide);
        _animator.SetBool(PLAYER_DEATH, currentState == PlayerStateType.Dead);

        // Always set parameters instead of caching the existence check, 
        // to prevent bugs if the animator controller is swapped dynamically.
        _animator.SetBool(JUMP_BOOL, isJumpRising);
        _animator.SetBool(FREE_FALL_BOOL, isFreeFalling);
        _animator.SetFloat(VERTICAL_SPEED, verticalSpeed);

        // Triggers — chỉ set khi ENTER state (previous → current)
        if (currentState != _previousState)
        {
            switch (currentState)
            {
                case PlayerStateType.DoubleJump:   SetTrigger(DJUMP_TRIGGER);        break;
                case PlayerStateType.WallJump:     SetTrigger(WJUMP_TRIGGER);        break;
                case PlayerStateType.GroundSlide:  SetTrigger(SLIDE_TRIGGER);        break;
                case PlayerStateType.Respawning:   
                    SetTrigger(RESPAWN_TRIGGER);
                    _animator.SetBool(PLAYER_DEATH, false); // Đảm bảo thoát trạng thái chết
                    break;
                case PlayerStateType.DashInAir:    SetTrigger(DASH_TRIGGER);         break;
                case PlayerStateType.DashOnGround: SetTrigger(GROUND_DASH_TRIGGER);  break;

                case PlayerStateType.Attack1:
                    SetTrigger(ATTACK1_TRIGGER);
                    _animator.SetInteger(ATTACK_COUNT, 1);
                    break;
                case PlayerStateType.Attack2:
                    SetTrigger(ATTACK2_TRIGGER);
                    _animator.SetInteger(ATTACK_COUNT, 2);
                    break;
                case PlayerStateType.Attack3:
                    SetTrigger(ATTACK3_TRIGGER);
                    _animator.SetInteger(ATTACK_COUNT, 3);
                    break;

                case PlayerStateType.Knockback:
                    SetTrigger(PLAYER_HIT);
                    break;

                case PlayerStateType.Idle:
                case PlayerStateType.Walk:
                case PlayerStateType.Run:
                    _animator.SetInteger(ATTACK_COUNT, 0);
                    break;
            }

            _previousState = currentState;
        }
    }

    /// <summary>Chuyển PlayerStateType sang Speed float cho Blend Tree.</summary>
    private float StateToSpeed(PlayerStateType state) => state switch
    {
        PlayerStateType.Walk or PlayerStateType.CrouchWalk => 0.5f,
        PlayerStateType.Run                                => 1.0f,
        _                                                  => 0.0f,
    };

    /// <summary>Kiểm tra state có phải ground state không.</summary>
    private bool IsGroundedState(PlayerStateType state) => state is
        PlayerStateType.Idle or PlayerStateType.Walk or PlayerStateType.Run or
        PlayerStateType.CrouchIdle or PlayerStateType.CrouchWalk or
        PlayerStateType.GroundSlide or PlayerStateType.DashOnGround;

    private void SetTrigger(int parameterHash)
    {
        _animator.SetTrigger(parameterHash);
        if (_appearance == null) _appearance = GetComponent<LobbyCharacterAppearance>();
        _appearance?.MirrorTrigger(parameterHash);
    }

    /// <summary>Kích hoạt animation trúng đòn (Player_Hit).</summary>
    public void TriggerHit()
    {
        if (_animator != null)
        {
            SetTrigger(PLAYER_HIT);
        }
    }
}
