using System.Collections.Generic;
using UnityEngine;
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

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_uiDocument == null)
        {
            Debug.LogError("[InputSettingsPanelController] UIDocument chưa gán trong Inspector!");
            return;
        }

        if (_rebindService == null)
            Debug.LogError("[InputSettingsPanelController] InputRebindService chưa gán trong Inspector!");

        if (_iconProvider == null)
            Debug.LogError("[InputSettingsPanelController] InputIconMap chưa gán trong Inspector!");
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

    // ─── Public API ───────────────────────────────────────────────────────────

    public bool IsVisible => _isVisible;

    /// <summary>Mở InputSettings panel.</summary>
    public void Show()
    {
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
            _root.schedule.Execute(() => _rows[0].Focus()).ExecuteLater(50);
        }
    }

    /// <summary>Đóng InputSettings panel.</summary>
    public void Hide()
    {
        _isVisible = false;
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
        _btnModeAuto?.RegisterCallback<ClickEvent>(_ => OnDeviceModeClicked(0));
        _btnModeKeyboard?.RegisterCallback<ClickEvent>(_ => OnDeviceModeClicked(1));
        _btnModeGamepad?.RegisterCallback<ClickEvent>(_ => OnDeviceModeClicked(2));
        _btnResetAll?.RegisterCallback<ClickEvent>(_ => OnResetAllClicked());
        _btnBack?.RegisterCallback<ClickEvent>(_ => Hide());
        _btnCancelRebind?.RegisterCallback<ClickEvent>(_ => OnCancelRebindClicked());

        if (_sensitivitySlider != null)
            _sensitivitySlider.RegisterValueChangedCallback(OnSensitivityChanged);

        // Gamepad B button = back (NavigationCancelEvent)
        _panelOverlay?.RegisterCallback<NavigationCancelEvent>(_ => Hide());
    }

    private void UnbindCallbacks()
    {
        _btnModeAuto?.UnregisterCallback<ClickEvent>(_ => OnDeviceModeClicked(0));
        _btnModeKeyboard?.UnregisterCallback<ClickEvent>(_ => OnDeviceModeClicked(1));
        _btnModeGamepad?.UnregisterCallback<ClickEvent>(_ => OnDeviceModeClicked(2));
        _btnResetAll?.UnregisterCallback<ClickEvent>(_ => OnResetAllClicked());
        _btnBack?.UnregisterCallback<ClickEvent>(_ => Hide());
        _btnCancelRebind?.UnregisterCallback<ClickEvent>(_ => OnCancelRebindClicked());

        if (_sensitivitySlider != null)
            _sensitivitySlider.UnregisterValueChangedCallback(OnSensitivityChanged);
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

        // TODO: Apply sensitivity to PlayerInputHandler
        // _inputHandler._gamepadCameraSensitivity = evt.newValue;
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
        // TODO: Read sensitivity from PlayerInputHandler and set slider value
        if (_sensitivitySlider != null)
        {
            float value = _sensitivitySlider.value;
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
    }

    private void HideRebindOverlay()
    {
        _rebindOverlay?.AddToClassList("hidden");
    }
}
