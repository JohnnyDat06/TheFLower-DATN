using UnityEngine;

/// <summary>
/// Owns one scene's looping background music and releases it whenever that scene unloads.
/// Boss-completion music waits for the locally replicated BossDefeatController state.
/// </summary>
public sealed class SceneBackgroundMusic : MonoBehaviour
{
    [Tooltip("Music configuration played through AudioManager's Music volume channel.")]
    [SerializeField] private SOAudioClip _music;
    [Tooltip("When enabled, this track begins only after the Cat Sphinx has been defeated.")]
    [SerializeField] private bool _playAfterBossDefeat;

    private AudioSource _musicSource;
    private BossDefeatController _bossDefeatController;

    private void Start()
    {
        if (!_playAfterBossDefeat) StartMusic();
    }

    private void Update()
    {
        if (!_playAfterBossDefeat || _musicSource != null) return;

        ResolveBossDefeatController();
        if (_bossDefeatController != null && _bossDefeatController.IsDefeated)
            StartMusic();
    }

    private void OnDisable() => StopMusic();

    /// <summary>Starts this scene's two-dimensional looping music once.</summary>
    private void StartMusic()
    {
        if (_musicSource != null || _music == null || AudioManager.Instance == null) return;

        _musicSource = AudioManager.Instance.PlayMusicLoop(_music);
        if (_musicSource != null)
            Debug.Log($"[SceneBackgroundMusic] Started {_music.name} in {gameObject.scene.name}.", this);
    }

    /// <summary>Stops only the source owned by this scene, leaving unrelated music untouched.</summary>
    private void StopMusic()
    {
        if (_musicSource == null) return;

        if (AudioManager.Instance != null) AudioManager.Instance.StopMusic(_musicSource);
        _musicSource = null;
    }

    private void ResolveBossDefeatController()
    {
        if (_bossDefeatController == null)
            _bossDefeatController = FindFirstObjectByType<BossDefeatController>();
    }
}
