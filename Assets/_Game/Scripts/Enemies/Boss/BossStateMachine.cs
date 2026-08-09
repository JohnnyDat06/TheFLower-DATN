using System;

/// <summary>
/// Encapsulates the legal Phase 1 Cat Sphinx state transitions.
/// </summary>
public sealed class BossStateMachine
{
    public event Action<BossState, BossState> StateChanged;

    public BossState CurrentState { get; private set; }

    public BossStateMachine(BossState initialState)
    {
        CurrentState = initialState;
    }

    /// <summary>
    /// Transitions to a legal state. Defeated is a terminal state for the current encounter.
    /// </summary>
    public bool TryTransitionTo(BossState nextState)
    {
        if (!CanTransitionTo(nextState)) return false;

        BossState previousState = CurrentState;
        CurrentState = nextState;
        StateChanged?.Invoke(previousState, nextState);
        return true;
    }

    /// <summary>Returns a non-defeated encounter to Idle when an external combat lock is released.</summary>
    public bool ResetToIdle()
    {
        if (CurrentState is BossState.Defeated or BossState.Idle) return false;

        BossState previousState = CurrentState;
        CurrentState = BossState.Idle;
        StateChanged?.Invoke(previousState, CurrentState);
        return true;
    }

    private bool CanTransitionTo(BossState nextState)
    {
        if (CurrentState == BossState.Defeated) return false;
        if (nextState == BossState.Defeated) return true;

        return (CurrentState, nextState) switch
        {
            (BossState.Idle, BossState.SelectTarget) => true,
            (BossState.SelectTarget, BossState.Telegraph) => true,
            (BossState.Telegraph, BossState.Recovery) => true,
            (BossState.Recovery, BossState.Idle) => true,
            _ => false
        };
    }
}
