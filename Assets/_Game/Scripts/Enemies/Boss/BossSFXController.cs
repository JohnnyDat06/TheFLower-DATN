using UnityEngine;

/// <summary>Plays local Cat Sphinx feedback through the project's pooled AudioManager on Host and Client.</summary>
public sealed class BossSFXController : MonoBehaviour
{
    [Tooltip("Looping battle music that starts when the EnterBoss encounter becomes Active and stops after Boss Defeat.")]
    [SerializeField] private SOAudioClip _battleBossMusic;
    [Tooltip("Am bao truoc khi vung do tan cong cua Boss xuat hien.")]
    [SerializeField] private SOAudioClip _telegraphSfx;
    [Tooltip("Am va cham nang khi Boss dap xuong san.")]
    [SerializeField] private SOAudioClip _slamImpactSfx;
    [Tooltip("Am nang luong chay theo moi Shockwave cyan.")]
    [SerializeField] private SOAudioClip _shockwaveSfx;
    [Tooltip("Am thanh khi mot Rune duoc Shockwave nap thanh cong.")]
    [SerializeField] private SOAudioClip _runeChargedSfx;
    [Tooltip("Am khoa co che khi mot Seal duoc kich hoat.")]
    [SerializeField] private SOAudioClip _sealActivatedSfx;
    [Tooltip("Am Boss mat nang luong va chuyen sang Stunned.")]
    [SerializeField] private SOAudioClip _stunnedSfx;
    [Tooltip("Am Core mo ra sau khi hai Seal cung Active.")]
    [SerializeField] private SOAudioClip _coreExposedSfx;
    [Tooltip("Am mot Core Hit hop le sau tuong tac cua hai player.")]
    [SerializeField] private SOAudioClip _coreHitSfx;
    [Tooltip("Am gach san nut khi FloorTile chuyen sang Cracked hoac Warning.")]
    [SerializeField] private SOAudioClip _tileCrackSfx;
    [Tooltip("Am gach san roi khi FloorTile chuyen sang Fall.")]
    [SerializeField] private SOAudioClip _tileFallSfx;
    [Tooltip("Am ket thuc khi Cat Sphinx bi Defeat va Exit Door mo.")]
    [SerializeField] private SOAudioClip _defeatSfx;

    private BossController _bossController;
    private BossStunController _stunController;
    private BossCoreController _coreController;
    private BossPhaseController _phaseController;
    private BossDefeatController _defeatController;
    private BossEncounterManager _encounterManager;
    private FloorPatternController _floorPatternController;
    private RuneController[] _runes;
    private SealController[] _seals;
    private FloorTile[] _tiles;
    private bool _wasTelegraphActive;
    private bool _wasStunned;
    private bool _wasDefeated;
    private BossCoreState _previousCoreState;
    private int _previousCoreHitCount;
    private AudioSource _battleMusicSource;

    private void Awake()
    {
        _bossController = GetComponent<BossController>();
        _stunController = GetComponent<BossStunController>();
        _coreController = GetComponent<BossCoreController>();
        _phaseController = GetComponent<BossPhaseController>();
        _defeatController = GetComponent<BossDefeatController>();
        ResolveEncounterManager();
        _floorPatternController = GetComponent<FloorPatternController>();
        _runes = GetComponent<RuneManager>()?.Runes;
        _seals = GetComponent<SealManager>()?.Seals;
        _tiles = GetComponent<FloorTileManager>()?.Tiles;
        CaptureInitialState();
    }

    private void OnEnable()
    {
        ShockwaveController.ShockwaveVisualSpawned += HandleShockwaveSpawned;
        SubscribeStateEvents();
    }

    private void OnDisable()
    {
        ShockwaveController.ShockwaveVisualSpawned -= HandleShockwaveSpawned;
        UnsubscribeStateEvents();
        StopBattleMusic();
    }

