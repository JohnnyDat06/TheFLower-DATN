using UnityEngine;

/// <summary>
/// Điều khiển audio presentation của Sand Boat Chase bằng AudioManager hiện có.
/// Mọi nguồn âm thanh đều đi qua kênh SFX để Master/SFX Volume trong Settings có hiệu lực.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandBoatChaseAudio : MonoBehaviour
{
    [Header("Trạng thái gameplay")]
    [SerializeField, Tooltip("Boarding cung cấp trạng thái chase đang chạy.")]
    private SandBoatBoarding _boarding;
    [SerializeField, Tooltip("Movement cung cấp tốc độ thuyền hiện tại để điều chỉnh tiếng thân thuyền.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Logic bão cung cấp khoảng cách và trạng thái Safe/Warning/Critical.")]
    private SandstormChaseController _stormLogic;
    [SerializeField, Tooltip("Completion cung cấp TempleFinish và VaoDen để tạo tương phản âm thanh khi vào đền.")]
    private SandBoatChaseCompletion _completion;
    [SerializeField, Tooltip("Nguồn collision authoritative dùng để phát âm va đá đúng một lần trên mỗi máy.")]
    private SandBoatNetworkSynchronizer _networkSynchronizer;

    [Header("SOAudioClip do AudioManager quản lý")]
    [SerializeField, Tooltip("Cấu hình tiếng thân thuyền gỗ; AudioManager phát qua kênh SFX.")]
    private SOAudioClip _boatCreakSfx;
    [SerializeField, Tooltip("Cấu hình windloop chính của bão cát; AudioManager phát qua kênh SFX.")]
    private SOAudioClip _stormWindSfx;
    [SerializeField, Tooltip("Cấu hình rumble tần số thấp bổ sung cho bão cát.")]
    private SOAudioClip _stormRumbleSfx;
    [SerializeField, Tooltip("Cấu hình âm va chạm đá phát một lần qua AudioManager.")]
    private SOAudioClip _rockCollisionSfx;
    [SerializeField, Tooltip("Cấu hình âm cổng đá đóng phát một lần qua AudioManager.")]
    private SOAudioClip _gateClosingSfx;

    [Header("Cân chỉnh âm lượng")]
    [SerializeField, Range(0f, 1f), Tooltip("Âm lượng tối đa của tiếng thân thuyền trước khi áp dụng Master/SFX Volume.")]
    private float _boatCreakVolume = 0.28f;
    [SerializeField, Range(0f, 1f), Tooltip("Âm lượng windloop khi bão ở trạng thái Safe, trước khi áp dụng Master/SFX Volume.")]
    private float _safeWindVolume = 0.42f;
    [SerializeField, Range(0f, 1f), Tooltip("Âm lượng windloop khi bão ở trạng thái Critical, trước khi áp dụng Master/SFX Volume.")]
    private float _criticalWindVolume = 0.9f;
    [SerializeField, Range(0f, 1f), Tooltip("Âm lượng rumble tối đa khi bão Critical, trước khi áp dụng Master/SFX Volume.")]
    private float _criticalRumbleVolume = 0.85f;
    [SerializeField, Min(0.01f), Tooltip("Tốc độ fade âm lượng để loop không bật hoặc tắt đột ngột.")]
    private float _volumeFadeSpeed = 3.5f;
    [SerializeField, Range(200f, 22000f), Tooltip("Tần số Low Pass khi người chơi vào VaoDen; giá trị thấp tạo cảm giác bão bị bóp nghẹt ngoài đền.")]
    private float _templeMuffledCutoff = 850f;

    private AudioSource _boatCreakSource;
    private AudioSource _stormWindSource;
    private AudioSource _stormRumbleSource;
    private bool _wasCompleted;

    private void OnValidate()
    {
        _volumeFadeSpeed = Mathf.Max(0.01f, _volumeFadeSpeed);
        _templeMuffledCutoff = Mathf.Clamp(_templeMuffledCutoff, 200f, 22000f);
    }

    private void Awake()
    {
        _wasCompleted = _completion != null && _completion.IsChaseCompleted;
    }

    private void OnEnable()
    {
        if (_networkSynchronizer != null)
        {
            _networkSynchronizer.CollisionConfirmedLocally += PlayRockCollision;
        }
    }

    private void OnDisable()
    {
        if (_networkSynchronizer != null)
        {
            _networkSynchronizer.CollisionConfirmedLocally -= PlayRockCollision;
        }

        StopManagedLoop(ref _boatCreakSource);
        StopManagedLoop(ref _stormWindSource);
        StopManagedLoop(ref _stormRumbleSource);
    }

    private void Update()
    {
        bool chaseActive = _boarding != null && _boarding.ChaseStarted;
        bool completed = _completion != null && _completion.IsChaseCompleted;
        bool stormOutsideTemple = completed && !_completion.IsStormStoppedAtShelter;
        bool stormShouldPlay = chaseActive || stormOutsideTemple;
        float speedRatio = _movement != null
            ? Mathf.InverseLerp(8f, 24f, _movement.CurrentForwardSpeed)
            : 0f;
        float stormPressure = _stormLogic != null ? 1f - _stormLogic.StormDistance : 0f;

        UpdateManagedLoop(
            ref _boatCreakSource,
            _boatCreakSfx,
            chaseActive ? Mathf.Lerp(0.08f, _boatCreakVolume, speedRatio) : 0f,
            Mathf.Lerp(0.9f, 1.1f, speedRatio),
            false);
        UpdateManagedLoop(
            ref _stormWindSource,
            _stormWindSfx,
            stormShouldPlay
                ? Mathf.Lerp(_safeWindVolume, _criticalWindVolume, Mathf.Clamp01(stormPressure * 1.4f))
                : 0f,
            Mathf.Lerp(0.96f, 1.04f, stormPressure),
            true);
        UpdateManagedLoop(
            ref _stormRumbleSource,
            _stormRumbleSfx,
            stormShouldPlay
                ? Mathf.Lerp(0.08f, _criticalRumbleVolume, Mathf.Clamp01(stormPressure * 1.5f))
                : 0f,
            Mathf.Lerp(0.88f, 1.05f, stormPressure),
            true);

        if (completed && !_wasCompleted && _gateClosingSfx != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(_gateClosingSfx);
        }

        _wasCompleted = completed;
    }

    private void PlayRockCollision()
    {
        if (_rockCollisionSfx != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(_rockCollisionSfx);
        }
    }

    private void UpdateManagedLoop(
        ref AudioSource source,
        SOAudioClip config,
        float targetVolume,
        float targetPitch,
        bool usesTempleLowPass)
    {
        AudioManager manager = AudioManager.Instance;
        if (manager == null || config == null || config.Clip == null)
        {
            StopManagedLoop(ref source);
            return;
        }

        if (source == null && targetVolume > 0.001f)
        {
            source = manager.PlaySFXLoop(config);
            if (source != null)
            {
                source.volume = 0f;
            }
        }

        if (source == null)
        {
            return;
        }

        float scaledTargetVolume = manager.ScaleSFXVolume(targetVolume * config.Volume);
        source.volume = Mathf.MoveTowards(
            source.volume,
            scaledTargetVolume,
            _volumeFadeSpeed * Time.deltaTime);
        source.pitch = Mathf.Lerp(
            source.pitch,
            targetPitch,
            1f - Mathf.Exp(-5f * Time.deltaTime));

        if (usesTempleLowPass)
        {
            bool stormMuffled = _completion != null && _completion.IsStormStoppedAtShelter;
            SetLowPass(source, stormMuffled ? _templeMuffledCutoff : 22000f);
        }

        if (source.volume <= 0.001f && targetVolume <= 0f)
        {
            StopManagedLoop(ref source);
        }
    }

    private static void StopManagedLoop(ref AudioSource source)
    {
        if (source == null)
        {
            return;
        }

        AudioLowPassFilter filter = source.GetComponent<AudioLowPassFilter>();
        if (filter != null)
        {
            filter.enabled = false;
            filter.cutoffFrequency = 22000f;
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.StopSFX(source);
        }
        else
        {
            source.Stop();
        }

        source = null;
    }

    private static void SetLowPass(AudioSource source, float cutoff)
    {
        if (source == null)
        {
            return;
        }

        AudioLowPassFilter filter = source.GetComponent<AudioLowPassFilter>();
        if (filter == null)
        {
            filter = source.gameObject.AddComponent<AudioLowPassFilter>();
        }

        filter.enabled = true;
        filter.cutoffFrequency = cutoff;
    }
}
