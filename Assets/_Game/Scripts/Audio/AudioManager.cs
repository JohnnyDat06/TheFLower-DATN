using UnityEngine;
using UnityEngine.Audio;
using System.Collections.Generic;

/// <summary>
/// AudioManager — Singleton quản lý việc phát âm thanh toàn cục sử dụng SOAudioClip.
/// </summary>
public class AudioManager : MonoBehaviour
{
    private static AudioManager _instance;
    public static AudioManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<AudioManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("AudioManager");
                    _instance = go.AddComponent<AudioManager>();
                }
            }
            return _instance;
        }
    }

    [Header("Mixer Settings")]
    public AudioMixer MainMixer;

    private List<AudioSource> _sfxSources = new List<AudioSource>();
    private readonly HashSet<AudioSource> _musicSources = new();
    private AudioSource _uiSource; // Nguồn âm thanh riêng cho UI để không bị chồng lấp
    private const int INITIAL_POOL_SIZE = 10;
    private Dictionary<string, float> _lastPlayTimes = new Dictionary<string, float>();
    private float _masterVolume = 1f;
    private float _musicVolume = 1f;
    private float _sfxVolume = 1f;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        LoadVolumeSettings();
        InitializePool();
        _uiSource = CreateNewSFXSource("UISource");
    }

    private void OnEnable()
    {
        EventBus.OnSettingsChanged += ApplyVolumeSettings;
    }

    private void OnDisable()
    {
        EventBus.OnSettingsChanged -= ApplyVolumeSettings;
    }

    private void InitializePool()
    {
        for (int i = 0; i < INITIAL_POOL_SIZE; i++)
        {
            CreateNewSFXSource();
        }
    }

    private AudioSource CreateNewSFXSource(string name = null)
    {
        GameObject go = new GameObject(name ?? "SFXSource_" + _sfxSources.Count);
        go.transform.SetParent(transform);
        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        
        if (MainMixer != null)
        {
            AudioMixerGroup[] groups = MainMixer.FindMatchingGroups("SFX");
            if (groups.Length > 0) source.outputAudioMixerGroup = groups[0];
        }

        if (string.IsNullOrEmpty(name)) _sfxSources.Add(source);
        return source;
    }

    private AudioSource GetAvailableSource()
    {
        foreach (var source in _sfxSources)
        {
            if (!source.isPlaying) return source;
        }
        return CreateNewSFXSource();
    }

    /// <summary>
    /// Phát nhạc nền một lần duy nhất (không lặp)
    /// </summary>
    public AudioSource PlayMusicOnce(SOAudioClip config)
    {
        if (config == null || config.Clip == null) return null;

        AudioSource source = GetAvailableSource();
        source.spatialBlend = 0f;
        source.clip = config.Clip;
        source.volume = GetMusicVolume(config.Volume);
        source.pitch = Random.Range(config.PitchMin, config.PitchMax);
        source.loop = false; // QUAN TRỌNG: Tắt lặp cho trailer
        _musicSources.Add(source);
        source.Play();
        return source;
    }

    public AudioSource PlaySFXLoop(SOAudioClip config)
    {
        if (config == null || config.Clip == null) return null;

        AudioSource source = GetAvailableSource();
        source.spatialBlend = 0f; // Luôn là 2D cho âm thanh lặp
        source.clip = config.Clip;
        source.volume = GetSfxVolume(config.Volume);
        source.pitch = Random.Range(config.PitchMin, config.PitchMax);
        source.loop = true;
        _musicSources.Remove(source);
        source.Play();
        return source;
    }

    public void StopSFX(AudioSource source)
    {
        if (source != null)
        {
            source.Stop();
            source.clip = null; // Giải phóng clip
            source.loop = false;
            _musicSources.Remove(source);
        }
    }

    public void PlaySFX(
        SOAudioClip config,
        Vector3? position = null,
        float minDistance = 1f,
        float maxDistance = 500f,
        AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic,
        int playbackScope = 0)
    {
        if (config == null || config.Clip == null) return;

        // Giới hạn tần suất phát (không cho phép phát cùng 1 clip trong vòng 0.05s)
        string cooldownKey = playbackScope == 0
            ? config.Clip.name
            : config.Clip.name + ":" + playbackScope;
        if (_lastPlayTimes.TryGetValue(cooldownKey, out float lastTime))
        {
            if (Time.unscaledTime - lastTime < 0.05f) return;
        }
        _lastPlayTimes[cooldownKey] = Time.unscaledTime;

        AudioSource source = GetAvailableSource();
        
        source.spatialBlend = position.HasValue ? 1f : 0f;
        if (position.HasValue)
        {
            source.transform.position = position.Value;
            source.minDistance = Mathf.Max(0.01f, minDistance);
            source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
            source.rolloffMode = rolloffMode;
        }

        source.clip = config.Clip;
        source.volume = GetSfxVolume(config.Volume);
        source.pitch = Random.Range(config.PitchMin, config.PitchMax);
        source.loop = false; 
        _musicSources.Remove(source);
        source.Play();
    }

    /// <summary>
    /// Phát âm thanh UI không chồng lấp (Cái mới sẽ ngắt cái cũ)
    /// </summary>
    public void PlayUISFX(SOAudioClip config)
    {
        if (config == null || config.Clip == null || _uiSource == null) return;

        _uiSource.Stop();
        _uiSource.clip = config.Clip;
        _uiSource.volume = GetSfxVolume(config.Volume);
        _uiSource.pitch = Random.Range(config.PitchMin, config.PitchMax);
        _uiSource.spatialBlend = 0f; 
        _uiSource.Play();
    }

    /// <summary>
    /// Dừng âm thanh UI ngay lập tức
    /// </summary>
    public void StopUISFX()
    {
        if (_uiSource != null && _uiSource.isPlaying)
        {
            _uiSource.Stop();
        }
    }

    private void ApplyVolumeSettings()
    {
        float oldMusicScale = Mathf.Max(0.0001f, _masterVolume * _musicVolume);
        float oldSfxScale = Mathf.Max(0.0001f, _masterVolume * _sfxVolume);

        LoadVolumeSettings();

        float newMusicScale = _masterVolume * _musicVolume;
        float newSfxScale = _masterVolume * _sfxVolume;

        foreach (var source in _sfxSources)
        {
            if (source == null) continue;

            float ratio = _musicSources.Contains(source)
                ? newMusicScale / oldMusicScale
                : newSfxScale / oldSfxScale;
            source.volume = Mathf.Clamp01(source.volume * ratio);
        }

        if (_uiSource != null)
            _uiSource.volume = Mathf.Clamp01(_uiSource.volume * (newSfxScale / oldSfxScale));
    }

    private void LoadVolumeSettings()
    {
        _masterVolume = PlayerPrefs.GetFloat(Constants.PlayerPrefsKeys.MASTER_VOLUME, 1f);
        _musicVolume = PlayerPrefs.GetFloat(Constants.PlayerPrefsKeys.BGM_VOLUME, 1f);
        _sfxVolume = PlayerPrefs.GetFloat(Constants.PlayerPrefsKeys.SFX_VOLUME, 1f);
    }

    private float GetMusicVolume(float baseVolume) => Mathf.Clamp01(baseVolume * _masterVolume * _musicVolume);
    private float GetSfxVolume(float baseVolume) => Mathf.Clamp01(baseVolume * _masterVolume * _sfxVolume);
}
