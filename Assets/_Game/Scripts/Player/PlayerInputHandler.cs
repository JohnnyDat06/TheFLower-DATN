using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PlayerInputHandler — layer duy nhất được phép đọc input.
/// Các class khác chỉ hỏi Handler, KHÔNG BAO GIỜ gọi InputSystem trực tiếp.
/// Yêu cầu: CHỈ CHẠY trên máy khách là LẤY QUYỀN SỞ HỮU (IsOwner) của bản sao Player này.
/// SRS §4.1.5
/// </summary>
[DefaultExecutionOrder(-100)] // Chạy trước PlayerStateMachine và PlayerController
public class PlayerInputHandler : NetworkBehaviour
{
    [SerializeField] private InputActionAsset _inputActions;
    [SerializeField, Min(0f)] private float _jumpBufferDuration = 0.12f;
    [SerializeField] private SOPlayerConfig _config;

    [Header("Camera Sensitivity")]
    [SerializeField, Range(Constants.Camera.MIN_DEVICE_SENSITIVITY, Constants.Camera.MAX_DEVICE_SENSITIVITY)]
    private float _mouseCameraSensitivity = 1f;
    [SerializeField, Range(Constants.Camera.MIN_DEVICE_SENSITIVITY, Constants.Camera.MAX_DEVICE_SENSITIVITY)]
    private float _gamepadCameraSensitivity = 1f;

    // Cached InputActions
    private InputAction _moveAction;
    private InputAction _sprintAction;
    private InputAction _jumpAction;
    private InputAction _crouchAction;
    private InputAction _interactAction;
    private InputAction _pauseAction;
    private InputAction _cameraLookAction;
    private InputAction _dashAction;
    private InputAction _attackAction;
    private InputAction _chatAction;
    private InputAction _voiceMuteAction;
    private InputAction _stickerWheelAction;
    private InputAction _stickerNavigateAction;
    private InputAction _stickerPreviousSetAction;
    private InputAction _stickerNextSetAction;
    private InputAction _stickerCancelAction;

    private bool  _inputLocked;
    private float _jumpBufferTimer;
    private float _dashBufferTimer;

    // ─── Movement Properties (read-only cho class khác) ──────────────────────

    /// <summary>Raw WASD/Arrow input.</summary>
    public Vector2 MoveInput { get; private set; }

    /// <summary>True khi MoveInput.magnitude > 0.1f.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>True khi Sprint held.</summary>
    public bool IsSprinting { get; private set; }

    /// <summary>True khi Crouch held.</summary>
    public bool IsCrouching { get; private set; }

    // ─── Action Properties (consumed once per press — reset sau khi đọc) ─────

    /// <summary>Jump down this frame.</summary>
    public bool JumpPressed { get; private set; }

    /// <summary>Jump held.</summary>
    public bool JumpHeld { get; private set; }

    /// <summary>Interact down this frame.</summary>
    public bool InteractPressed { get; private set; }

    /// <summary>Pause down this frame.</summary>
    public bool PausePressed { get; private set; }

    /// <summary>Dash pressed this frame.</summary>
    public bool DashPressed { get; private set; }

    /// <summary>Attack pressed this frame.</summary>
    public bool AttackPressed { get; private set; }

    /// <summary>True for one frame when the chat shortcut is pressed.</summary>
    public bool ChatPressed { get; private set; }

    /// <summary>True for one frame when the voice mute shortcut is pressed.</summary>
    public bool VoiceMutePressed { get; private set; }

    /// <summary>True while the sticker wheel binding is held.</summary>
    public bool StickerWheelHeld { get; private set; }

    /// <summary>Raw stick direction used to select a sticker while the wheel is open.</summary>
    public Vector2 StickerNavigateInput { get; private set; }

    /// <summary>True for one frame when the previous sticker set is requested.</summary>
    public bool StickerPreviousSetPressed { get; private set; }

    /// <summary>True for one frame when the next sticker set is requested.</summary>
    public bool StickerNextSetPressed { get; private set; }

    /// <summary>True for one frame when the sticker wheel is canceled.</summary>
    public bool StickerCancelPressed { get; private set; }

    public bool IsInputLocked => _inputLocked;

    // ─── Camera Properties ───────────────────────────────────────────────────

    /// <summary>Mouse delta.</summary>
    public Vector2 CameraLookDelta { get; private set; }

    /// <summary>Tắt khi camera đặc biệt.</summary>
    public bool CameraLookEnabled { get; private set; } = true;

