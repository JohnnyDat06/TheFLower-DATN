using UnityEngine;

/// <summary>
/// SOSpeedBoostRingConfig — Config data cho SpeedBoostRing (vòng tăng tốc bay).
/// SRS §13.3 · §7.4
/// </summary>
[CreateAssetMenu(fileName = "SOSpeedBoostRingConfig", menuName = "CoopGame/Environment/SpeedBoostRingConfig")]
public class SOSpeedBoostRingConfig : ScriptableObject
{
    [Tooltip("Lực tăng tốc ngang khi bay qua ring")]
    public float BoostForce = 10f;

    [Tooltip("Lực nâng lên (counter gravity tạm thời)")]
    public float LiftForce = 5f;

    [Tooltip("Thời gian trước khi cùng player có thể kích hoạt lại ring")]
    [Min(0f)] public float Cooldown = 1.5f;

    [Tooltip("Ring chỉ kích hoạt một lần cho mỗi player")]
    public bool OneShotPerPlayer = true;

    [Tooltip("Prefab VFX tia sáng khi bay qua")]
    public GameObject VFXPrefab;

    [Tooltip("SOAudioClip phát khi thu thập")]
    public SOAudioClip SFXClip;
}
