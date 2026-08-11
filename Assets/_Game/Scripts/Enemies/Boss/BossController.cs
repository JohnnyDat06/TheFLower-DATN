using UnityEngine;

/// <summary>
/// Owns the Cat Sphinx's observable state machine and starts the current attack cycle.
/// </summary>
public sealed class BossController : MonoBehaviour
{
    [SerializeField] private BossState _debugCurrentState = BossState.Idle;
    [SerializeField] private Transform _debugCurrentTarget;

    private BossStateMachine _stateMachine;
    private BossTargetSelector _targetSelector;
    private BossTargetIndicator _targetIndicator;
    private BossPawSlamAttack _pawSlamAttack;
    private BossStunController _stunController;

    /// <summary>Current boss state for debug UI and later phase-specific controllers.</summary>
    public BossState CurrentState
    {
        get
        {
            EnsureStateMachine();
            return _stateMachine.CurrentState;
        }
    }

    /// <summary>The player selected for the current non-combat cycle, if any.</summary>
    public Transform CurrentTarget => _debugCurrentTarget;

    /// <summary>True while the Phase 8 dual-seal controller locks boss combat.</summary>
    public bool IsStunned => _stunController != null && _stunController.IsStunned;

    private void Awake()
    {
        _targetSelector = GetComponent<BossTargetSelector>();
        _targetIndicator = GetComponent<BossTargetIndicator>();
        _pawSlamAttack = GetComponent<BossPawSlamAttack>();
        _stunController = GetComponent<BossStunController>();
        CreateStateMachine(BossState.Idle);
    }

    private void OnDestroy()
    {
        if (_stateMachine != null) _stateMachine.StateChanged -= HandleStateChanged;
    }

    /// <summary>Attempts one legal Phase 1 transition.</summary>
    public bool TryTransitionTo(BossState nextState)
    {
        EnsureStateMachine();
        if (IsStunned && nextState != BossState.Defeated)
        {
            Debug.LogWarning($"[BossController] Rejected {nextState}; boss is Stunned.", this);
            return false;
        }

        bool transitioned = _stateMachine.TryTransitionTo(nextState);
        if (!transitioned)
            Debug.LogWarning($"[BossController] Rejected transition: {CurrentState} -> {nextState}", this);

        return transitioned;
    }

    [ContextMenu("Debug/Advance State")]
    private void AdvanceStateForDebug()
    {
        BossState nextState = CurrentState switch
        {
            BossState.Idle => BossState.SelectTarget,
            BossState.SelectTarget => BossState.Telegraph,
            BossState.Telegraph => BossState.Recovery,
            BossState.Recovery => BossState.Idle,
            _ => BossState.Defeated
        };

        TryTransitionTo(nextState);
    }

    [ContextMenu("Debug/Run Paw Slam Test Cycle")]
    private void RunTestCycleForDebug()
    {
        TryStartPawSlamCycle();
    }

    /// <summary>Starts one target-selection and Paw Slam cycle from Idle.</summary>
    public bool TryStartPawSlamCycle()
    {
        if (IsStunned)
        {
            Debug.LogWarning("[BossController] Paw Slam cycle rejected; boss is Stunned.", this);
            return false;
        }

        if (CurrentState != BossState.Idle)
        {
            Debug.LogWarning($"[BossController] Paw Slam cycle requires Idle; current state is {CurrentState}.", this);
            return false;
        }

        return TryTransitionTo(BossState.SelectTarget) &&
               TryTransitionTo(BossState.Telegraph);
    }

    /// <summary>Permanently ends Cat Sphinx combat after the final valid Core Hit.</summary>
    public void Defeat()
    {
        EnsureStateMachine();
        if (CurrentState == BossState.Defeated) return;

        _debugCurrentTarget = null;
        _targetIndicator?.SetTarget(null);
        TryTransitionTo(BossState.Defeated);
    }

    /// <summary>Applies an authoritative Host state without starting gameplay logic on Client.</summary>
    public void ApplyNetworkState(BossState state, Transform target)
    {
        EnsureStateMachine();
        if (_stateMachine.CurrentState != state) CreateStateMachine(state);

        _debugCurrentTarget = target;
        if (_targetIndicator == null) _targetIndicator = GetComponent<BossTargetIndicator>();
        _targetIndicator?.SetTarget(target);
    }

    [ContextMenu("Debug/Force Defeated")]
    private void ForceDefeatedForDebug()
    {
        TryTransitionTo(BossState.Defeated);
    }

    private void HandleStateChanged(BossState previousState, BossState nextState)
    {
        _debugCurrentState = nextState;
        Debug.Log($"[BossController] {previousState} -> {nextState}", this);

        if (nextState == BossState.SelectTarget) SelectTargetForCycle();
        if (nextState == BossState.Telegraph) BeginPawSlamFromTelegraph();
    }

    private void EnsureStateMachine()
    {
        if (_stateMachine == null) CreateStateMachine(BossState.Idle);
    }

    private void CreateStateMachine(BossState initialState)
    {
        if (_stateMachine != null) _stateMachine.StateChanged -= HandleStateChanged;

        _stateMachine = new BossStateMachine(initialState);
        _stateMachine.StateChanged += HandleStateChanged;
        _debugCurrentState = initialState;
    }

    private void SelectTargetForCycle()
    {
        if (_targetSelector == null) _targetSelector = GetComponent<BossTargetSelector>();
        if (_targetIndicator == null) _targetIndicator = GetComponent<BossTargetIndicator>();

        if (_targetSelector == null)
        {
            Debug.LogError("[BossController] BossTargetSelector is missing.", this);
            return;
        }

        if (!_targetSelector.TrySelectNextTarget(out Transform target))
        {
            _debugCurrentTarget = null;
            _targetIndicator?.SetTarget(null);
            Debug.LogWarning("[BossController] No valid player target is available.", this);
            return;
        }

        _debugCurrentTarget = target;
        _targetIndicator?.SetTarget(target);
        Debug.Log($"[BossController] Target selected: {target.name}", this);
    }

    private void BeginPawSlamFromTelegraph()
    {
        if (_pawSlamAttack == null) _pawSlamAttack = GetComponent<BossPawSlamAttack>();
        if (_pawSlamAttack != null && _pawSlamAttack.TryBeginFromTelegraph()) return;

        Debug.LogError("[BossController] BossPawSlamAttack is missing or could not start from Telegraph.", this);
    }

    /// <summary>Clears any in-progress combat state when Phase 8 releases its Stun lock.</summary>
    public void ResetToIdleAfterStun()
    {
        EnsureStateMachine();
        _stateMachine.ResetToIdle();
    }
}