    public float GamepadCameraSensitivity
    {
        get => _gamepadCameraSensitivity;
        set => _gamepadCameraSensitivity = Mathf.Clamp(
            value,
            Constants.Camera.MIN_DEVICE_SENSITIVITY,
            Constants.Camera.MAX_DEVICE_SENSITIVITY);
    }

    public float MouseCameraSensitivity
    {
        get => _mouseCameraSensitivity;
        set => _mouseCameraSensitivity = Mathf.Clamp(
            value,
            Constants.Camera.MIN_DEVICE_SENSITIVITY,
            Constants.Camera.MAX_DEVICE_SENSITIVITY);
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        _mouseCameraSensitivity = PlayerPrefs.GetFloat(
            Constants.PlayerPrefsKeys.MOUSE_CAMERA_SENSITIVITY,
            _mouseCameraSensitivity);
        _gamepadCameraSensitivity = PlayerPrefs.GetFloat(
            Constants.PlayerPrefsKeys.GAMEPAD_CAMERA_SENSITIVITY,
            _gamepadCameraSensitivity);

        if (_inputActions == null)
        {
            Debug.LogError("[PlayerInputHandler] InputActionAsset chưa được gán trong Inspector!");
            return;
        }

        var playerMap = _inputActions.FindActionMap("Player");
        if (playerMap == null)
        {
            Debug.LogError("[PlayerInputHandler] Không tìm thấy ActionMap 'Player' trong InputActionAsset!");
            return;
        }

        _moveAction       = playerMap.FindAction("Move");
        _sprintAction     = playerMap.FindAction("Sprint");
        _jumpAction       = playerMap.FindAction("Jump");
        _crouchAction     = playerMap.FindAction("Crouch");
        _interactAction   = playerMap.FindAction("Interact");
        _pauseAction      = playerMap.FindAction("Pause");
        _cameraLookAction = playerMap.FindAction("CameraLook");
        _dashAction       = playerMap.FindAction("Dash");
        _attackAction     = playerMap.FindAction("Attack");
        _chatAction       = playerMap.FindAction("Chat");
        _voiceMuteAction  = playerMap.FindAction("VoiceMute");
        _stickerWheelAction = playerMap.FindAction("StickerWheel");
        _stickerNavigateAction = playerMap.FindAction("StickerNavigate");
        _stickerPreviousSetAction = playerMap.FindAction("StickerPreviousSet");
        _stickerNextSetAction = playerMap.FindAction("StickerNextSet");
        _stickerCancelAction = playerMap.FindAction("StickerCancel");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsOwner)
        {
            EnableInputActions();
            EventBus.OnCutSceneStarted += LockAllInput;
            EventBus.OnCutSceneEnded   += UnlockAllInput;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (IsOwner)
        {
            EventBus.OnCutSceneStarted -= LockAllInput;
            EventBus.OnCutSceneEnded   -= UnlockAllInput;
            DisableInputActions();
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        // CHẶN INPUT TRONG LOBBY
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("Lobby"))
        {
            ReadMenuInput();
            ClearGameplayInput();
            return;
        }

        if (_inputLocked)
        {
            ReadMenuInput();
            ClearGameplayInput();
            return;
        }

        ReadInput();
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner) return;

