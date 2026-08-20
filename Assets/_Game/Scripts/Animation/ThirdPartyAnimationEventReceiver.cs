using UnityEngine;

/// <summary>
/// Nhận các Animation Event có sẵn trong animation asset bên thứ ba.
/// Component này giữ cho animation preview/demo không phát cảnh báo khi
/// event "PlayFootstep" không cần hiệu ứng âm thanh riêng.
/// </summary>
[DisallowMultipleComponent]
public sealed class ThirdPartyAnimationEventReceiver : MonoBehaviour
{
    /// <summary>
    /// Receiver cho event "PlayFootstep" trong Monkey_Walk/Monkey_Run.
    /// </summary>
    public void PlayFootstep()
    {
        // Event được giữ lại bởi asset bên thứ ba; demo monkey không dùng SFX bước chân.
    }
}
