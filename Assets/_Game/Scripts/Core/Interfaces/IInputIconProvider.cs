using UnityEngine;

/// <summary>
/// IInputIconProvider — Abstraction cho icon/text mapping theo device type.
/// OCP: Thêm provider mới (PS5, Switch) bằng cách tạo ScriptableObject entries,
///       KHÔNG cần sửa code.
/// ISP: Tách riêng khỏi detect và rebind.
/// </summary>
public interface IInputIconProvider
{
    /// <summary>
    /// Lấy display text cho action theo device type.
    /// Ví dụ: GetDisplayText("Jump", Gamepad) → "A"
    /// </summary>
    string GetDisplayText(string actionName, InputDeviceType deviceType);

    /// <summary>
    /// Lấy sprite icon cho action theo device type. Trả null nếu chưa có sprite.
    /// </summary>
    Sprite GetIcon(string actionName, InputDeviceType deviceType);

    /// <summary>True nếu có sprite icon (không phải text fallback).</summary>
    bool HasIcon(string actionName, InputDeviceType deviceType);
}
