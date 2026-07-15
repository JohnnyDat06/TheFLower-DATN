using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit controller for the Elden Ring-style input binding panel.
/// It only coordinates UI state and delegates binding logic to InputRebindService.
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

    private VisualElement _root;
    private VisualElement _panelOverlay;
    private ScrollView _rebindList;
    private VisualElement _rebindOverlay;
    private VisualElement _conflictOverlay;
    private VisualElement _gamepadMap;
    private Label _rebindPrompt;
    private Label _conflictPrompt;
    private Label _keyboardHeader;
    private Label _mouseHeader;
    private Label _gamepadHeader;
    private Label _sensitivityValueLabel;
    private Label _footerOkHint;
    private Label _footerBackHint;
    private Label _footerCancelHint;
    private Label _tabSwitchHint;
    private Slider _sensitivitySlider;
    private Button _btnModeKeyboard;
    private Button _btnModeGamepad;
    private Button _btnResetAll;
    private Button _btnBack;
    private Button _btnCancelRebind;
    private Button _btnConfirmConflict;
    private Button _btnCancelConflict;

    private readonly List<RebindRowController> _rows = new();
    private readonly Dictionary<PlayerInputHandler, bool> _cameraLookStates = new();
    private bool _isVisible;
    private bool _showGamepadTab;
    private bool _managesPlayerInput;
    private Action _onClosed;
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

    private static readonly InputBindingTarget[] KeyboardMouseTargets =
    {
        InputBindingTarget.Keyboard,
        InputBindingTarget.Mouse
    };

    private static readonly InputBindingTarget[] GamepadTargets =
    {
        InputBindingTarget.Gamepad
    };

    public bool IsVisible => _isVisible;
    public bool IsInitialized => _panelOverlay != null;
    public bool IsRebinding => _rebindService != null && _rebindService.IsRebinding;

    public void Configure(InputRebindService rebindService, InputIconMap iconProvider, UIDocument uiDocument)
    {
        _rebindService = rebindService;
        _iconProvider = iconProvider;
        _uiDocument = uiDocument;
    }

    private void Awake()
    {
        _uiDocument ??= GetComponent<UIDocument>();
        _rebindService ??= FindFirstObjectByType<InputRebindService>();
        _inputHandler ??= FindFirstObjectByType<PlayerInputHandler>();

        if (_uiDocument == null)
        {
            Debug.LogError("[InputSettingsPanelController] UIDocument is not assigned.");
            enabled = false;
            return;
        }

        if (_rebindService == null)
            Debug.LogError("[InputSettingsPanelController] InputRebindService is not assigned.");

        if (_iconProvider == null)
            Debug.LogWarning("[InputSettingsPanelController] InputIconMap is not assigned. Binding text will still work.");

        _modeKeyboardClicked = _ => SelectDeviceTab(false);
        _modeGamepadClicked = _ => SelectDeviceTab(true);
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

        _root = _uiDocument.rootVisualElement;
        if (_root == null) return;

        CacheUIElements();
        BindCallbacks();
        Hide();
    }

    private void OnDisable()
    {
        EventBus.OnInputDeviceChanged -= OnDeviceChanged;
        EventBus.OnInputBindingChanged -= RefreshAllBindings;
        UnbindCallbacks();

        if (_isVisible)
        {
            _isVisible = false;
            UICursorLockService.Release(this);
            if (_managesPlayerInput)
                SetPlayerInputLocked(false);
            _managesPlayerInput = false;
        }
    }

    private void Update()
    {
        if (_isVisible && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (IsRebinding)
                OnCancelRebindClicked();
            else
                Hide();
            return;
        }

        if (_enableKeyboardToggle && Keyboard.current != null && Keyboard.current[_keyboardToggleKey].wasPressedThisFrame)
        {
            Toggle();
            return;
        }

        if (_enableGamepadToggle && Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame)
        {
            Toggle();
            return;
        }

        if (_isVisible)
            HandleGamepadTabSwitch();
    }

    public void Show(Action onClosed = null, bool managePlayerInput = true)
    {
        if (_panelOverlay == null && !TryInitializeVisualTree())
        {
            Debug.LogError("[InputSettingsPanelController] The input settings visual tree could not be initialized.");
            return;
        }

        _onClosed = onClosed;
        _managesPlayerInput = managePlayerInput;
        _showGamepadTab = IsCurrentDeviceGamepad();
        _isVisible = true;
        _panelOverlay.RemoveFromClassList("hidden");
        _panelOverlay.style.display = DisplayStyle.Flex;
        UICursorLockService.Request(this);
        if (_managesPlayerInput)
            SetPlayerInputLocked(true);

        RefreshDeviceModeButtons();
        BuildRebindRows();
        RefreshAllBindings();
        RefreshSensitivitySlider();
        RefreshInputHints();

        _root.schedule.Execute(FocusFirstBinding).ExecuteLater(50);
    }

    public bool TryInitializeVisualTree()
    {
        _uiDocument ??= GetComponent<UIDocument>();
        if (_uiDocument == null || _uiDocument.rootVisualElement == null)
            return false;

        UnbindCallbacks();
        _root = _uiDocument.rootVisualElement;
        CacheUIElements();
        BindCallbacks();
        return _panelOverlay != null;
    }

    public void Hide()
    {
        bool wasVisible = _isVisible;
        _isVisible = false;

        if (_panelOverlay != null)
        {
            _panelOverlay.style.display = DisplayStyle.None;
            _panelOverlay.AddToClassList("hidden");
        }

        HideRebindOverlay();
        HideConflictOverlay();

        if (!wasVisible) return;

        UICursorLockService.Release(this);
        if (_managesPlayerInput)
            SetPlayerInputLocked(false);
        _managesPlayerInput = false;

        if (_rebindService != null && _rebindService.IsRebinding)
            _rebindService.CancelRebind();

        _rebindService?.DiscardPendingConflict();

        var onClosed = _onClosed;
        _onClosed = null;
        onClosed?.Invoke();
    }

    public void Toggle()
    {
        if (_isVisible) Hide();
        else Show();
    }

    private void CacheUIElements()
    {
        _panelOverlay = _root.Q<VisualElement>("panel-overlay");
        _rebindList = _root.Q<ScrollView>("rebind-list");
        _rebindOverlay = _root.Q<VisualElement>("rebind-overlay");
        _conflictOverlay = _root.Q<VisualElement>("conflict-overlay");
        _gamepadMap = _root.Q<VisualElement>("gamepad-map");
        _rebindPrompt = _root.Q<Label>("rebind-prompt");
        _conflictPrompt = _root.Q<Label>("conflict-prompt");
        _keyboardHeader = _root.Q<Label>("header-keyboard");
        _mouseHeader = _root.Q<Label>("header-mouse");
        _gamepadHeader = _root.Q<Label>("header-gamepad");
        _sensitivitySlider = _root.Q<Slider>("gamepad-sensitivity");
        _sensitivityValueLabel = _root.Q<Label>("sensitivity-value");
        _footerOkHint = _root.Q<Label>("footer-ok-hint");
        _footerBackHint = _root.Q<Label>("footer-back-hint");
        _footerCancelHint = _root.Q<Label>("footer-cancel-hint");
        _tabSwitchHint = _root.Q<Label>("tab-switch-hint");
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
        _btnModeKeyboard?.RegisterCallback(_modeKeyboardClicked);
        _btnModeGamepad?.RegisterCallback(_modeGamepadClicked);
        _btnResetAll?.RegisterCallback(_resetAllClicked);
        _btnBack?.RegisterCallback(_backClicked);
        _btnCancelRebind?.RegisterCallback(_cancelRebindClicked);
        _btnConfirmConflict?.RegisterCallback(_confirmConflictClicked);
        _btnCancelConflict?.RegisterCallback(_cancelConflictClicked);
        _sensitivitySlider?.RegisterValueChangedCallback(OnSensitivityChanged);
        _panelOverlay?.RegisterCallback(_navigationCancel);
        _gamepadMap?.RegisterCallback<ClickEvent>(OnPadBindingClicked);
    }

    private void UnbindCallbacks()
    {
        _btnModeKeyboard?.UnregisterCallback(_modeKeyboardClicked);
        _btnModeGamepad?.UnregisterCallback(_modeGamepadClicked);
        _btnResetAll?.UnregisterCallback(_resetAllClicked);
        _btnBack?.UnregisterCallback(_backClicked);
        _btnCancelRebind?.UnregisterCallback(_cancelRebindClicked);
        _btnConfirmConflict?.UnregisterCallback(_confirmConflictClicked);
        _btnCancelConflict?.UnregisterCallback(_cancelConflictClicked);
        _sensitivitySlider?.UnregisterValueChangedCallback(OnSensitivityChanged);
        _panelOverlay?.UnregisterCallback(_navigationCancel);
        _gamepadMap?.UnregisterCallback<ClickEvent>(OnPadBindingClicked);
    }

    private void BuildRebindRows()
    {
        _rebindList?.Clear();
        _rows.Clear();
        RebindRowController.ResetTabIndex();

        if (_rebindService == null || _rebindList == null) return;

        var targets = _showGamepadTab ? GamepadTargets : KeyboardMouseTargets;
        var actionNames = _rebindService.GetRebindableActionNames();
        for (int i = 0; i < actionNames.Count; i++)
        {
            _rows.Add(new RebindRowController(
                actionNames[i],
                _rebindList,
                OnRebindClicked,
                targets,
                isAlt: i % 2 == 1
            ));
        }
    }

    private void OnRebindClicked(string actionName, InputBindingTarget target)
    {
        if (_rebindService == null || _rebindService.IsRebinding) return;

        _activeRow = _rows.Find(r => r.ActionName == actionName);
        _activeTarget = target;

        ShowRebindOverlay(actionName, target);
        _activeRow?.SetRebindingState(target, true);

        _rebindService.StartRebind(actionName, target,
            onComplete: (_, _) =>
            {
                HideRebindOverlay();
                _activeRow?.SetRebindingState(target, false);
                RefreshAllBindings();
                _activeRow?.Focus(target);
            },
            onConflict: conflict =>
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

    private void SelectDeviceTab(bool gamepad)
    {
        if (_showGamepadTab == gamepad) return;

        _showGamepadTab = gamepad;
        InputDeviceDetector.Instance?.SetPreferredDevice(gamepad ? 2 : 1);
        RefreshDeviceModeButtons();
        BuildRebindRows();
        RefreshAllBindings();
        RefreshInputHints();
        _root.schedule.Execute(FocusFirstBinding).ExecuteLater(50);
    }

    private void HandleGamepadTabSwitch()
    {
        if (_rebindService != null && (_rebindService.IsRebinding || _rebindService.HasPendingConflict))
            return;

        var gamepad = Gamepad.current;
        if (gamepad == null) return;

        if (gamepad.leftShoulder.wasPressedThisFrame)
            SelectDeviceTab(false);
        else if (gamepad.rightShoulder.wasPressedThisFrame)
            SelectDeviceTab(true);
    }

    private void OnSensitivityChanged(ChangeEvent<float> evt)
    {
        if (_sensitivityValueLabel != null)
            _sensitivityValueLabel.text = $"{Mathf.RoundToInt(evt.newValue * 100)}%";

        PlayerPrefs.SetFloat(Constants.PlayerPrefsKeys.GAMEPAD_CAMERA_SENSITIVITY, evt.newValue);
        PlayerPrefs.Save();

        _inputHandler ??= FindFirstObjectByType<PlayerInputHandler>();
        if (_inputHandler != null)
            _inputHandler.GamepadCameraSensitivity = evt.newValue;
    }

    private void OnDeviceChanged(InputDeviceType deviceType)
    {
        if (!_isVisible) return;

        RefreshInputHints();
        if ((_showGamepadTab && deviceType == InputDeviceType.Gamepad)
            || (!_showGamepadTab && deviceType == InputDeviceType.KeyboardMouse))
            RefreshAllBindings();
    }

    private void RefreshAllBindings()
    {
        if (_rebindService == null) return;

        foreach (var row in _rows)
            row.Refresh(_rebindService);

        RefreshGamepadDiagram();
    }

    private void OnPadBindingClicked(ClickEvent evt)
    {
        if (!_showGamepadTab || _rebindService == null || _rebindService.IsRebinding || _rebindService.HasPendingConflict)
            return;

        if (evt.target is not Button button) return;

        string actionName = GetDiagramActionName(button.name);
        if (!string.IsNullOrEmpty(actionName))
            OnRebindClicked(actionName, InputBindingTarget.Gamepad);
    }

    private void RefreshGamepadDiagram()
    {
        if (_gamepadMap == null || _rebindService == null) return;

        _gamepadMap.Query<Button>(className: "pad-binding").ForEach(button =>
        {
            string actionName = GetDiagramActionName(button.name);
            if (string.IsNullOrEmpty(actionName)) return;

            string binding = _rebindService.GetBindingDisplayName(actionName, InputBindingTarget.Gamepad);
            button.text = $"{GetPhysicalControlName(button.name)}\n{GetFriendlyDiagramActionName(actionName)}\n[{binding}]";
        });
    }

    private static string GetDiagramActionName(string elementName) => elementName switch
    {
        "pad-dpad-left" => "Previous",
        "pad-dpad-right" => "Next",
        "pad-y" => "Interact",
        "pad-x" => "Attack",
        "pad-b" => "Crouch",
        "pad-a" => "Jump",
        "pad-left-stick" => "Sprint",
        _ => string.Empty
    };

    private static string GetPhysicalControlName(string elementName) => elementName switch
    {
        "pad-dpad-left" => "D-Pad Left",
        "pad-dpad-right" => "D-Pad Right",
        "pad-left-stick" => "LS Click",
        "pad-y" => "Y",
        "pad-x" => "X",
        "pad-b" => "B",
        "pad-a" => "A",
        _ => "Gamepad"
    };

    private static string GetFriendlyDiagramActionName(string actionName) => actionName switch
    {
        "Previous" => "Previous Item",
        "Next" => "Next Item",
        "Crouch" => "Crouch / Dodge",
        _ => actionName
    };

    private void RefreshDeviceModeButtons()
    {
        SetModeButtonActive(_btnModeKeyboard, !_showGamepadTab);
        SetModeButtonActive(_btnModeGamepad, _showGamepadTab);
        SetElementVisible(_keyboardHeader, !_showGamepadTab);
        SetElementVisible(_mouseHeader, !_showGamepadTab);
        SetElementVisible(_gamepadHeader, _showGamepadTab);
        SetElementVisible(_gamepadMap, _showGamepadTab);
        RefreshGamepadDiagram();

        // This is a shared saved setting, so it can be configured from either tab.
        _sensitivitySlider?.SetEnabled(true);
    }

    private static void SetModeButtonActive(Button btn, bool active)
    {
        if (btn == null) return;
        if (active)
            btn.AddToClassList("mode-active");
        else
            btn.RemoveFromClassList("mode-active");
    }

    private static void SetElementVisible(VisualElement element, bool visible)
    {
        if (element == null) return;
        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void RefreshSensitivitySlider()
    {
        if (_sensitivitySlider == null) return;

        _inputHandler ??= FindFirstObjectByType<PlayerInputHandler>();
        float value = _inputHandler != null
            ? _inputHandler.GamepadCameraSensitivity
            : PlayerPrefs.GetFloat(
                Constants.PlayerPrefsKeys.GAMEPAD_CAMERA_SENSITIVITY,
                _sensitivitySlider.value);
        _sensitivitySlider.SetValueWithoutNotify(value);

        if (_sensitivityValueLabel != null)
            _sensitivityValueLabel.text = $"{Mathf.RoundToInt(value * 100)}%";
    }

    private void RefreshInputHints()
    {
        bool gamepad = IsCurrentDeviceGamepad();
        if (_footerOkHint != null)
            _footerOkHint.text = gamepad ? "A : OK" : "Enter / Click : OK";
        if (_footerBackHint != null)
            _footerBackHint.text = gamepad ? "B : Back" : "Esc : Back";
        if (_footerCancelHint != null)
            _footerCancelHint.text = gamepad ? "Menu : Cancel rebind" : "Esc : Cancel rebind";
        if (_tabSwitchHint != null)
            _tabSwitchHint.text = gamepad ? "LB / RB (L1 / R1) : Switch tab" : "Click tab to switch";
    }

    private void ShowRebindOverlay(string actionName, InputBindingTarget target)
    {
        if (_rebindOverlay == null) return;

        string cancelHint = target == InputBindingTarget.Gamepad ? "Menu: Cancel" : "Esc: Cancel";
        string deviceLabel = target switch
        {
            InputBindingTarget.Gamepad => "gamepad button",
            InputBindingTarget.Mouse => "mouse button",
            _ => "keyboard key"
        };

        if (_rebindPrompt != null)
            _rebindPrompt.text = $"Press any {deviceLabel} for '{actionName}'\n{cancelHint}";

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

    private void FocusFirstBinding()
    {
        if (_rows.Count == 0) return;

        _rows[0].Focus(_showGamepadTab ? InputBindingTarget.Gamepad : InputBindingTarget.Keyboard);
    }

    private static bool IsCurrentDeviceGamepad()
    {
        return InputDeviceDetector.Instance != null
            && InputDeviceDetector.Instance.CurrentDeviceType == InputDeviceType.Gamepad;
    }

    private void SetPlayerInputLocked(bool locked)
    {
        if (!_lockPlayerInputWhileOpen) return;

        foreach (var handler in FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None))
        {
            if (!handler.IsOwner) continue;

            if (locked)
            {
                if (!_cameraLookStates.ContainsKey(handler))
                    _cameraLookStates.Add(handler, handler.CameraLookEnabled);
                handler.LockAllInput();
                handler.DisableCameraLook();
            }
            else
            {
                handler.UnlockAllInput();
                if (_cameraLookStates.TryGetValue(handler, out bool wasEnabled) && wasEnabled)
                    handler.EnableCameraLook();
                else
                    handler.DisableCameraLook();

                _cameraLookStates.Remove(handler);
            }
        }
    }
}
