using System;

/// <summary>
/// IInputDeviceDetector — Abstraction cho hệ thống detect device đang active.
/// SRP: Chỉ chịu trách nhiệm cho biết device nào đang dùng.
/// ISP: Tách riêng khỏi rebind, icon, persistence.
/// </summary>
public interface IInputDeviceDetector
{
    /// <summary>Loại device đang active hiện tại.</summary>
    InputDeviceType CurrentDeviceType { get; }

    /// <summary>Event fired khi device type thay đổi (KeyboardMouse ↔ Gamepad).</summary>
    event Action<InputDeviceType> DeviceChanged;

    /// <summary>True nếu có ít nhất 1 Gamepad đang kết nối.</summary>
    bool IsGamepadConnected { get; }
}
