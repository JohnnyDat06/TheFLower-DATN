using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

/// <summary>
/// InputDeviceDetector — Singleton detect device đang active (Keyboard/Mouse hoặc Gamepad).
/// Đặt trên scene _Core, DontDestroyOnLoad.
/// 
/// SRP: Chỉ detect device và fire events. KHÔNG rebind, KHÔNG hiển thị UI.
/// 
/// Logic:
/// - InputSystem.onActionChange → phát hiện device từ control vừa triggered
/// - InputSystem.onDeviceChange → phát hiện gamepad connect/disconnect
/// - Cooldown 0.2s chống flicker khi user chạm cả 2 device
/// - Hỗ trợ "preferred device" từ PlayerPrefs (Auto/ForceKeyboard/ForceGamepad)
/// </summary>
[DefaultExecutionOrder(-300)]
public class InputDeviceDetector : MonoBehaviour, IInputDeviceDetector
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static InputDeviceDetector Instance { get; private set; }

    // ─── Constants ────────────────────────────────────────────────────────────

    private const float SWITCH_COOLDOWN = 0.2f;

    private const int PREFERRED_AUTO           = 0;
    private const int PREFERRED_FORCE_KEYBOARD = 1;
    private const int PREFERRED_FORCE_GAMEPAD  = 2;

    // ─── IInputDeviceDetector Implementation ──────────────────────────────────

    public InputDeviceType CurrentDeviceType
    {
        get => _currentDeviceType;
        private set
        {
            if (_currentDeviceType == value) return;
            _currentDeviceType = value;
            DeviceChanged?.Invoke(_currentDeviceType);
            EventBus.RaiseInputDeviceChanged(_currentDeviceType);

#if UNITY_EDITOR || DEBUG_BUILD
            Debug.Log($"[InputDeviceDetector] Device changed → {_currentDeviceType}");
#endif
        }
    }

    public event Action<InputDeviceType> DeviceChanged;

    public bool IsGamepadConnected => Gamepad.current != null;

    // ─── Private State ────────────────────────────────────────────────────────

    private InputDeviceType _currentDeviceType = InputDeviceType.KeyboardMouse;
    private float _lastSwitchTime;
    private int _preferredDevice;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Singleton pattern — destroy duplicates
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        
        PersistentSceneRoot.MarkDontDestroyOnLoad(transform);

        // Load preferred device setting
        _preferredDevice = PlayerPrefs.GetInt(Constants.PlayerPrefsKeys.INPUT_PREFERRED_DEVICE, PREFERRED_AUTO);

        // Set initial device type
        InitializeDeviceType();
    }

    private void OnEnable()
    {
        InputSystem.onActionChange += OnActionChanged;
        InputSystem.onDeviceChange += OnDeviceChanged;
    }

    private void OnDisable()
    {
        InputSystem.onActionChange -= OnActionChanged;
        InputSystem.onDeviceChange -= OnDeviceChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Set preferred device mode. Gọi từ Settings UI.
    /// 0 = Auto-Detect (default), 1 = Force Keyboard, 2 = Force Gamepad.
    /// </summary>
    public void SetPreferredDevice(int preference)
    {
        _preferredDevice = Mathf.Clamp(preference, 0, 2);
        PlayerPrefs.SetInt(Constants.PlayerPrefsKeys.INPUT_PREFERRED_DEVICE, _preferredDevice);
        PlayerPrefs.Save();

        // Apply forced device immediately
        switch (_preferredDevice)
        {
            case PREFERRED_FORCE_KEYBOARD:
                CurrentDeviceType = InputDeviceType.KeyboardMouse;
                break;
            case PREFERRED_FORCE_GAMEPAD:
                if (IsGamepadConnected)
                    CurrentDeviceType = InputDeviceType.Gamepad;
                break;
            // PREFERRED_AUTO: giữ nguyên, để auto-detect xử lý
        }
    }

    /// <summary>Trả giá trị preferred device hiện tại (0=Auto, 1=KB, 2=GP).</summary>
    public int GetPreferredDevice() => _preferredDevice;

    // ─── Private Logic ────────────────────────────────────────────────────────

    private void InitializeDeviceType()
    {
        switch (_preferredDevice)
        {
            case PREFERRED_FORCE_KEYBOARD:
                _currentDeviceType = InputDeviceType.KeyboardMouse;
                break;
            case PREFERRED_FORCE_GAMEPAD:
                _currentDeviceType = IsGamepadConnected
                    ? InputDeviceType.Gamepad
                    : InputDeviceType.KeyboardMouse;
                break;
            default: // Auto
                _currentDeviceType = IsGamepadConnected
                    ? InputDeviceType.Gamepad
                    : InputDeviceType.KeyboardMouse;
                break;
        }
    }

    /// <summary>
    /// Callback mỗi khi bất kỳ InputAction nào thay đổi trạng thái.
    /// Dùng để detect device nào vừa tạo input.
    /// </summary>
    private void OnActionChanged(object obj, InputActionChange change)
    {
        if (change != InputActionChange.ActionPerformed) return;

        // Nếu đang Force → bỏ qua auto-detect
        if (_preferredDevice != PREFERRED_AUTO) return;

        var action = obj as InputAction;
        if (action == null) return;

        var device = action.activeControl?.device;
        if (device == null) return;

        var newType = device is Gamepad
            ? InputDeviceType.Gamepad
            : InputDeviceType.KeyboardMouse;

        // Chỉ switch nếu khác type hiện tại VÀ đã qua cooldown
        if (newType == _currentDeviceType) return;
        if (Time.unscaledTime - _lastSwitchTime < SWITCH_COOLDOWN) return;

        _lastSwitchTime = Time.unscaledTime;
        CurrentDeviceType = newType;
    }

    /// <summary>
    /// Callback khi device được thêm/gỡ khỏi hệ thống.
    /// </summary>
    private void OnDeviceChanged(InputDevice device, InputDeviceChange change)
    {
        if (device is not Gamepad) return;

        switch (change)
        {
            case InputDeviceChange.Added:
#if UNITY_EDITOR || DEBUG_BUILD
                Debug.Log($"[InputDeviceDetector] Gamepad connected: {device.displayName}");
#endif
                // Nếu Auto mode và đang KB → không tự switch (chờ user bấm nút gamepad)
                break;

            case InputDeviceChange.Removed:
#if UNITY_EDITOR || DEBUG_BUILD
                Debug.Log($"[InputDeviceDetector] Gamepad disconnected: {device.displayName}");
#endif
                // Nếu đang dùng Gamepad → fallback về Keyboard
                if (_currentDeviceType == InputDeviceType.Gamepad && !IsGamepadConnected)
                {
                    CurrentDeviceType = InputDeviceType.KeyboardMouse;
                }
                break;
        }
    }
}
