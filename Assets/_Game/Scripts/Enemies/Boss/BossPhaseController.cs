using UnityEngine;

/// <summary>Runs Phase 1 and Phase 2 combat loops, then hands combat to the Phase 3 combo after Core Hit #2.</summary>
public sealed class BossPhaseController : MonoBehaviour
{
    [Tooltip("Cau hinh mau Core va nhip tan cong cua Phase 1 va Phase 2.")]
    [SerializeField] private BossPhaseData _phaseData = new();
    [Tooltip("Mau Core hien tai, hien thi de debug trong Inspector.")]
    [SerializeField] private int _debugCurrentCoreHealth;
    [Tooltip("Tong Core Hit da duoc ghi nhan, hien thi de debug trong Inspector.")]
    [SerializeField] private int _debugCoreHitCount;
    [Tooltip("Phase combat hien tai, hien thi de debug trong Inspector.")]
    [SerializeField] private BossCombatPhase _debugCurrentPhase = BossCombatPhase.PhaseOne;
    [Tooltip("Bat de Boss dung auto-attack; nhan V se chay mot don phu hop voi Phase hien tai. Tat de Boss danh binh thuong.")]
    [SerializeField] private bool _debugManualAttackMode = true;

    private BossController _bossController;
    private BossCoreController _coreController;
    private BossStunController _stunController;
    private BossTargetSlamAttack _targetSlamAttack;
    private BossDoublePawAttack _doublePawAttack;
    private FloorTileManager _floorTileManager;
    private float _nextAttackTime;
    private bool _nextPhaseTwoAttackIsDoublePaw;

    /// <summary>Current implemented combat phase.</summary>
    public BossCombatPhase CurrentPhase => _debugCurrentPhase;

    /// <summary>Remaining encounter Core-health after valid Core Hits.</summary>
    public int CurrentCoreHealth => _debugCurrentCoreHealth;

    /// <summary>Total valid Core Hits recorded by the authoritative encounter.</summary>
    public int CoreHitCount => _debugCoreHitCount;

    /// <summary>True when combat attacks are manually advanced with the V key for local testing.</summary>
    public bool IsDebugManualAttackMode => _debugManualAttackMode;

    /// <summary>Sets the encounter Core-health to zero for the Phase 17 local defeat test only.</summary>
    public void DebugSetFinalCoreHit()
    {
        _debugCurrentCoreHealth = 0;
        _debugCoreHitCount = _phaseData.MaxCoreHealth;
        Debug.Log("[BossPhaseController] Debug final Core Hit set Core-health to 0.", this);
    }

    /// <summary>Copies authoritative phase and Core-health values to a non-simulating Client.</summary>
    public void ApplyNetworkState(BossCombatPhase phase, int currentCoreHealth, int coreHitCount)
    {
        _debugCurrentPhase = phase;
        _debugCurrentCoreHealth = Mathf.Max(0, currentCoreHealth);
        _debugCoreHitCount = Mathf.Max(0, coreHitCount);
    }

    /// <summary>Restores Phase 1 and all Core-health after a server-authoritative full-party wipe.</summary>
    public void ResetEncounterState()
    {
        _debugCurrentCoreHealth = _phaseData.MaxCoreHealth;
        _debugCoreHitCount = 0;
        _debugCurrentPhase = BossCombatPhase.PhaseOne;
        _nextPhaseTwoAttackIsDoublePaw = false;
        _nextAttackTime = Time.time + _phaseData.AttackCycleInterval;
        enabled = true;
    }

    private void Awake()
    {
        _bossController = GetComponent<BossController>();
        _coreController = GetComponent<BossCoreController>();
        _stunController = GetComponent<BossStunController>();
        _targetSlamAttack = GetComponent<BossTargetSlamAttack>();
        _doublePawAttack = GetComponent<BossDoublePawAttack>();
        _floorTileManager = GetComponent<FloorTileManager>();
        _debugCurrentCoreHealth = _phaseData.MaxCoreHealth;
        if (_coreController != null) _coreController.CoreHit += HandleCoreHit;
        _nextAttackTime = Time.time + _phaseData.AttackCycleInterval;
    }

