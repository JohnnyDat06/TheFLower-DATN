using Unity.Netcode;
using UnityEngine;

/// <summary>
/// PlayerController — Xử lý toàn bộ di chuyển và vật lý Player bằng Rigidbody3D.
/// Đọc state từ PlayerStateMachine, input từ PlayerInputHandler, config từ SOPlayerConfig.
/// KHÔNG tự đọc Input, KHÔNG tự quản lý state — chỉ react.
/// Camera-relative movement: hướng di chuyển dựa trên hướng camera.
/// SRS §4.1.2 · §4.3.3 · §3.1
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerController : NetworkBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SOPlayerConfig _config;
    [SerializeField] private Transform      _cameraLookTarget; // child empty transform

    private Rigidbody          _rb;
    private CapsuleCollider    _capsule;
    private PlayerStateMachine _fsm;
    private PlayerInputHandler _input;
    private NGOPlayerSync      _sync; // Thêm reference này

    // Runtime state
    private int     _jumpCount;
    private bool    _isGrounded;
    private float   _jumpHorizontalSpeed;
    private bool    _glideUsed;
    private bool    _isCrouching;
    private float   _slideTimer;
    private bool    _isTouchingWall;
    private Vector3 _wallNormal;

    // Air dash state
    private bool    _dashUsed;
    private float   _dashTimer;
    private float   _dashCooldownTimer;
    private Vector3 _dashVelocity;

    // Ground roll state
    private float   _rollTimer;
    private float   _rollCooldownTimer;
    private Vector3 _rollDirection;
    private float   _rollStartSpeed;    // tốc độ ngang lúc bắt đầu roll (walk/run speed)
    private float   _rollBoostSpeed;    // tốc độ boost thêm từ config (đỉnh curve)

    // Knockback state
    private float   _knockbackTimer;
    private bool    _externalMovementOverride;

    // Capsule original values (để restore sau Crouch)
    private float   _originalCapsuleHeight;
    private Vector3 _originalCapsuleCenter;

    private void Awake()
    {
        _rb     = GetComponent<Rigidbody>();
        _capsule = GetComponent<CapsuleCollider>();
        _fsm    = GetComponent<PlayerStateMachine>();
        _input  = GetComponent<PlayerInputHandler>();
        _sync   = GetComponent<NGOPlayerSync>(); // Lấy component này

        if (_config == null)
        {
            Debug.LogError("[PlayerController] SOPlayerConfig chưa được gán trong Inspector!");
            enabled = false;
            return;
        }

        // Lưu capsule gốc
        _originalCapsuleHeight = _capsule.height;
        _originalCapsuleCenter = _capsule.center;

        // Rigidbody setup
        _rb.freezeRotation = true;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void FixedUpdate()
    {
        if (!IsSpawned) return;

        // Proxies and loading/teleporting players are kinematic; do not write velocity.
        if (_rb.isKinematic) return;

        // CHẶN DI CHUYỂN TRONG LOBBY
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("Lobby"))
        {
            _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
            _rb.angularVelocity = Vector3.zero;
            return;
        }

        // CHẶN HOÀN TOÀN KHI ĐANG TELEPORT (SỬA LỖI RƠI XUYÊN SÀN)
        if (_sync != null && _sync.IsTeleporting)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            return;
        }

        // Guard: skip khi Dead/Respawning
        if (_fsm.CurrentStateType is PlayerStateType.Dead or PlayerStateType.Respawning) return;

        // Level-specific controllers (for example Level 04 flight) own the Rigidbody
        // while this flag is enabled. This prevents two movement systems writing velocity.
        if (IsOwner && _externalMovementOverride) return;

        // Cả Host và Client đều tính toán Ground và Wall để phục vụ Animation local được siêu mượt
        CheckGrounded();
        CheckWall();

        // Từ đoạn này trở xuống: Liên quan đến tác động vật lý (thay đổi vận tốc, ép lực), CHỈ OWNER được làm:
        if (!IsOwner) return;

        if (_input != null && _input.IsInputLocked)
        {
            _rb.linearVelocity = new Vector3(0f, _rb.linearVelocity.y, 0f);
            _rb.angularVelocity = Vector3.zero;
            return;
        }

        // Update timers
        if (_knockbackTimer > 0f) _knockbackTimer -= Time.fixedDeltaTime;

        // SAFETY NET: Đảm bảo không bao giờ bị kẹt mất trọng lực "bay trên trời" nếu lỡ ngắt state đột ngột.
        // NHƯNG: Không được ép bật khi đang Teleport hoặc đang đợi Spawn (để tránh rơi xuyên sàn khi đổi màn).
        bool shouldDisableGravityByState = _fsm.CurrentStateType == PlayerStateType.DashInAir || 
                                          _fsm.CurrentStateType == PlayerStateType.WallHang || 
                                          _fsm.CurrentStateType == PlayerStateType.AirGlide;
        
        bool isSyncWaiting = _sync != null && _sync.IsTeleporting;

        if (!shouldDisableGravityByState && !isSyncWaiting)
        {
            _rb.useGravity = true;
        }
        else if (isSyncWaiting)
        {
            _rb.useGravity = false;
            _rb.linearVelocity = new Vector3(0, Mathf.Max(0, _rb.linearVelocity.y), 0); // Chặn vận tốc rơi âm
        }

        HandleCrouch();
        HandleGroundSlide();
        HandleJump();
        HandleRoll();
        HandleAirDash();
        // HandleAirGlide(); // Tắt tính năng bay lơ lửng (Glide) theo yêu cầu
        HandleWallClimb();
        HandleMovement(); // Luôn cuối cùng — override velocity sau các force
    }

    private void CheckWall()
    {
        _isTouchingWall = false;

        if (_input.MoveInput.sqrMagnitude < 0.01f) return;

        var cam = Camera.main;
        if (cam == null) return;

        var camForward = cam.transform.forward;
        var camRight   = cam.transform.right;
        camForward.y = 0f;
        camRight.y   = 0f;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 moveDir = (camForward * _input.MoveInput.y + camRight * _input.MoveInput.x).normalized;
        Vector3 origin = transform.position + Vector3.up * (_capsule.height * 0.5f);
        
        // Dùng SphereCast quét theo đúng hướng nhân vật đang cố đi tới
        if (Physics.SphereCast(origin, _capsule.radius * 0.9f, moveDir, out var hit, 0.3f,
            _config.WallLayerMask))
        {
            _isTouchingWall = true;
            _wallNormal = hit.normal;
        }
    }

    // ─── HandleMovement — Camera-relative movement ───────────────────────────

    private void HandleMovement()
    {
        // Block hoàn toàn khi đang dash, roll, hoặc attack — velocity do handle riêng kiểm soát
        if (_fsm.CurrentStateType is PlayerStateType.WallHang
                                  or PlayerStateType.DashInAir
                                  or PlayerStateType.DashOnGround
                                  or PlayerStateType.Attack1
                                  or PlayerStateType.Attack2
                                  or PlayerStateType.Attack3) return;

        // Chặn di chuyển nếu đang bị knockback
        if (_knockbackTimer > 0f) return;

        bool isUsingJumpMomentum = _fsm.CurrentStateType is PlayerStateType.Jump
                                                            or PlayerStateType.DoubleJump
                                                            or PlayerStateType.AirGlide;

        if (_input.MoveInput.sqrMagnitude < 0.01f)
        {
            // Preserve takeoff momentum while airborne. Releasing movement input
            // must not turn a sprint jump into a stationary jump.
            if (!isUsingJumpMomentum)
                _rb.linearVelocity = new Vector3(0f, _rb.linearVelocity.y, 0f);
            return;
        }

        var cam = Camera.main;
        if (cam == null) return;

        var camForward = cam.transform.forward;
        var camRight   = cam.transform.right;

        camForward.y = 0f;
        camRight.y   = 0f;
        camForward.Normalize();
        camRight.Normalize();

        // Tính hướng di chuyển từ input
        var moveDir = camForward * _input.MoveInput.y + camRight * _input.MoveInput.x;

        // Tính tốc độ theo state hiện tại
        float speed = _fsm.CurrentStateType switch
        {
            PlayerStateType.Run        => _config.MoveSpeed * _config.SprintMultiplier,
            PlayerStateType.CrouchWalk => _config.MoveSpeed * _config.CrouchSpeedMultiplier,
            PlayerStateType.Jump or PlayerStateType.DoubleJump or PlayerStateType.AirGlide
                => Mathf.Max(_jumpHorizontalSpeed, _config.MoveSpeed * 0.5f),
            PlayerStateType.GroundSlide => 0f,           // slide dùng quán tính, không drive
            _                          => _config.MoveSpeed,
        };

        var targetVel = moveDir * speed;

        // Xử lý chống trượt: NẾU ĐANG CHẠM TƯỜNG & HƯỚNG ĐI ĐÂM VÀO TƯỜNG -> CHIẾU (SLIDE) VẬN TỐC THEO MẶT TƯỜNG
        if (_isTouchingWall && Vector3.Dot(moveDir, _wallNormal) < 0f)
        {
            targetVel = Vector3.ProjectOnPlane(targetVel, _wallNormal);
            // Chuẩn hóa lại tốc độ để không bị giảm tốc độ dọc theo mặt phẳng tường
            if (targetVel.sqrMagnitude > 0.01f)
            {
                targetVel = targetVel.normalized * speed;
            }
        }

        // Apply velocity ngang — giữ velocity Y hiện tại
        _rb.linearVelocity = new Vector3(targetVel.x, _rb.linearVelocity.y, targetVel.z);

        // Xoay Player về hướng di chuyển (Slerp — KHÔNG xoay theo camera)
        if (moveDir.sqrMagnitude > 0.01f)
        {
            var targetRot = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
        }
    }

    // ─── HandleJump ──────────────────────────────────────────────────────────

    private void HandleJump()
    {
        if (!_input.JumpPressed) return;

        // Chặn nhảy nếu đang thực hiện Dash/Roll, Slide, hoặc Attack
        if (_fsm.CurrentStateType is PlayerStateType.DashOnGround or PlayerStateType.GroundSlide
                                  or PlayerStateType.Attack1 or PlayerStateType.Attack2 or PlayerStateType.Attack3) return;

        if (!_isGrounded || _jumpCount != 0) return;

        _input.ConsumeJumpPressed();

        // Capture horizontal speed at takeoff. The intended input speed covers
        // sprint + jump pressed in the same frame, before Rigidbody catches up.
        float currentHorizontalSpeed = new Vector3(
            _rb.linearVelocity.x,
            0f,
            _rb.linearVelocity.z).magnitude;
        float intendedGroundSpeed = _input.IsMoving
            ? (_input.IsSprinting
                ? _config.MoveSpeed * _config.SprintMultiplier
                : _config.MoveSpeed)
            : 0f;
        _jumpHorizontalSpeed = Mathf.Max(currentHorizontalSpeed, intendedGroundSpeed);

        // Khôi phục trọng lực lập tức phòng trường hợp vừa thoát khỏi trạng thái DashInAir hoặc WallHang.
        _rb.useGravity = true;

        // Reset Y velocity trước khi nhảy để lực nhất quán.
        _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        _rb.AddForce(Vector3.up * _config.JumpForce, ForceMode.Impulse);

        _jumpCount = 1;
        _isGrounded = false;
        _fsm.TransitionTo(PlayerStateType.Jump);
    }

    // ─── HandleRoll (Ground) ─────────────────────────────────────────────────

    private void HandleRoll()
    {
        if (_rollCooldownTimer > 0f)
            _rollCooldownTimer -= Time.fixedDeltaTime;

        // ── Đang roll ────────────────────────────────────────────────────────
        if (_fsm.CurrentStateType == PlayerStateType.DashOnGround)
        {
            _rollTimer -= Time.fixedDeltaTime;

            // t: 1 → 0 theo thời gian roll
            float t = Mathf.Clamp01(_rollTimer / _config.RollDuration);

            // Boost curve: ease-in/out — đỉnh ở giữa, về 0 ở cuối
            // Dùng SmoothStep để boost mượt hơn 4t(1-t)
            float boostCurve  = Mathf.SmoothStep(0f, 1f, t * 2f) * Mathf.SmoothStep(0f, 1f, (1f - t) * 2f);
            float boostSpeed  = _rollBoostSpeed * boostCurve;

            // Base speed: tốc độ locomotion gốc giảm dần tuyến tính từ startSpeed → moveSpeed
            // Đảm bảo cuối roll vẫn có momentum walk/run, không về 0
            float baseSpeed   = Mathf.Lerp(_config.MoveSpeed, _rollStartSpeed, t);

            float totalSpeed  = baseSpeed + boostSpeed;

            _rb.linearVelocity = new Vector3(
                _rollDirection.x * totalSpeed,
                _rb.linearVelocity.y,
                _rollDirection.z * totalSpeed
            );

            if (_rollTimer <= 0f)
            {
                _rollCooldownTimer = _config.RollCooldown;

                // Trả velocity về locomotion ngay — tránh khựng 1 frame
                float exitSpeed = _input.IsMoving
                    ? (_input.IsSprinting ? _config.MoveSpeed * _config.SprintMultiplier : _config.MoveSpeed)
                    : 0f;

                var exitDir = _input.IsMoving ? _rollDirection : Vector3.zero;
                _rb.linearVelocity = new Vector3(
                    exitDir.x * exitSpeed,
                    _rb.linearVelocity.y,
                    exitDir.z * exitSpeed
                );

                _fsm.TransitionTo(_input.IsMoving
                    ? (_input.IsSprinting ? PlayerStateType.Run : PlayerStateType.Walk)
                    : PlayerStateType.Idle);
            }
            return;
        }

        // ── Điều kiện kích hoạt ──────────────────────────────────────────────
        if (_rollCooldownTimer > 0f) return;
        if (!_isGrounded) return;
        if (_fsm.CurrentStateType is PlayerStateType.GroundSlide
                                  or PlayerStateType.Dead
                                  or PlayerStateType.Respawning
                                  or PlayerStateType.Attack1
                                  or PlayerStateType.Attack2
                                  or PlayerStateType.Attack3) return;
        if (!_input.ConsumeDashPressed()) return;

        // Hướng roll: ưu tiên hướng di chuyển hiện tại, fallback transform.forward
        _rollDirection = _input.IsMoving ? ComputeDashDirection() : transform.forward;

        // Lấy tốc độ hiện tại làm baseline — giữ momentum walk/run
        float currentSpeed = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z).magnitude;
        _rollStartSpeed    = Mathf.Max(currentSpeed, _config.MoveSpeed);

        // Boost speed: khoảng cách phụ thêm / (integral của boostCurve ≈ 0.5 * duration)
        _rollBoostSpeed    = _config.RollDistance / (_config.RollDuration * 0.5f);

        _rollTimer = _config.RollDuration;

        // Xoay nhân vật về hướng roll ngay lập tức
        if (_rollDirection.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(_rollDirection);

        _fsm.TransitionTo(PlayerStateType.DashOnGround);
    }

    // ─── HandleAirDash ───────────────────────────────────────────────────────

    private void HandleAirDash()
    {
        if (_dashCooldownTimer > 0f)
            _dashCooldownTimer -= Time.fixedDeltaTime;

        // ── Đang air dash — duy trì velocity ngang cố định, tắt gravity ──
        if (_fsm.CurrentStateType == PlayerStateType.DashInAir)
        {
            _dashTimer -= Time.fixedDeltaTime;
            _rb.linearVelocity = new Vector3(_dashVelocity.x, 0f, _dashVelocity.z);
            _rb.useGravity = false;

            if (_dashTimer <= 0f)
            {
                _rb.useGravity = true;
                _fsm.TransitionTo(PlayerStateType.Jump);
            }
            return;
        }

        // ── Điều kiện kích hoạt ──
        if (_dashCooldownTimer > 0f) return;
        if (_isGrounded || _dashUsed) return;
        if (_fsm.CurrentStateType is not (PlayerStateType.Jump
                                       or PlayerStateType.DoubleJump
                                       or PlayerStateType.AirGlide)) return;
        if (!_input.ConsumeDashPressed()) return;

        var dashDir   = ComputeDashDirection();
        float speed   = _config.DashDistance / _config.DashDuration;
        _dashVelocity = dashDir * speed;

        _rb.linearVelocity = new Vector3(_dashVelocity.x, 0f, _dashVelocity.z);
        _rb.useGravity     = false;

        _dashUsed          = true;
        _dashTimer         = _config.DashDuration;
        _dashCooldownTimer = _config.DashCooldown;

        if (dashDir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dashDir);

        _fsm.TransitionTo(PlayerStateType.DashInAir);
    }

    /// <summary>Tính hướng dash từ camera + input. Fallback về transform.forward.</summary>
    private Vector3 ComputeDashDirection()
    {
        var cam = Camera.main;
        if (cam != null && _input.MoveInput.sqrMagnitude > 0.01f)
        {
            var fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
            var rgt = cam.transform.right;   rgt.y = 0f; rgt.Normalize();
            return (fwd * _input.MoveInput.y + rgt * _input.MoveInput.x).normalized;
        }
        return transform.forward;
    }

    // ─── HandleAirGlide ──────────────────────────────────────────────────────
    private void HandleAirGlide()
    {
        bool isFalling = _rb.linearVelocity.y < 0f;
        bool canGlide  = _input.JumpHeld && isFalling && !_glideUsed && !_isGrounded;

        if (canGlide && _fsm.CurrentStateType is PlayerStateType.Jump or PlayerStateType.DoubleJump)
        {
            _rb.linearVelocity = new Vector3(
                _rb.linearVelocity.x,
                Mathf.Max(_rb.linearVelocity.y, -2f), // giới hạn tốc độ rơi
                _rb.linearVelocity.z
            );
            _rb.useGravity = false;
            _rb.AddForce(Vector3.up * (Physics.gravity.magnitude * (1f - _config.GlideGravityScale)), ForceMode.Acceleration);
            _glideUsed = true;
            _fsm.TransitionTo(PlayerStateType.AirGlide);
        }

        // Khi không giữ Jump nữa hoặc chạm đất → tắt glide
        if (_fsm.CurrentStateType == PlayerStateType.AirGlide && !_input.JumpHeld)
        {
            _rb.useGravity = true;
            _fsm.TransitionTo(PlayerStateType.Jump);
        }
    }

    // ─── HandleCrouch ────────────────────────────────────────────────────────

    private void HandleCrouch()
    {
        if (_input.IsCrouching && !_isCrouching)
        {
            // Thu nhỏ capsule
            _capsule.height = _originalCapsuleHeight * 0.5f;
            _capsule.center = new Vector3(0f, _capsule.height * 0.5f, 0f);
            _isCrouching = true;
        }
        else if (!_input.IsCrouching && _isCrouching)
        {
            // Kiểm tra có đủ chỗ đứng dậy không (raycast lên)
            if (!Physics.Raycast(transform.position, Vector3.up, _originalCapsuleHeight))
            {
                _capsule.height = _originalCapsuleHeight;
                _capsule.center = _originalCapsuleCenter;
                _isCrouching = false;
            }
        }
    }

    // ─── HandleGroundSlide ───────────────────────────────────────────────────

    private void HandleGroundSlide()
    {
        if (_fsm.CurrentStateType == PlayerStateType.GroundSlide)
        {
            _slideTimer -= Time.deltaTime;
            if (_slideTimer <= 0f || !_input.IsCrouching)
            {
                // Kết thúc slide
                _capsule.height = _originalCapsuleHeight;
                _capsule.center = _originalCapsuleCenter;
                _isCrouching = false;
                _fsm.TransitionTo(_input.IsMoving ? PlayerStateType.Run : PlayerStateType.Idle);
            }
            return;
        }

        // Bắt đầu slide: đang Run + Crouch pressed + isGrounded
        if (_fsm.CurrentStateType == PlayerStateType.Run && _input.IsCrouching && _isGrounded)
        {
            _slideTimer = _config.GroundSlideDuration;
            _capsule.height = _originalCapsuleHeight * 0.5f;
            _capsule.center = new Vector3(0f, _capsule.height * 0.5f, 0f);
            _isCrouching = true;

            // Lực quán tính ban đầu theo hướng đang chạy
            _rb.AddForce(transform.forward * _config.GroundSlideForce, ForceMode.Impulse);
            _fsm.TransitionTo(PlayerStateType.GroundSlide);
        }
    }

    // ─── HandleWallClimb — chỉ active khi WallClimbEnabled ──────────────────

    private void HandleWallClimb()
    {
        if (!_fsm.WallClimbEnabled) return;

        // Raycast tìm tường xung quanh khi đang không IsGrounded
        _isTouchingWall = false;
        var dirs = new[] { transform.forward, -transform.forward, transform.right, -transform.right };
        foreach (var dir in dirs)
        {
            if (Physics.Raycast(transform.position + Vector3.up, dir, out var hit, 0.6f,
                _config.WallLayerMask))
            {
                _isTouchingWall = true;
                _wallNormal = hit.normal;
                break;
            }
        }

        // Bắt đầu WallHang
        if (_isTouchingWall && !_isGrounded
            && _fsm.CurrentStateType is PlayerStateType.Jump or PlayerStateType.DoubleJump or PlayerStateType.AirGlide)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.useGravity = false;
            _fsm.TransitionTo(PlayerStateType.WallHang);
        }

        // WallJump
        if (_fsm.CurrentStateType == PlayerStateType.WallHang && _input.JumpPressed)
        {
            _input.ConsumeJumpPressed();
            _rb.useGravity = true;
            var jumpDir = (_wallNormal + Vector3.up).normalized;
            _rb.AddForce(jumpDir * _config.WallJumpVerticalForce
                         + _wallNormal * _config.WallJumpHorizontalForce, ForceMode.Impulse);
            _jumpCount = 1; // Chặn nhảy tiếp cho đến khi chạm đất.
            _glideUsed = false;
            _fsm.TransitionTo(PlayerStateType.WallJump);
        }

        // Rơi nếu không còn chạm tường
        if (_fsm.CurrentStateType == PlayerStateType.WallHang && !_isTouchingWall)
        {
            _rb.useGravity = true;
            _fsm.TransitionTo(PlayerStateType.Jump);
        }
    }

    // ─── CheckGrounded ───────────────────────────────────────────────────────

    private void CheckGrounded()
    {
        // Khi đang nhảy lên (Velocity Y > 0), tạm thời coi như không chạm đất để tránh reset state xuống Idle/Walk ngay lập tức
        if (_rb.linearVelocity.y > 0.1f)
        {
            _isGrounded = false;
            return;
        }

        // Bắt đầu SphereCast từ vị trí ngang tâm capsule để tránh collider chồng chéo ngay lúc đầu
        float radius  = _capsule.radius * 0.9f;
        Vector3 origin = transform.position + Vector3.up * (_capsule.height * 0.5f);
        float castDistance = (_capsule.height * 0.5f) - radius + 0.2f;

        _isGrounded = Physics.SphereCast(
            origin, 
            radius, 
            Vector3.down, 
            out _, 
            castDistance,
            _config.GroundLayerMask);

        if (_isGrounded)
        {
            _jumpCount  = 0;
            _jumpHorizontalSpeed = 0f;
            _glideUsed  = false;
            _dashUsed   = false;
            _rb.useGravity = true;

            // Reset về ground state nếu đang ở air state hoặc knockback
            if (_fsm.CurrentStateType is PlayerStateType.Jump
                or PlayerStateType.DoubleJump
                or PlayerStateType.AirGlide
                or PlayerStateType.WallJump
                or PlayerStateType.DashInAir
                or PlayerStateType.Knockback)
            {
                // Chỉ thoát knockback khi timer đã hết hoặc vận tốc đã giảm đáng kể
                if (_fsm.CurrentStateType == PlayerStateType.Knockback && _knockbackTimer > 0f) return;

                _fsm.TransitionTo(_input.IsMoving
                    ? (_input.IsSprinting ? PlayerStateType.Run : PlayerStateType.Walk)
                    : PlayerStateType.Idle);
            }
        }
    }

    // ─── Public Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Hất người chơi lên cao với một lực nhất định (Impulse).
    /// Dùng cho các vật thể môi trường như Nấm bật.
    /// </summary>
    public void Bounce(float force)
    {
        if (!IsOwner) return;

        // Reset vận tốc Y để lực bật luôn nhất quán
        _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        _rb.AddForce(Vector3.up * force, ForceMode.Impulse);

        // Đảm bảo thoát khỏi Grounded ngay lập tức
        _isGrounded = false;
        
        // Chuyển sang trạng thái Jump để có thể điều hướng trên không
        _fsm.TransitionTo(PlayerStateType.Jump);
        
        // Chặn nhảy tiếp cho đến khi chạm đất.
        _jumpCount = 1;
    }

    /// <summary>Expose IsGrounded cho các class khác (ví dụ PlayerAnimator).</summary>
    public bool IsGrounded => _isGrounded;

    public void SetExternalMovementOverride(bool enabled)
    {
        if (!IsOwner) return;
        _externalMovementOverride = enabled;

        if (!enabled && _rb != null)
        {
            _rb.useGravity = true;
        }
    }

    [ClientRpc]
    public void ApplyKnockbackClientRpc(Vector3 force)
    {
        if (!IsOwner) return;

        // Ép lại trọng tâm thẳng đứng phòng trường hợp bị va chạm vật lý làm nghiêng
        _rb.freezeRotation = true;
        _rb.angularVelocity = Vector3.zero;

        // Reset vận tốc ngang để lực hất có tác động rõ rệt nhất
        _rb.linearVelocity = new Vector3(0f, _rb.linearVelocity.y, 0f);
        
        // Áp dụng lực hất (Impulse)
        _rb.AddForce(force, ForceMode.Impulse);

        // Chặn điều khiển di chuyển trong một khoảng thời gian ngắn (ví dụ 0.4s)
        _knockbackTimer = 0.5f;

        // Xoay mặt Player về phía hướng bay của đá (như thể bị đập vào mặt)
        if (force.sqrMagnitude > 0.01f)
        {
            Vector3 lookDir = -force.normalized;
            lookDir.y = 0;
            if (lookDir.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(lookDir);
            }
        }

        // Chuyển sang trạng thái Knockback (nếu đã có state class) hoặc Jump để quản lý physics trên không
        _fsm.TransitionTo(PlayerStateType.Knockback);

        // Nếu đang ở dưới đất, hất nhẹ lên để giảm ma sát và trông tự nhiên hơn
        if (_isGrounded)
        {
            _rb.AddForce(Vector3.up * 3f, ForceMode.Impulse);
            _isGrounded = false;
        }
    }
}
