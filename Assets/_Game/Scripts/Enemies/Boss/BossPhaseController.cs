using UnityEngine;

/// <summary>Runs Phase 1 and Phase 2 combat loops, then records the Phase 3 placeholder after Core Hit #2.</summary>
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

    private BossController _bossController;
    private BossCoreController _coreController;
    private BossStunController _stunController;
    private BossTargetSlamAttack _targetSlamAttack;
    private float _nextAttackTime;
    private bool _nextPhaseTwoAttackIsDiagonal;

    /// <summary>Current implemented combat phase, including the non-combat Phase 3 placeholder.</summary>
    public BossCombatPhase CurrentPhase => _debugCurrentPhase;

    /// <summary>Remaining encounter Core-health after valid Core Hits.</summary>
    public int CurrentCoreHealth => _debugCurrentCoreHealth;

    private void Awake()
    {
        _bossController = GetComponent<BossController>();
        _coreController = GetComponent<BossCoreController>();
        _stunController = GetComponent<BossStunController>();
        _targetSlamAttack = GetComponent<BossTargetSlamAttack>();
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
        if (_debugCurrentPhase == BossCombatPhase.PhaseThreePlaceholder ||
            _bossController == null ||
            _stunController == null ||
            _stunController.IsStunned ||
            Time.time < _nextAttackTime)
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
        if (_targetSlamAttack == null || _targetSlamAttack.IsRunning) return;

        bool useDiagonal = _nextPhaseTwoAttackIsDiagonal;
        if (!_targetSlamAttack.TryStart(useDiagonal)) return;

        _nextPhaseTwoAttackIsDiagonal = !_nextPhaseTwoAttackIsDiagonal;
        _nextAttackTime = Time.time + _phaseData.PhaseTwoAttackCycleInterval;
    }

    private void HandleCoreHit()
    {
        if (_debugCurrentPhase == BossCombatPhase.PhaseThreePlaceholder) return;

        _debugCurrentCoreHealth = Mathf.Max(0, _debugCurrentCoreHealth - 1);
        _debugCoreHitCount++;
        Debug.Log($"[BossPhaseController] Core Hit #{_debugCoreHitCount}. Boss Core-health: {_debugCurrentCoreHealth}/{_phaseData.MaxCoreHealth}.", this);

        if (_debugCurrentPhase == BossCombatPhase.PhaseOne)
        {
            _debugCurrentPhase = BossCombatPhase.PhaseTwo;
            _nextAttackTime = Time.time + _phaseData.PhaseTwoAttackCycleInterval;
            Debug.Log("[BossPhaseController] Phase 1 complete. Phase 2 Guardian Rage started.", this);
            return;
        }

        _debugCurrentPhase = BossCombatPhase.PhaseThreePlaceholder;
        Debug.Log("[BossPhaseController] Phase 2 complete. Boss is now in the Phase 3 placeholder state.", this);
    }
}

/// <summary>Implemented boss phase progression before Phase 3 attack combos are added.</summary>
public enum BossCombatPhase
{
    PhaseOne,
    PhaseTwo,
    PhaseThreePlaceholder
}
