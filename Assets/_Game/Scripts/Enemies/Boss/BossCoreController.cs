using System;
using UnityEngine;

/// <summary>Mở hai điểm Core khi Boss bị choáng và đóng chu kỳ nếu người chơi không kịp tương tác.</summary>
public sealed class BossCoreController : MonoBehaviour
{
    [Tooltip("Số giây hai Core chờ người chơi tương tác trước khi tự đóng và khôi phục Rune/Seal.")]
    [SerializeField, Range(6f, 8f)] private float _exposedDuration = 7f;

    private BossStunController _stunController;
    private SealManager _sealManager;
    private RuneManager _runeManager;
    private float _exposedUntil;

    /// <summary>Raised once when two valid Core points are activated within the Phase 10 sync window.</summary>
    public event Action CoreHit;

    /// <summary>Current Core state.</summary>
    public BossCoreState State { get; private set; } = BossCoreState.Locked;

    /// <summary>True only while the current exposed Core can receive one dual-player activation.</summary>
    public bool CanAcceptDualActivation => State == BossCoreState.Exposed;

    private void Awake()
    {
        CacheDependencies();
    }

    private void Update()
    {
        CacheDependencies();

        if (State == BossCoreState.Locked && _stunController != null && _stunController.IsStunned)
        {
            ExposeCore();
            return;
        }

        if (State != BossCoreState.Exposed) return;

        if (_stunController == null || !_stunController.IsStunned)
        {
            LockCore();
            return;
        }

        if (Time.time >= _exposedUntil) CloseAfterTimeout();
    }

    private void ExposeCore()
    {
        State = BossCoreState.Exposed;
        float descentDuration = GetLongestCoreDescentDuration();
        _exposedUntil = Time.time + descentDuration + _exposedDuration;
        Debug.Log($"[BossCoreController] Core exposed for {_exposedDuration:0.0} seconds.", this);
    }

    private void CloseAfterTimeout()
    {
        ResetPuzzleCycle();
        LockCore();
        Debug.Log("[BossCoreController] Core exposure expired. Rune and Seal cycle reset.", this);
    }

    /// <summary>Accepts one completed dual activation and closes this Core before a later phase consumes the hit.</summary>
    public bool TryRegisterCoreHit()
    {
        if (!CanAcceptDualActivation) return false;

        CoreHit?.Invoke();
        ResetPuzzleCycle();
        LockCore();
        Debug.Log("[BossCoreController] Core Hit registered.", this);
        return true;
    }

    /// <summary>Applies the Host-owned Core state and visibility on Client.</summary>
    public void ApplyNetworkState(BossCoreState state)
    {
        State = state;
        _exposedUntil = 0f;
    }

    /// <summary>Locks and hides the Core for a complete boss encounter retry.</summary>
    public void ResetEncounterState()
    {
        LockCore();
        enabled = true;
    }

    private void ResetPuzzleCycle()
    {
        _sealManager?.ResetAllSealsForCycle();
        _runeManager?.ResetAllRunesForCycle();
        _stunController?.ReleaseStunAfterCoreTimeout();
    }

    private void LockCore()
    {
        State = BossCoreState.Locked;
        _exposedUntil = 0f;
    }

    private void CacheDependencies()
    {
        _stunController ??= GetComponent<BossStunController>();
        _sealManager ??= GetComponent<SealManager>();
        _runeManager ??= GetComponent<RuneManager>();
    }

    private float GetLongestCoreDescentDuration()
    {
        float longestDuration = 0f;
        foreach (CoreInteractionPoint point in GetComponentsInChildren<CoreInteractionPoint>(true))
            longestDuration = Mathf.Max(longestDuration, point.DescentDuration);
        return longestDuration;
    }
}
