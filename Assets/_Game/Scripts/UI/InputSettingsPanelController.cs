using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// InputSettingsPanelController — Controller cho InputSettingsPanel (UI Toolkit).
/// SRP: Chỉ binding UI elements ↔ services. KHÔNG chứa business logic.
/// DIP: Phụ thuộc IInputRebindService và IInputIconProvider qua [SerializeField].
/// 
/// Panel lifecycle:
/// 1. Show() → build rows, refresh bindings, focus first element
/// 2. User interacts → delegate to services
/// 3. Hide() → cleanup
/// </summary>
public class InputSettingsPanelController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private InputRebindService _rebindService;
    [SerializeField] private InputIconMap _iconProvider;
    [SerializeField] private UIDocument _uiDocument;
    [SerializeField] private PlayerInputHandler _inputHandler;

    [Header("Fallback Toggle")]
    [SerializeField] private bool _enableKeyboardToggle = true;
    [SerializeField] private Key _keyboardToggleKey = Key.F10;
    [SerializeField] private bool _enableGamepadToggle = true;

    // Cached UI elements
    private VisualElement _root;
    private VisualElement _panelOverlay;
    private ScrollView _rebindList;
    private VisualElement _rebindOverlay;
    private Label _rebindPrompt;
    private Slider _sensitivitySlider;
    private Label _sensitivityValueLabel;
    private Button _btnModeAuto;
    private Button _btnModeKeyboard;
    private Button _btnModeGamepad;
    private Button _btnResetAll;
    private Button _btnBack;
    private Button _btnCancelRebind;

    private readonly List<RebindRowController> _rows = new();
    private bool _isVisible;
    private EventCallback<ClickEvent> _modeAutoClicked;
    private EventCallback<ClickEvent> _modeKeyboardClicked;
    private EventCallback<ClickEvent> _modeGamepadClicked;
    private EventCallback<ClickEvent> _resetAllClicked;
    private EventCallback<ClickEvent> _backClicked;
    private EventCallback<ClickEvent> _cancelRebindClicked;
    private EventCallback<NavigationCancelEvent> _navigationCancel;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        _uiDocument ??= GetComponent<UIDocument>();
        _rebindService ??= FindFirstObjectByType<InputRebindService>();
        _inputHandler ??= FindFirstObjectByType<PlayerInputHandler>();

        if (_uiDocument == null)
        {
            Debug.LogError("[InputSettingsPanelController] UIDocument chưa gán trong Inspector!");
            return;
        }

        if (_rebindService == null)
            Debug.LogError("[InputSettingsPanelController] InputRebindService chưa gán trong Inspector!");

        if (_iconProvider == null)
            Debug.LogError("[InputSettingsPanelController] InputIconMap chưa gán trong Inspector!");

        _modeAutoClicked = _ => OnDeviceModeClicked(0);
        _modeKeyboardClicked = _ => OnDeviceModeClicked(1);
        _modeGamepadClicked = _ => OnDeviceModeClicked(2);
        _resetAllClicked = _ => OnResetAllClicked();
        _backClicked = _ => Hide();
        _cancelRebindClicked = _ => OnCancelRebindClicked();
        _navigationCancel = _ => Hide();
    }

    private void OnEnable()
    {
        EventBus.OnInputDeviceChanged += OnDeviceChanged;
        EventBus.OnInputBindingChanged += RefreshAllBindings;

        // Bind UI sau khi UIDocument loaded
        _root = _uiDocument.rootVisualElement;
        if (_root == null) return;

        CacheUIElements();
        BindCallbacks();

        // Mặc định ẩn panel
        Hide();
    }

    private void OnDisable()
    {
        EventBus.OnInputDeviceChanged -= OnDeviceChanged;
        EventBus.OnInputBindingChanged -= RefreshAllBindings;
        UnbindCallbacks();
    }

    private void Update()
    {
        if (_enableKeyboardToggle && Keyboard.current != null && Keyboard.current[_keyboardToggleKey].wasPressedThisFrame)
        {
            Toggle();
            return;
        }

        if (_enableGamepadToggle && Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame)
            Toggle();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public bool IsVisible => _isVisible;

    /// <summary>Mở InputSettings panel.</summary>
    public void Show()
    {
        if (_panelOverlay == null) return;

        _isVisible = true;
        _panelOverlay.style.display = DisplayStyle.Flex;

        BuildRebindRows();
        RefreshAllBindings();
        RefreshDeviceModeButtons();
        RefreshSensitivitySlider();

        // Focus first rebind button cho gamepad navigation
        if (_rows.Count > 0)
        {
            // Delay 1 frame để UI Toolkit build xong
            _root.schedule.Execute(() => _btnModeAuto?.Focus()).ExecuteLater(50);
        }
    }

    /// <summary>Đóng InputSettings panel.</summary>
    public void Hide()
    {
        _isVisible = false;
        if (_panelOverlay != null)
            _panelOverlay.style.display = DisplayStyle.None;
        HideRebindOverlay();

        // Cancel rebind nếu đang chờ
        if (_rebindService != null && _rebindService.IsRebinding)
            _rebindService.CancelRebind();
    }

    /// <summary>Toggle panel visibility.</summary>
    public void Toggle()
    {
        if (_isVisible) Hide();
        else Show();
    }

    // ─── UI Binding ───────────────────────────────────────────────────────────

    private void CacheUIElements()
    {
        _panelOverlay = _root.Q<VisualElement>("panel-overlay");
        _rebindList = _root.Q<ScrollView>("rebind-list");
        _rebindOverlay = _root.Q<VisualElement>("rebind-overlay");
        _rebindPrompt = _root.Q<Label>("rebind-prompt");
        _sensitivitySlider = _root.Q<Slider>("gamepad-sensitivity");
        _sensitivityValueLabel = _root.Q<Label>("sensitivity-value");
        _btnModeAuto = _root.Q<Button>("btn-mode-auto");
        _btnModeKeyboard = _root.Q<Button>("btn-mode-keyboard");
        _btnModeGamepad = _root.Q<Button>("btn-mode-gamepad");
        _btnResetAll = _root.Q<Button>("btn-reset-all");
        _btnBack = _root.Q<Button>("btn-back");
        _btnCancelRebind = _root.Q<Button>("btn-cancel-rebind");
    }

    private void BindCallbacks()
    {
        _btnModeAuto?.RegisterCallback(_modeAutoClicked);
        _btnModeKeyboard?.RegisterCallback(_modeKeyboardClicked);
        _btnModeGamepad?.RegisterCallback(_modeGamepadClicked);
        _btnResetAll?.RegisterCallback(_resetAllClicked);
        _btnBack?.RegisterCallback(_backClicked);
        _btnCancelRebind?.RegisterCallback(_cancelRebindClicked);

        if (_sensitivitySlider != null)
            _sensitivitySlider.RegisterValueChangedCallback(OnSensitivityChanged);

        // Gamepad B button = back (NavigationCancelEvent)
        _panelOverlay?.RegisterCallback(_navigationCancel);
    }

    private void UnbindCallbacks()
    {
        _btnModeAuto?.UnregisterCallback(_modeAutoClicked);
        _btnModeKeyboard?.UnregisterCallback(_modeKeyboardClicked);
        _btnModeGamepad?.UnregisterCallback(_modeGamepadClicked);
        _btnResetAll?.UnregisterCallback(_resetAllClicked);
        _btnBack?.UnregisterCallback(_backClicked);
        _btnCancelRebind?.UnregisterCallback(_cancelRebindClicked);

        if (_sensitivitySlider != null)
            _sensitivitySlider.UnregisterValueChangedCallback(OnSensitivityChanged);

        _panelOverlay?.UnregisterCallback(_navigationCancel);
    }

    // ─── Build Rows ───────────────────────────────────────────────────────────

    private void BuildRebindRows()
    {
        _rebindList.Clear();
        _rows.Clear();
        RebindRowController.ResetTabIndex();

        if (_rebindService == null) return;

        var actionNames = _rebindService.GetRebindableActionNames();
        for (int i = 0; i < actionNames.Count; i++)
        {
            var row = new RebindRowController(
                actionNames[i],
                _rebindList,
                OnRebindClicked,
                isAlt: i % 2 == 1
            );
            _rows.Add(row);
        }
    }

    // ─── Event Handlers ───────────────────────────────────────────────────────

    private void OnRebindClicked(string actionName, InputDeviceType deviceType)
    {
        if (_rebindService == null || _rebindService.IsRebinding) return;

        // Show rebind overlay
        ShowRebindOverlay(actionName, deviceType);

        // Tìm row và set rebinding state
        var row = _rows.Find(r => r.ActionName == actionName);
        row?.SetRebindingState(deviceType, true);

        _rebindService.StartRebind(actionName, deviceType,
            onComplete: (success, newKey) =>
            {
                HideRebindOverlay();
                row?.SetRebindingState(deviceType, false);
                RefreshAllBindings();
            },
            onConflict: (conflictAction) =>
            {
                // Hiện warning trong prompt
                if (_rebindPrompt != null)
                    _rebindPrompt.text = $"Conflict with '{conflictAction}'!\nPress another key...";

                row?.SetRebindingState(deviceType, false);
                HideRebindOverlay();
                RefreshAllBindings();
            });
    }

    private void OnResetAllClicked()
    {
        _rebindService?.ResetAllBindings();
        RefreshAllBindings();
    }

    private void OnCancelRebindClicked()
    {
        _rebindService?.CancelRebind();
        HideRebindOverlay();
    }

    private void OnDeviceModeClicked(int mode)
    {
        InputDeviceDetector.Instance?.SetPreferredDevice(mode);
        RefreshDeviceModeButtons();
    }

    private void OnSensitivityChanged(ChangeEvent<float> evt)
    {
        if (_sensitivityValueLabel != null)
            _sensitivityValueLabel.text = $"{Mathf.RoundToInt(evt.newValue * 100)}%";

        _inputHandler ??= FindFirstObjectByType<PlayerInputHandler>();
        if (_inputHandler != null)
            _inputHandler.GamepadCameraSensitivity = evt.newValue;
    }

    private void OnDeviceChanged(InputDeviceType _)
    {
        if (_isVisible) RefreshAllBindings();
    }

    // ─── Refresh UI ───────────────────────────────────────────────────────────

    private void RefreshAllBindings()
    {
        if (_rebindService == null) return;

        foreach (var row in _rows)
            row.Refresh(_rebindService, _iconProvider);
    }

    private void RefreshDeviceModeButtons()
    {
        int mode = InputDeviceDetector.Instance?.GetPreferredDevice() ?? 0;

        SetModeButtonActive(_btnModeAuto, mode == 0);
        SetModeButtonActive(_btnModeKeyboard, mode == 1);
        SetModeButtonActive(_btnModeGamepad, mode == 2);
    }

    private void SetModeButtonActive(Button btn, bool active)
    {
        if (btn == null) return;
        if (active)
            btn.AddToClassList("mode-active");
        else
            btn.RemoveFromClassList("mode-active");
    }

    private void RefreshSensitivitySlider()
    {
        if (_sensitivitySlider != null)
        {
            _inputHandler ??= FindFirstObjectByType<PlayerInputHandler>();
            float value = _inputHandler != null ? _inputHandler.GamepadCameraSensitivity : _sensitivitySlider.value;
            _sensitivitySlider.SetValueWithoutNotify(value);

            if (_sensitivityValueLabel != null)
                _sensitivityValueLabel.text = $"{Mathf.RoundToInt(value * 100)}%";
        }
    }

    // ─── Rebind Overlay ───────────────────────────────────────────────────────

    private void ShowRebindOverlay(string actionName, InputDeviceType deviceType)
    {
        if (_rebindOverlay == null) return;

        string deviceLabel = deviceType == InputDeviceType.Gamepad ? "gamepad button" : "key";
        if (_rebindPrompt != null)
            _rebindPrompt.text = $"Press any {deviceLabel} for '{actionName}'...";

        _rebindOverlay.RemoveFromClassList("hidden");
        _root.schedule.Execute(() => _btnCancelRebind?.Focus()).ExecuteLater(50);
    }

    private void HideRebindOverlay()
    {
        _rebindOverlay?.AddToClassList("hidden");
    }
}
