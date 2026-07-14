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
    [SerializeField] private bool _enableKeyboardToggle = false;
    [SerializeField] private Key _keyboardToggleKey = Key.F10;
    [SerializeField] private bool _enableGamepadToggle = false;
    [SerializeField] private bool _lockPlayerInputWhileOpen = true;

    // Cached UI elements
    private VisualElement _root;
    private VisualElement _panelOverlay;
    private ScrollView _rebindList;
    private VisualElement _rebindOverlay;
    private VisualElement _conflictOverlay;
    private Label _rebindPrompt;
    private Label _conflictPrompt;
    private Slider _sensitivitySlider;
    private Label _sensitivityValueLabel;
    private Button _btnModeAuto;
    private Button _btnModeKeyboard;
    private Button _btnModeGamepad;
    private Button _btnResetAll;
    private Button _btnBack;
    private Button _btnCancelRebind;
    private Button _btnConfirmConflict;
    private Button _btnCancelConflict;

    private readonly List<RebindRowController> _rows = new();
    private bool _isVisible;
    private EventCallback<ClickEvent> _modeAutoClicked;
    private EventCallback<ClickEvent> _modeKeyboardClicked;
    private EventCallback<ClickEvent> _modeGamepadClicked;
    private EventCallback<ClickEvent> _resetAllClicked;
    private EventCallback<ClickEvent> _backClicked;
    private EventCallback<ClickEvent> _cancelRebindClicked;
    private EventCallback<ClickEvent> _confirmConflictClicked;
    private EventCallback<ClickEvent> _cancelConflictClicked;
    private EventCallback<NavigationCancelEvent> _navigationCancel;
    private RebindRowController _activeRow;
    private InputBindingTarget _activeTarget;
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
        _confirmConflictClicked = _ => OnConfirmConflictClicked();
        _cancelConflictClicked = _ => OnCancelConflictClicked();
        _navigationCancel = _ => OnNavigationCancel();
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
    public bool IsRebinding => _rebindService != null && _rebindService.IsRebinding;

    /// <summary>Mở InputSettings panel.</summary>
    public void Show()
    {
        if (_panelOverlay == null) return;

        _isVisible = true;
        _panelOverlay.RemoveFromClassList("hidden");
        _panelOverlay.style.display = DisplayStyle.Flex;
        UICursorLockService.Request(this);
        SetPlayerInputLocked(true);

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
        {
            _panelOverlay.style.display = DisplayStyle.None;
            _panelOverlay.AddToClassList("hidden");
        }
        HideRebindOverlay();
        HideConflictOverlay();
        UICursorLockService.Release(this);
        SetPlayerInputLocked(false);

        // Cancel rebind nếu đang chờ
        if (_rebindService != null && _rebindService.IsRebinding)
            _rebindService.CancelRebind();

        _rebindService?.DiscardPendingConflict();
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
        _conflictOverlay = _root.Q<VisualElement>("conflict-overlay");
        _rebindPrompt = _root.Q<Label>("rebind-prompt");
        _conflictPrompt = _root.Q<Label>("conflict-prompt");
        _sensitivitySlider = _root.Q<Slider>("gamepad-sensitivity");
        _sensitivityValueLabel = _root.Q<Label>("sensitivity-value");
        _btnModeAuto = _root.Q<Button>("btn-mode-auto");
        _btnModeKeyboard = _root.Q<Button>("btn-mode-keyboard");
        _btnModeGamepad = _root.Q<Button>("btn-mode-gamepad");
        _btnResetAll = _root.Q<Button>("btn-reset-all");
        _btnBack = _root.Q<Button>("btn-back");
        _btnCancelRebind = _root.Q<Button>("btn-cancel-rebind");
        _btnConfirmConflict = _root.Q<Button>("btn-confirm-conflict");
        _btnCancelConflict = _root.Q<Button>("btn-cancel-conflict");
    }

    private void BindCallbacks()
    {
        _btnModeAuto?.RegisterCallback(_modeAutoClicked);
        _btnModeKeyboard?.RegisterCallback(_modeKeyboardClicked);
        _btnModeGamepad?.RegisterCallback(_modeGamepadClicked);
        _btnResetAll?.RegisterCallback(_resetAllClicked);
        _btnBack?.RegisterCallback(_backClicked);
        _btnCancelRebind?.RegisterCallback(_cancelRebindClicked);
        _btnConfirmConflict?.RegisterCallback(_confirmConflictClicked);
        _btnCancelConflict?.RegisterCallback(_cancelConflictClicked);

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
        _btnConfirmConflict?.UnregisterCallback(_confirmConflictClicked);
        _btnCancelConflict?.UnregisterCallback(_cancelConflictClicked);

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

    private void OnRebindClicked(string actionName, InputBindingTarget target)
    {
        if (_rebindService == null || _rebindService.IsRebinding) return;

        _activeRow = _rows.Find(r => r.ActionName == actionName);
        _activeTarget = target;

        ShowRebindOverlay(actionName, target);
        _activeRow?.SetRebindingState(target, true);

        _rebindService.StartRebind(actionName, target,
            onComplete: (success, newKey) =>
            {
                HideRebindOverlay();
                _activeRow?.SetRebindingState(target, false);
                RefreshAllBindings();
                _activeRow?.Focus(target);
            },
            onConflict: (conflict) =>
            {
                _activeRow?.SetRebindingState(target, false);
                HideRebindOverlay();
                RefreshAllBindings();
                ShowConflictOverlay(conflict);
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
        _activeRow?.SetRebindingState(_activeTarget, false);
        _activeRow?.Focus(_activeTarget);
    }

    private void OnConfirmConflictClicked()
    {
        _rebindService?.ApplyPendingConflict();
        HideConflictOverlay();
        RefreshAllBindings();
        _activeRow?.Focus(_activeTarget);
    }

    private void OnCancelConflictClicked()
    {
        _rebindService?.DiscardPendingConflict();
        HideConflictOverlay();
        RefreshAllBindings();
        _activeRow?.Focus(_activeTarget);
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
            row.Refresh(_rebindService);
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

    private void ShowRebindOverlay(string actionName, InputBindingTarget target)
    {
        if (_rebindOverlay == null) return;

        string deviceLabel = target switch
        {
            InputBindingTarget.Gamepad => "gamepad button",
            InputBindingTarget.Mouse => "mouse button",
            _ => "keyboard key"
        };
        if (_rebindPrompt != null)
            _rebindPrompt.text = $"Press any {deviceLabel} for '{actionName}'\nEsc / Menu: Cancel";

        _rebindOverlay.RemoveFromClassList("hidden");
        _root.schedule.Execute(() => _btnCancelRebind?.Focus()).ExecuteLater(50);
    }

    private void HideRebindOverlay()
    {
        _rebindOverlay?.AddToClassList("hidden");
    }

    private void ShowConflictOverlay(InputRebindConflict conflict)
    {
        if (_conflictOverlay == null) return;

        if (_conflictPrompt != null)
        {
            _conflictPrompt.text =
                $"'{conflict.BindingDisplayName}' is already assigned to '{conflict.ConflictActionName}'.\n" +
                $"Swap it with '{conflict.ActionName}'?";
        }

        _conflictOverlay.RemoveFromClassList("hidden");
        _root.schedule.Execute(() => _btnConfirmConflict?.Focus()).ExecuteLater(50);
    }

    private void HideConflictOverlay()
    {
        _conflictOverlay?.AddToClassList("hidden");
    }

    private void OnNavigationCancel()
    {
        if (_rebindService != null && _rebindService.IsRebinding)
        {
            OnCancelRebindClicked();
            return;
        }

        if (_rebindService != null && _rebindService.HasPendingConflict)
        {
            OnCancelConflictClicked();
            return;
        }

        Hide();
    }

    private void SetPlayerInputLocked(bool locked)
    {
        if (!_lockPlayerInputWhileOpen) return;

        foreach (var handler in FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None))
        {
            if (!handler.IsOwner) continue;

            if (locked)
                handler.LockAllInput();
            else
                handler.UnlockAllInput();
        }
    }
}