        // Reset consumed properties sau mỗi frame
        InteractPressed = false;
        PausePressed    = false;
        AttackPressed   = false;
        ChatPressed     = false;
        VoiceMutePressed = false;
        StickerPreviousSetPressed = false;
        StickerNextSetPressed = false;
        StickerCancelPressed = false;
    }

    // ─── Input Reading ───────────────────────────────────────────────────────

    private void ReadInput()
    {
        // Movement
        MoveInput   = _moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
        IsMoving    = MoveInput.sqrMagnitude > 0.01f;
        IsSprinting = _sprintAction?.IsPressed() ?? false;
        IsCrouching = _crouchAction?.IsPressed() ?? false;

        // Actions (consumed — chỉ true 1 frame)
        if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
        {
            _jumpBufferTimer = _jumpBufferDuration;
        }

        if (_jumpBufferTimer > 0f)
        {
            _jumpBufferTimer -= Time.deltaTime;
        }
        JumpPressed = _jumpBufferTimer > 0f;

        JumpHeld = _jumpAction?.IsPressed() ?? false;

        if (_interactAction != null && _interactAction.WasPressedThisFrame())
            InteractPressed = true;

        if (_pauseAction != null && _pauseAction.WasPressedThisFrame())
            PausePressed = true;

        // Dash buffer — giống jump buffer để không miss FixedUpdate frame
        if (_dashAction != null && _dashAction.WasPressedThisFrame())
        {
            _dashBufferTimer = _config != null ? _config.DashInputBuffer : 0.15f;
        }

        if (_dashBufferTimer > 0f)
        {
            _dashBufferTimer -= Time.deltaTime;
        }
        DashPressed = _dashBufferTimer > 0f;

        if (_attackAction != null && _attackAction.WasPressedThisFrame())
            AttackPressed = true;

        ReadMenuInput();

        // Camera sensitivity follows the active input device.
        var rawLook = _cameraLookAction?.ReadValue<Vector2>() ?? Vector2.zero;
        if (CameraLookEnabled)
        {
            bool isGamepad = InputDeviceDetector.Instance != null
                && InputDeviceDetector.Instance.CurrentDeviceType == InputDeviceType.Gamepad;
            float sensitivity = isGamepad ? _gamepadCameraSensitivity : _mouseCameraSensitivity;
            CameraLookDelta = rawLook * sensitivity;
        }
        else
        {
            CameraLookDelta = Vector2.zero;
        }
    }

    private void ReadMenuInput()
    {
        ChatPressed = _chatAction?.WasPressedThisFrame() ?? false;
        VoiceMutePressed = _voiceMuteAction?.WasPressedThisFrame() ?? false;
        StickerWheelHeld = _stickerWheelAction?.IsPressed() ?? false;
        StickerNavigateInput = _stickerNavigateAction?.ReadValue<Vector2>() ?? Vector2.zero;
        StickerPreviousSetPressed = _stickerPreviousSetAction?.WasPressedThisFrame() ?? false;
        StickerNextSetPressed = _stickerNextSetAction?.WasPressedThisFrame() ?? false;
        StickerCancelPressed = _stickerCancelAction?.WasPressedThisFrame() ?? false;
    }

    private void ClearGameplayInput()
    {
        MoveInput        = Vector2.zero;
        IsMoving         = false;
        IsSprinting      = false;
        IsCrouching      = false;
        JumpPressed      = false;
        JumpHeld         = false;
        _jumpBufferTimer = 0f;
        _dashBufferTimer = 0f;
        DashPressed      = false;
        InteractPressed  = false;
        PausePressed     = false;
        AttackPressed    = false;
        CameraLookDelta  = Vector2.zero;
    }

    private void ClearAllInput()
    {
        ClearGameplayInput();
        ChatPressed = false;
        VoiceMutePressed = false;
        StickerWheelHeld = false;
        StickerNavigateInput = Vector2.zero;
        StickerPreviousSetPressed = false;
        StickerNextSetPressed = false;
        StickerCancelPressed = false;
    }

    // ─── Public Methods ──────────────────────────────────────────────────────

    /// <summary>Tắt CameraLook input. Gọi bởi CameraManager khi switch sang camera đặc biệt.</summary>
    public void DisableCameraLook()
    {
        CameraLookEnabled = false;
    }

    /// <summary>Bật lại CameraLook input. Gọi bởi CameraManager khi về ThirdPerson.</summary>
    public void EnableCameraLook()
    {
        CameraLookEnabled = true;
    }

    /// <summary>Lock toàn bộ input. Gọi khi CutScene bắt đầu.</summary>
    public void LockAllInput()
    {
        _inputLocked = true;
        ClearAllInput();
    }

    /// <summary>Unlock input. Gọi khi CutScene kết thúc.</summary>
    public void UnlockAllInput()
    {
        _inputLocked = false;
    }

    /// <summary>
    /// Dùng cho code vật lý (FixedUpdate): đọc và consume jump buffered.
    /// </summary>
    public bool ConsumeJumpPressed()
    {
        if (_jumpBufferTimer <= 0f) return false;
        _jumpBufferTimer = 0f;
        JumpPressed = false;
        return true;
    }

    /// <summary>
    /// Dùng cho code vật lý (FixedUpdate): đọc và consume dash buffer.
    /// </summary>
    public bool ConsumeDashPressed()
    {
        if (_dashBufferTimer <= 0f) return false;
        _dashBufferTimer = 0f;
        DashPressed = false;
        return true;
    }

    // ─── Enable/Disable Actions ──────────────────────────────────────────────

    private void EnableInputActions()
    {
        _inputActions?.Enable();
    }

    private void DisableInputActions()
    {
        _inputActions?.Disable();
    }
}
