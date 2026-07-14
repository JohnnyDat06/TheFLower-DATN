using System;
using System.Collections.Generic;

/// <summary>
/// IInputRebindService — Abstraction cho hệ thống rebind input.
/// SRP: Chỉ xử lý rebind logic, không save/load (delegate cho IInputBindingPersistence).
/// ISP: Tách riêng khỏi detect, icon, persistence.
/// </summary>
public interface IInputRebindService
{
    /// <summary>
    /// Bắt đầu rebind cho 1 action. Async — game vào listen mode.
    /// </summary>
    /// <param name="actionName">Tên action (ví dụ: "Jump")</param>
    /// <param name="deviceType">Device type cần rebind (KeyboardMouse hoặc Gamepad)</param>
    /// <param name="onComplete">Callback khi rebind xong: (success, newKeyDisplayName)</param>
    /// <param name="onConflict">Callback khi conflict: (conflictActionName)</param>
    void StartRebind(string actionName, InputDeviceType deviceType,
                     Action<bool, string> onComplete, Action<string> onConflict = null);

    /// <summary>
    /// Bắt đầu rebind theo cột UI cụ thể: Keyboard, Mouse hoặc Gamepad.
    /// </summary>
    void StartRebind(string actionName, InputBindingTarget target,
                     Action<bool, string> onComplete, Action<InputRebindConflict> onConflict = null);

    /// <summary>Hủy rebind đang chờ input.</summary>
    void CancelRebind();

    /// <summary>Reset toàn bộ bindings (cả Keyboard và Gamepad) về mặc định.</summary>
    void ResetAllBindings();

    /// <summary>Reset bindings của 1 device type về mặc định.</summary>
    void ResetBindingsForDevice(InputDeviceType deviceType);

    /// <summary>
    /// Lấy display name của binding hiện tại cho action theo device type.
    /// Ví dụ: GetBindingDisplayName("Jump", KeyboardMouse) → "Space"
    /// </summary>
    string GetBindingDisplayName(string actionName, InputDeviceType deviceType);

    /// <summary>Lấy display name cho cột Keyboard, Mouse hoặc Gamepad.</summary>
    string GetBindingDisplayName(string actionName, InputBindingTarget target);

    /// <summary>Danh sách action names có thể rebind (không bao gồm Move, CameraLook, SkipCutScene).</summary>
    IReadOnlyList<string> GetRebindableActionNames();

    /// <summary>True nếu đang trong quá trình rebind (chờ input).</summary>
    bool IsRebinding { get; }

    /// <summary>True khi có conflict đang chờ UI xác nhận swap.</summary>
    bool HasPendingConflict { get; }

    /// <summary>Conflict đang chờ xác nhận. Chỉ hợp lệ khi HasPendingConflict = true.</summary>
    InputRebindConflict PendingConflict { get; }

    /// <summary>Áp dụng swap binding đang chờ xác nhận.</summary>
    bool ApplyPendingConflict();

    /// <summary>Hủy conflict đang chờ xác nhận.</summary>
    void DiscardPendingConflict();
}
