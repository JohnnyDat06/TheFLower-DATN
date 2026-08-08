using UnityEngine;

/// <summary>
/// BackgroundMusicPlayer — Component tự động phát nhạc nền (BGM) cho Scene/Level.
/// Chỉ cần gắn script này vào GameObject/Prefab bất kỳ và kéo thả bản nhạc BGM vào.
/// </summary>
public class BackgroundMusicPlayer : MonoBehaviour
{
    [Header("Music Settings")]
    [Tooltip("Cấu hình SOAudioClip nhạc nền (Ưu tiên nếu có)")]
    [SerializeField] private SOAudioClip backgroundMusicSO;

    [Tooltip("Hoặc AudioClip trực tiếp (nếu không dùng SOAudioClip)")]
    [SerializeField] private AudioClip backgroundMusicClip;

    [Header("Playback Options")]
    [Range(0f, 1f)] [SerializeField] private float volume = 0.5f;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool playOnStart = true;

    private AudioSource _audioSource;

    private void Start()
    {
        if (playOnStart)
        {
            PlayMusic();
        }
    }

    public void PlayMusic()
    {
        // 1. Ưu tiên phát qua SOAudioClip và AudioManager toàn cục
        if (backgroundMusicSO != null && AudioManager.Instance != null)
        {
            _audioSource = AudioManager.Instance.PlaySFXLoop(backgroundMusicSO);
            return;
        }

        // 2. Phát qua AudioClip trực tiếp nếu được gán
        if (backgroundMusicClip != null)
        {
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }
            _audioSource.clip = backgroundMusicClip;
            _audioSource.volume = volume;
            _audioSource.loop = loop;
            _audioSource.playOnAwake = false;
            _audioSource.Play();
        }
    }

    public void StopMusic()
    {
        if (backgroundMusicSO != null && AudioManager.Instance != null && _audioSource != null)
        {
            AudioManager.Instance.StopSFX(_audioSource);
            _audioSource = null;
            return;
        }

        if (_audioSource != null)
        {
            _audioSource.Stop();
        }
    }

    private void OnDestroy()
    {
        StopMusic();
    }
}
