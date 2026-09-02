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
    [SerializeField, Tooltip("Logic bão cung cấp khoảng cách và trạng thái Safe/Warning/Critical.")]
    private SandstormChaseController _stormLogic;
    [SerializeField, Tooltip("Completion cung cấp TempleFinish và VaoDen để tạo tương phản âm thanh khi vào đền.")]
    private SandBoatChaseCompletion _completion;
    [SerializeField, Tooltip("Nguồn collision authoritative dùng để phát âm va đá đúng một lần trên mỗi máy.")]
    private SandBoatNetworkSynchronizer _networkSynchronizer;

    [Header("SOAudioClip do AudioManager quản lý")]
    [SerializeField, Tooltip("Cấu hình tiếng thân thuyền kẽo kẹt; AudioManager phát loop qua kênh SFX khi Chase đang chạy.")]
    private SOAudioClip _boatCreakSfx;
    [SerializeField, Tooltip("Cấu hình windloop chính của bão cát; AudioManager phát qua kênh SFX.")]
    private SOAudioClip _stormWindSfx;
    [SerializeField, Tooltip("Cấu hình rumble tần số thấp bổ sung cho bão cát.")]
    private SOAudioClip _stormRumbleSfx;
    [SerializeField, Tooltip("Cấu hình âm va chạm đá phát một lần qua AudioManager.")]
    private SOAudioClip _rockCollisionSfx;

    [Header("Cân chỉnh âm lượng")]
    [SerializeField, Range(0f, 1f), Tooltip("Âm lượng tiếng thân thuyền khi Chase đang chạy, trước khi AudioManager áp dụng Master/SFX Volume.")]
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
    private float _currentBoatCreakVolume;
    private float _currentStormWindVolume;
    private float _currentStormRumbleVolume;

    private void OnValidate()
    {
        _volumeFadeSpeed = Mathf.Max(0.01f, _volumeFadeSpeed);
        _templeMuffledCutoff = Mathf.Clamp(_templeMuffledCutoff, 200f, 22000f);
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

        StopManagedLoop(ref _boatCreakSource, ref _currentBoatCreakVolume);
        StopManagedLoop(ref _stormWindSource, ref _currentStormWindVolume);
        StopManagedLoop(ref _stormRumbleSource, ref _currentStormRumbleVolume);
    }

    private void Update()
    {
        bool chaseActive = _boarding != null && _boarding.ChaseStarted;
        bool completed = _completion != null && _completion.IsChaseCompleted;
        // Windloop chỉ bắt đầu khi Boat đã khởi hành và bão thực sự truy đuổi.
        // Sau TempleFinish, bão vẫn đuổi người chơi cho đến khi họ vào VaoDen.
        bool stormIsPursuingPlayers = chaseActive ||
                                      (completed && !_completion.IsStormStoppedAtShelter);
        float stormPressure = _stormLogic != null ? 1f - _stormLogic.StormDistance : 0f;

        UpdateManagedLoop(
            ref _boatCreakSource,
            ref _currentBoatCreakVolume,
            _boatCreakSfx,
            chaseActive && !completed ? _boatCreakVolume : 0f,
            1f,
            false);
        UpdateManagedLoop(
            ref _stormWindSource,
            ref _currentStormWindVolume,
            _stormWindSfx,
            stormIsPursuingPlayers
                ? Mathf.Lerp(_safeWindVolume, _criticalWindVolume, Mathf.Clamp01(stormPressure * 1.4f))
                : 0f,
            Mathf.Lerp(0.96f, 1.04f, stormPressure),
            true);
        UpdateManagedLoop(
            ref _stormRumbleSource,
            ref _currentStormRumbleVolume,
            _stormRumbleSfx,
            stormIsPursuingPlayers
                ? Mathf.Lerp(0.08f, _criticalRumbleVolume, Mathf.Clamp01(stormPressure * 1.5f))
                : 0f,
            Mathf.Lerp(0.88f, 1.05f, stormPressure),
            true);

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
        ref float currentBaseVolume,
        SOAudioClip config,
        float targetVolume,
        float targetPitch,
        bool usesTempleLowPass)
    {
        AudioManager manager = AudioManager.Instance;
        if (manager == null || config == null || config.Clip == null)
        {
            StopManagedLoop(ref source, ref currentBaseVolume);
            return;
        }

        if (source == null && targetVolume > 0.001f)
        {
            source = manager.PlaySFXLoop(config);
            if (source != null)
            {
                currentBaseVolume = 0f;
                manager.SetSFXLoopParameters(source, 0f, targetPitch);
            }
        }

        if (source == null)
        {
            return;
        }

        currentBaseVolume = Mathf.MoveTowards(
            currentBaseVolume,
            targetVolume * config.Volume,
            _volumeFadeSpeed * Time.deltaTime);
        float currentPitch = Mathf.Lerp(
            source.pitch,
            targetPitch,
            1f - Mathf.Exp(-5f * Time.deltaTime));
        manager.SetSFXLoopParameters(source, currentBaseVolume, currentPitch);

        if (usesTempleLowPass)
        {
            bool stormMuffled = _completion != null && _completion.IsStormStoppedAtShelter;
            SetLowPass(source, stormMuffled ? _templeMuffledCutoff : 22000f);
        }

        if (currentBaseVolume <= 0.001f && targetVolume <= 0f)
        {
            StopManagedLoop(ref source, ref currentBaseVolume);
        }
    }

    private static void StopManagedLoop(ref AudioSource source, ref float currentBaseVolume)
    {
        if (source == null)
        {
            currentBaseVolume = 0f;
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
        currentBaseVolume = 0f;
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
