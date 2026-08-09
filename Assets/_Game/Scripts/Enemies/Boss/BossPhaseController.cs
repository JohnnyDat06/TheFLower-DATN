using UnityEngine;

/// <summary>Runs the automatic Phase 1 loop and records the first Core Hit before Phase 2 is implemented.</summary>
public sealed class BossPhaseController : MonoBehaviour
{
    [Tooltip("Cau hinh mau Core va nhip tan cong cua Phase 1.")]
    [SerializeField] private BossPhaseData _phaseOneData = new();
    [Tooltip("Mau Core hien tai, hien thi de debug trong Inspector.")]
    [SerializeField] private int _debugCurrentCoreHealth;
    [Tooltip("So Core Hit da duoc ghi nhan trong Phase 1, hien thi de debug trong Inspector.")]
    [SerializeField] private int _debugPhaseOneCoreHits;

    private BossController _bossController;
    private BossCoreController _coreController;
    private BossStunController _stunController;
    private float _nextAttackTime;

    /// <summary>True after Core Hit #1 has completed Phase 1 and before Phase 2 is implemented.</summary>
    public bool IsPhaseTwoPlaceholder { get; private set; }

    /// <summary>Remaining encounter Core-health after valid Core Hits.</summary>
    public int CurrentCoreHealth => _debugCurrentCoreHealth;

    private void Awake()
    {
        _bossController = GetComponent<BossController>();
        _coreController = GetComponent<BossCoreController>();
        _stunController = GetComponent<BossStunController>();
        _debugCurrentCoreHealth = _phaseOneData.MaxCoreHealth;
        if (_coreController != null) _coreController.CoreHit += HandleCoreHit;
        _nextAttackTime = Time.time + _phaseOneData.AttackCycleInterval;
    }

    private void OnDestroy()
    {
        if (_coreController != null) _coreController.CoreHit -= HandleCoreHit;
    }

    private void Update()
    {
        if (IsPhaseTwoPlaceholder || _bossController == null || _stunController == null) return;
        if (_stunController.IsStunned || _bossController.CurrentState != BossState.Idle) return;
        if (Time.time < _nextAttackTime) return;

        _bossController.TryStartPawSlamCycle();
        _nextAttackTime = Time.time + _phaseOneData.AttackCycleInterval;
    }

    private void HandleCoreHit()
    {
        if (IsPhaseTwoPlaceholder) return;

        _debugCurrentCoreHealth = Mathf.Max(0, _debugCurrentCoreHealth - 1);
        _debugPhaseOneCoreHits++;
        Debug.Log($"[BossPhaseController] Core Hit #{_debugPhaseOneCoreHits}. Boss Core-health: {_debugCurrentCoreHealth}/{_phaseOneData.MaxCoreHealth}.", this);

        if (_debugPhaseOneCoreHits < _phaseOneData.PhaseOneCoreHitsToComplete) return;

        IsPhaseTwoPlaceholder = true;
        Debug.Log("[BossPhaseController] Phase 1 complete. Boss is now in the Phase 2 placeholder state.", this);
    }
}
