using System;
using UnityEngine;

/// <summary>
/// InputIconMap — ScriptableObject chứa mapping action → icon/text theo device type.
/// Implements IInputIconProvider.
/// 
/// OCP: Thêm loại Gamepad mới (PS5, Switch) → tạo thêm entries, KHÔNG sửa code.
/// SRP: Chỉ mapping icon/text, KHÔNG detect device, KHÔNG rebind.
/// 
/// Sprite icons hiện tại để null (text fallback). Thêm sprite assets sau.
/// </summary>
[CreateAssetMenu(fileName = "InputIconMap", menuName = "Game/Input Icon Map")]
public class InputIconMap : ScriptableObject, IInputIconProvider
{
    [Serializable]
    public struct IconEntry
    {
        [Tooltip("Tên action trong InputActionAsset (ví dụ: Jump, Dash, Attack)")]
        public string actionName;

        [Tooltip("Sprite icon. Null = dùng displayText fallback. Thêm sprite sau.")]
        public Sprite icon;

        [Tooltip("Text hiển thị khi không có sprite (ví dụ: Space, A, X)")]
        public string displayText;
    }

    [Header("Keyboard + Mouse")]
    [SerializeField] private IconEntry[] _keyboardMouseEntries = new IconEntry[]
    {
        new() { actionName = "Jump",     displayText = "Space" },
        new() { actionName = "Dash",     displayText = "Q" },
        new() { actionName = "Crouch",   displayText = "L Ctrl" },
        new() { actionName = "Interact", displayText = "E" },
        new() { actionName = "Attack",   displayText = "LMB" },
        new() { actionName = "Sprint",   displayText = "L Shift" },
        new() { actionName = "Pause",    displayText = "Esc" },
    };

    [Header("Xbox Gamepad")]
    [SerializeField] private IconEntry[] _gamepadEntries = new IconEntry[]
    {
        new() { actionName = "Jump",     displayText = "A" },
        new() { actionName = "Dash",     displayText = "B" },
        new() { actionName = "Crouch",   displayText = "LS" },
        new() { actionName = "Interact", displayText = "Y" },
        new() { actionName = "Attack",   displayText = "X" },
        new() { actionName = "Sprint",   displayText = "LT" },
        new() { actionName = "Pause",    displayText = "Back" },
    };

    // ─── IInputIconProvider Implementation ────────────────────────────────────

    /// <inheritdoc/>
    public string GetDisplayText(string actionName, InputDeviceType deviceType)
    {
        var entries = GetEntries(deviceType);
        if (entries == null) return actionName;

        foreach (var entry in entries)
        {
            if (string.Equals(entry.actionName, actionName, StringComparison.Ordinal))
                return entry.displayText;
        }

        return actionName; // fallback: raw action name
    }

    /// <inheritdoc/>
    public Sprite GetIcon(string actionName, InputDeviceType deviceType)
    {
        var entries = GetEntries(deviceType);
        if (entries == null) return null;

        foreach (var entry in entries)
        {
            if (string.Equals(entry.actionName, actionName, StringComparison.Ordinal))
                return entry.icon;
        }

        return null;
    }

    /// <inheritdoc/>
    public bool HasIcon(string actionName, InputDeviceType deviceType)
    {
        return GetIcon(actionName, deviceType) != null;
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    private IconEntry[] GetEntries(InputDeviceType deviceType) => deviceType switch
    {
        InputDeviceType.Gamepad => _gamepadEntries,
        _                      => _keyboardMouseEntries
    };
}
