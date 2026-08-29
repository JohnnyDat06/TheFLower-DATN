using UnityEngine;

/// <summary>
/// HapticService — Trung gian giữa Listener và IHapticProvider.
/// Quản lý flag IsEnabled (PlayerPrefs) và delegate request xuống Provider.
/// SRP: chỉ quyết định có nên rung hay không, rồi gọi Provider.
/// </summary>
public class HapticService : MonoBehaviour
{
    private const string PREFS_KEY = "haptics_enabled";

    [SerializeField]
    [Tooltip("Provider thực thi rung. Assign UnityInputSystemHapticProvider tại đây.")]
    private UnityInputSystemHapticProvider _provider;

    /// <summary>True khi haptics được bật trong Settings.</summary>
    public bool IsEnabled { get; private set; }

    private void Awake()
    {
        if (_provider == null)
        {
            Debug.LogError("[HapticService] UnityInputSystemHapticProvider chưa được gán trong Inspector!");
        }

        LoadEnabledFlag();
    }

    private void OnEnable()
    {
        EventBus.OnSettingsChanged += OnSettingsChanged;
    }

    private void OnDisable()
    {
        EventBus.OnSettingsChanged -= OnSettingsChanged;
    }

    /// <summary>
    /// Yêu cầu rung theo profile đã cho.
    /// Bỏ qua khi haptics bị tắt hoặc không có gamepad.
    /// </summary>
    public void Request(SOHapticProfile profile)
    {
        if (profile == null) return;
        if (!IsEnabled) return;
        if (_provider == null || !_provider.IsSupported) return;

        _provider.Play(profile.LowFrequencyMotor, profile.HighFrequencyMotor, profile.Duration);
    }

    /// <summary>Dừng rung ngay lập tức.</summary>
    public void Stop()
    {
        _provider?.Stop();
    }

    private void OnSettingsChanged()
    {
        LoadEnabledFlag();
    }

    private void LoadEnabledFlag()
    {
        // Mặc định bật (1 = true) nếu chưa từng lưu
        IsEnabled = PlayerPrefs.GetInt(PREFS_KEY, 1) == 1;
    }

    /// <summary>
    /// Gọi bởi Settings UI khi người dùng toggle haptics.
    /// Tự động raise EventBus.OnSettingsChanged để reload flag.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(PREFS_KEY, enabled ? 1 : 0);
        PlayerPrefs.Save();
        IsEnabled = enabled;

        if (!enabled) Stop();
    }
}