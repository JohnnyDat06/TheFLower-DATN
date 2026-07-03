/// <summary>
/// InputDeviceType — Phân biệt loại thiết bị input đang active.
/// Dùng bởi toàn bộ hệ thống input để switch behavior/UI.
/// </summary>
public enum InputDeviceType : byte
{
    KeyboardMouse = 0,
    Gamepad       = 1
}
