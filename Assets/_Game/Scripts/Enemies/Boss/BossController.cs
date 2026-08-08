using UnityEngine;

/// <summary>
/// Phase 1 owner for the Cat Sphinx's observable non-combat state machine.
/// </summary>
public sealed class BossController : MonoBehaviour
{
    [SerializeField] private BossState _debugCurrentState = BossState.Idle;
    [SerializeField] private Transform _debugCurrentTarget;

    private BossStateMachine _stateMachine;
    private BossTargetSelector _targetSelector;
    private BossTargetIndicator _targetIndicator;

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

    private void Awake()
    {
        _targetSelector = GetComponent<BossTargetSelector>();
        _targetIndicator = GetComponent<BossTargetIndicator>();
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

    [ContextMenu("Debug/Run Target Test Cycle")]
    private void RunTestCycleForDebug()
    {
        if (CurrentState != BossState.Idle)
        {
            Debug.LogWarning($"[BossController] Test cycle requires Idle; current state is {CurrentState}.", this);
            return;
        }

        TryTransitionTo(BossState.SelectTarget);
        TryTransitionTo(BossState.Telegraph);
        TryTransitionTo(BossState.Recovery);
        TryTransitionTo(BossState.Idle);
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
}
