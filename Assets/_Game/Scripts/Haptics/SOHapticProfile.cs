using UnityEngine;

/// <summary>
/// SOHapticProfile — Data config cho một hiệu ứng rung tay cầm cụ thể.
/// Designer điều chỉnh cường độ và thời gian trực tiếp trong Inspector.
/// </summary>
[CreateAssetMenu(fileName = "Haptic_New", menuName = "CoopGame/Haptics/HapticProfile")]
public class SOHapticProfile : ScriptableObject
{
    [Header("Motor Speeds (0 – 1)")]
    [Tooltip("Motor tần số thấp (tay trái / cảm giác nặng).")]
    [Range(0f, 1f)]
    public float LowFrequencyMotor = 0.5f;

    [Tooltip("Motor tần số cao (tay phải / cảm giác nhẹ/tinh tế).")]
    [Range(0f, 1f)]
    public float HighFrequencyMotor = 0.5f;

    [Header("Duration")]
    [Tooltip("Thời gian rung (giây). Sau khoảng này motor tự dừng.")]
    [Min(0.01f)]
    public float Duration = 0.2f;
}