    private void OnDestroy()
    {
        if (_coreController != null) _coreController.CoreHit -= HandleCoreHit;
    }

    private void Update()
    {
        if (_debugCurrentPhase == BossCombatPhase.PhaseThree ||
            _bossController == null ||
            _stunController == null ||
            _stunController.IsStunned)
            return;

        if (_debugManualAttackMode)
        {
            if (!Input.GetKeyDown(KeyCode.V)) return;
        }
        else if (Time.time < _nextAttackTime)
            return;

        if (_debugCurrentPhase == BossCombatPhase.PhaseOne)
            RunPhaseOneAttack();
        else
            RunPhaseTwoAttack();
    }

    private void RunPhaseOneAttack()
    {
        if (_targetSlamAttack == null) _targetSlamAttack = GetComponent<BossTargetSlamAttack>();
        if (_targetSlamAttack != null)
        {
            if (_targetSlamAttack.IsRunning) return;

            if (_targetSlamAttack.TryStart(false))
                _nextAttackTime = Time.time + _phaseData.AttackCycleInterval;
            return;
        }

        if (_bossController.CurrentState != BossState.Idle) return;

        _bossController.TryStartPawSlamCycle();
        _nextAttackTime = Time.time + _phaseData.AttackCycleInterval;
    }

    private void RunPhaseTwoAttack()
    {
        if (_targetSlamAttack == null) _targetSlamAttack = GetComponent<BossTargetSlamAttack>();
        if (_doublePawAttack == null) _doublePawAttack = GetComponent<BossDoublePawAttack>();
        if (_targetSlamAttack == null || _targetSlamAttack.IsRunning ||
            (_doublePawAttack != null && _doublePawAttack.IsRunning))
            return;

        bool started;
        if (_nextPhaseTwoAttackIsDoublePaw)
        {
            started = _doublePawAttack != null && _doublePawAttack.TryStart();
            if (!started) started = _targetSlamAttack.TryStart(false);
        }
        else
        {
            started = _targetSlamAttack.TryStart(false);
        }
        if (!started) return;

        _nextPhaseTwoAttackIsDoublePaw = !_nextPhaseTwoAttackIsDoublePaw;
        _nextAttackTime = Time.time + _phaseData.PhaseTwoAttackCycleInterval;
    }

    private void HandleCoreHit()
    {
        _debugCurrentCoreHealth = Mathf.Max(0, _debugCurrentCoreHealth - 1);
        _debugCoreHitCount++;
        Debug.Log($"[BossPhaseController] Core Hit #{_debugCoreHitCount}. Boss Core-health: {_debugCurrentCoreHealth}/{_phaseData.MaxCoreHealth}.", this);

        if (_debugCurrentPhase == BossCombatPhase.PhaseThree)
        {
            ResetFloorTilesForNextPhase();
            Debug.Log("[BossPhaseController] Final Core Hit recorded. Boss Defeat will be handled in Phase 17.", this);
            return;
        }

        if (_debugCurrentPhase == BossCombatPhase.PhaseOne)
        {
            ResetFloorTilesForNextPhase();
            _debugCurrentPhase = BossCombatPhase.PhaseTwo;
            _nextAttackTime = Time.time + _phaseData.PhaseTwoAttackCycleInterval;
            Debug.Log("[BossPhaseController] Phase 1 complete. Phase 2 Guardian Rage started.", this);
            return;
        }

        ResetFloorTilesForNextPhase();
        _debugCurrentPhase = BossCombatPhase.PhaseThree;
        Debug.Log("[BossPhaseController] Phase 2 complete. Phase 3 combo started.", this);
    }

    private void ResetFloorTilesForNextPhase()
    {
        if (_floorTileManager == null) _floorTileManager = GetComponent<FloorTileManager>();
        _floorTileManager?.ResetAllTilesForEncounter();
    }
}

/// <summary>Implemented Cat Sphinx combat phase progression.</summary>
public enum BossCombatPhase
{
    PhaseOne,
    PhaseTwo,
    PhaseThree
}