    private void Update()
    {
        UpdateBattleMusic();

        bool isTelegraphActive = _floorPatternController != null && _floorPatternController.HasActiveTelegraph;
        if (isTelegraphActive && !_wasTelegraphActive) Play(_telegraphSfx);
        _wasTelegraphActive = isTelegraphActive;

        bool isStunned = _stunController != null && _stunController.IsStunned;
        if (isStunned && !_wasStunned) Play(_stunnedSfx);
        _wasStunned = isStunned;

        BossCoreState coreState = _coreController != null ? _coreController.State : BossCoreState.Locked;
        if (coreState == BossCoreState.Exposed && _previousCoreState != BossCoreState.Exposed)
            Play(_coreExposedSfx);
        _previousCoreState = coreState;

        int coreHitCount = _phaseController != null ? _phaseController.CoreHitCount : 0;
        if (coreHitCount > _previousCoreHitCount) Play(_coreHitSfx);
        _previousCoreHitCount = coreHitCount;

        bool isDefeated = _defeatController != null && _defeatController.IsDefeated;
        if (isDefeated && !_wasDefeated) Play(_defeatSfx);
        _wasDefeated = isDefeated;
    }

    private void UpdateBattleMusic()
    {
        ResolveEncounterManager();

        bool isDefeated = _defeatController != null && _defeatController.IsDefeated;
        if (isDefeated)
        {
            StopBattleMusic();
            return;
        }

        if (_battleMusicSource != null || _encounterManager == null || !_encounterManager.IsActive ||
            _battleBossMusic == null)
            return;

        _battleMusicSource = AudioManager.Instance.PlayMusicLoop(_battleBossMusic);
        if (_battleMusicSource != null)
            Debug.Log("[BossSFXController] BattleBossMusic started for the active boss encounter.", this);
    }

    /// <summary>Finds the network encounter object, which is authored separately from BossArena_Architecture.</summary>
    private void ResolveEncounterManager()
    {
        if (_encounterManager != null) return;

        _encounterManager = BossEncounterManager.Instance;
        if (_encounterManager == null)
            _encounterManager = FindFirstObjectByType<BossEncounterManager>();
    }

    private void StopBattleMusic()
    {
        if (_battleMusicSource == null) return;

        if (AudioManager.Instance != null) AudioManager.Instance.StopMusic(_battleMusicSource);
        _battleMusicSource = null;
    }

    private void CaptureInitialState()
    {
        _wasTelegraphActive = _floorPatternController != null && _floorPatternController.HasActiveTelegraph;
        _wasStunned = _stunController != null && _stunController.IsStunned;
        _previousCoreState = _coreController != null ? _coreController.State : BossCoreState.Locked;
        _previousCoreHitCount = _phaseController != null ? _phaseController.CoreHitCount : 0;
        _wasDefeated = _defeatController != null && _defeatController.IsDefeated;
    }

    private void SubscribeStateEvents()
    {
        if (_runes != null)
            foreach (RuneController rune in _runes)
                if (rune != null) rune.StateChanged += HandleRuneStateChanged;
        if (_seals != null)
            foreach (SealController seal in _seals)
                if (seal != null) seal.StateChanged += HandleSealStateChanged;
        if (_tiles != null)
            foreach (FloorTile tile in _tiles)
                if (tile != null) tile.StateChanged += HandleTileStateChanged;
    }

    private void UnsubscribeStateEvents()
    {
        if (_runes != null)
            foreach (RuneController rune in _runes)
                if (rune != null) rune.StateChanged -= HandleRuneStateChanged;
        if (_seals != null)
            foreach (SealController seal in _seals)
                if (seal != null) seal.StateChanged -= HandleSealStateChanged;
        if (_tiles != null)
            foreach (FloorTile tile in _tiles)
                if (tile != null) tile.StateChanged -= HandleTileStateChanged;
    }

    private void HandleShockwaveSpawned(ShockwaveSpawnInfo spawnInfo)
    {
        Play(_slamImpactSfx);
        Play(_shockwaveSfx);
    }

    private void HandleRuneStateChanged(RuneController rune, RuneState state)
    {
        if (state == RuneState.Charged) Play(_runeChargedSfx);
    }

    private void HandleSealStateChanged(SealController seal, SealState state)
    {
        if (state == SealState.Active) Play(_sealActivatedSfx);
    }

    private void HandleTileStateChanged(FloorTile tile, FloorTileState state)
    {
        if (state is FloorTileState.Cracked or FloorTileState.Warning) Play(_tileCrackSfx);
        else if (state == FloorTileState.Fall) Play(_tileFallSfx);
    }

    private static void Play(SOAudioClip clip)
    {
        if (clip == null) return;
        AudioManager.Instance.PlaySFX(clip);
    }
}
