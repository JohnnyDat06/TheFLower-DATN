using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

/// <summary>
/// Owns one Elden Ring-style binding row: action name + Keyboard/Mouse/Gamepad cells.
/// Business logic stays in InputSettingsPanelController and InputRebindService.
/// </summary>
public class RebindRowController
{
    public string ActionName { get; }

    private readonly Label _actionLabel;
    private readonly Button _keyboardButton;
    private readonly Button _mouseButton;
    private readonly Button _gamepadButton;
    private readonly List<InputBindingTarget> _visibleTargets = new();
    private readonly Action<string, InputBindingTarget> _onRebindClicked;

    private static int _tabIndexCounter = 10;

    public RebindRowController(string actionName, ScrollView parent,
                               Action<string, InputBindingTarget> onRebindClicked,
                               IReadOnlyList<InputBindingTarget> visibleTargets,
                               bool isAlt)
    {
        ActionName = actionName;
        _onRebindClicked = onRebindClicked;
        _visibleTargets.AddRange(visibleTargets);

        var row = new VisualElement();
        row.AddToClassList("rebind-row");
        if (isAlt) row.AddToClassList("rebind-row-alt");

        _actionLabel = new Label(GetFriendlyActionName(actionName));
        _actionLabel.AddToClassList("col-action");
        row.Add(_actionLabel);

        if (_visibleTargets.Contains(InputBindingTarget.Keyboard))
        {
            _keyboardButton = CreateBindingButton(InputBindingTarget.Keyboard);
            row.Add(WrapBindingButton(_keyboardButton));
        }

        if (_visibleTargets.Contains(InputBindingTarget.Mouse))
        {
            _mouseButton = CreateBindingButton(InputBindingTarget.Mouse);
            row.Add(WrapBindingButton(_mouseButton));
        }

        if (_visibleTargets.Contains(InputBindingTarget.Gamepad))
        {
            _gamepadButton = CreateBindingButton(InputBindingTarget.Gamepad);
            row.Add(WrapBindingButton(_gamepadButton));
        }

        parent.Add(row);
    }

    public void Refresh(InputRebindService rebindService)
    {
        SetButtonText(_keyboardButton, rebindService.GetBindingDisplayName(ActionName, InputBindingTarget.Keyboard));
        SetButtonText(_mouseButton, rebindService.GetBindingDisplayName(ActionName, InputBindingTarget.Mouse));
        SetButtonText(_gamepadButton, rebindService.GetBindingDisplayName(ActionName, InputBindingTarget.Gamepad));
    }

    public void SetRebindingState(InputBindingTarget target, bool isRebinding)
    {
        var button = GetButton(target);
        if (button == null) return;

        if (isRebinding)
        {
            button.text = "...";
            button.AddToClassList("rebinding");
        }
        else
        {
            button.RemoveFromClassList("rebinding");
        }
    }

    public void Focus(InputBindingTarget target = InputBindingTarget.Keyboard)
    {
        (GetButton(target) ?? GetFirstButton())?.Focus();
    }

    public static void ResetTabIndex()
    {
        _tabIndexCounter = 10;
    }

    private Button CreateBindingButton(InputBindingTarget target)
    {
        var button = new Button(() => _onRebindClicked?.Invoke(ActionName, target));
        button.AddToClassList("rebind-button");
        button.focusable = true;
        button.tabIndex = _tabIndexCounter++;
        return button;
    }

    private static VisualElement WrapBindingButton(Button button)
    {
        var container = new VisualElement();
        container.AddToClassList("col-binding");
        container.Add(button);
        return container;
    }

    private Button GetButton(InputBindingTarget target) => target switch
    {
        InputBindingTarget.Mouse => _mouseButton,
        InputBindingTarget.Gamepad => _gamepadButton,
        _ => _keyboardButton
    };

    private Button GetFirstButton()
    {
        if (_keyboardButton != null) return _keyboardButton;
        if (_mouseButton != null) return _mouseButton;
        return _gamepadButton;
    }

    private static void SetButtonText(Button button, string displayName)
    {
        if (button == null) return;
        button.text = string.IsNullOrWhiteSpace(displayName) ? "----" : displayName;
    }

    private static string GetFriendlyActionName(string actionName) => actionName switch
    {
        "Jump" => "Jump",
        "Dash" => "Backstep / Dodge Roll / Dash",
        "Crouch" => "Crouch / Stand Up",
        "Interact" => "Event Action",
        "Attack" => "Attack",
        "Sprint" => "Sprint / Guard",
        _ => actionName
    };
}
