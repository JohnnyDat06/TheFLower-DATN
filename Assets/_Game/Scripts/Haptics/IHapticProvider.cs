/// <summary>
/// IHapticProvider — Contract cho việc phát rung tay cầm.
/// Abstraction layer giúp swap implementation (Gamepad / DualSense / Mock)
/// mà không ảnh hưởng code gọi.
/// SRP: chỉ quan tâm play / stop / supported.
/// </summary>
public interface IHapticProvider
{
    /// <summary>True khi có tay cầm hỗ trợ rung đang kết nối.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Bắt đầu rung.
    /// </summary>
    /// <param name="lowMotor">Motor tần số thấp [0, 1].</param>
    /// <param name="highMotor">Motor tần số cao [0, 1].</param>
    /// <param name="duration">Thời gian rung tính bằng giây.</param>
    void Play(float lowMotor, float highMotor, float duration);

    /// <summary>Dừng rung ngay lập tức.</summary>
    void Stop();
}