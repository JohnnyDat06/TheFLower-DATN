using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// RebindRowController — Quản lý 1 hàng trong bảng rebind.
/// SRP: Chỉ quản lý 1 hàng UI (action name + keyboard btn + gamepad btn).
/// Không chứa business logic — delegate lên InputSettingsPanelController.
/// </summary>
public class RebindRowController
{
    public string ActionName { get; }

    private readonly Label _actionLabel;
    private readonly Button _keyboardButton;
    private readonly Button _gamepadButton;
    private readonly Action<string, InputDeviceType> _onRebindClicked;

    private static int _tabIndexCounter = 10;

    // ─── Constructor ──────────────────────────────────────────────────────────

    public RebindRowController(string actionName, ScrollView parent,
                               Action<string, InputDeviceType> onRebindClicked, bool isAlt)
    {
        ActionName = actionName;
        _onRebindClicked = onRebindClicked;

        // Tạo row element
        var row = new VisualElement();
        row.AddToClassList("rebind-row");
        if (isAlt) row.AddToClassList("rebind-row-alt");

        // Action name label
        _actionLabel = new Label(actionName);
        _actionLabel.AddToClassList("col-action");
        row.Add(_actionLabel);

        // Keyboard binding button
        var kbContainer = new VisualElement();
        kbContainer.AddToClassList("col-binding");
        _keyboardButton = new Button(() => _onRebindClicked?.Invoke(ActionName, InputDeviceType.KeyboardMouse));
        _keyboardButton.AddToClassList("rebind-button");
        _keyboardButton.focusable = true;
        _keyboardButton.tabIndex = _tabIndexCounter++;
        kbContainer.Add(_keyboardButton);
        row.Add(kbContainer);

        // Gamepad binding button
        var gpContainer = new VisualElement();
        gpContainer.AddToClassList("col-binding");
        _gamepadButton = new Button(() => _onRebindClicked?.Invoke(ActionName, InputDeviceType.Gamepad));
        _gamepadButton.AddToClassList("rebind-button");
        _gamepadButton.focusable = true;
        _gamepadButton.tabIndex = _tabIndexCounter++;
        gpContainer.Add(_gamepadButton);
        row.Add(gpContainer);

        parent.Add(row);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Cập nhật text hiển thị trên cả 2 buttons từ rebind service.
    /// </summary>
    public void Refresh(IInputRebindService rebindService, IInputIconProvider iconProvider)
    {
        // Ưu tiên rebind service display name (phản ánh binding override thực tế)
        string kbDisplay = rebindService.GetBindingDisplayName(ActionName, InputDeviceType.KeyboardMouse);
        string gpDisplay = rebindService.GetBindingDisplayName(ActionName, InputDeviceType.Gamepad);

        // Nếu rỗng → fallback sang icon provider static text
        if (string.IsNullOrEmpty(kbDisplay) && iconProvider != null)
            kbDisplay = iconProvider.GetDisplayText(ActionName, InputDeviceType.KeyboardMouse);
        if (string.IsNullOrEmpty(gpDisplay) && iconProvider != null)
            gpDisplay = iconProvider.GetDisplayText(ActionName, InputDeviceType.Gamepad);

        _keyboardButton.text = string.IsNullOrEmpty(kbDisplay) ? "---" : kbDisplay;
        _gamepadButton.text = string.IsNullOrEmpty(gpDisplay) ? "---" : gpDisplay;
    }

    /// <summary>
    /// Hiển thị trạng thái "đang rebinding" trên button tương ứng.
    /// </summary>
    public void SetRebindingState(InputDeviceType deviceType, bool isRebinding)
    {
        var btn = deviceType == InputDeviceType.Gamepad ? _gamepadButton : _keyboardButton;

        if (isRebinding)
        {
            btn.text = "...";
            btn.AddToClassList("rebinding");
        }
        else
        {
            btn.RemoveFromClassList("rebinding");
        }
    }

    /// <summary>Focus vào keyboard button (dùng khi panel mở).</summary>
    public void Focus()
    {
        _keyboardButton?.Focus();
    }

    /// <summary>Reset tab index counter (gọi khi rebuild rows).</summary>
    public static void ResetTabIndex()
    {
        _tabIndexCounter = 10;
    }
}
