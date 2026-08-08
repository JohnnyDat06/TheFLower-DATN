using UnityEngine;

/// <summary>
/// Phase 1 owner for the Cat Sphinx's observable non-combat state machine.
/// </summary>
public sealed class BossController : MonoBehaviour
{
    [SerializeField] private BossState _debugCurrentState = BossState.Idle;

    private BossStateMachine _stateMachine;

    /// <summary>Current boss state for debug UI and later phase-specific controllers.</summary>
    public BossState CurrentState
    {
        get
        {
            EnsureStateMachine();
            return _stateMachine.CurrentState;
        }
    }

    private void Awake()
    {
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

    [ContextMenu("Debug/Run Phase 1 Test Cycle")]
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
}
