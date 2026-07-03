/// <summary>
/// IInputBindingPersistence — Abstraction cho lưu/load binding overrides.
/// SRP: Chỉ chịu trách nhiệm persistence, không biết rebind logic.
/// DIP: InputRebindService phụ thuộc interface này, không phụ thuộc PlayerPrefs trực tiếp.
/// OCP: Có thể swap sang file-based hoặc cloud save bằng cách implement interface này.
/// </summary>
public interface IInputBindingPersistence
{
    /// <summary>Lưu binding overrides JSON cho 1 device type.</summary>
    void SaveBindings(string json, InputDeviceType deviceType);

    /// <summary>Load binding overrides JSON cho 1 device type. Trả empty string nếu không có.</summary>
    string LoadBindings(InputDeviceType deviceType);

    /// <summary>Xóa bindings đã lưu cho 1 device type.</summary>
    void ClearBindings(InputDeviceType deviceType);

    /// <summary>Xóa toàn bộ bindings đã lưu (cả Keyboard và Gamepad).</summary>
    void ClearAllBindings();
}
