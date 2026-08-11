using UnityEngine;

/// <summary>
/// Validates the Phase 3 requirement that Rune_A and Rune_B receive separate Shockwave charges
/// within one short window before the normal Seal-to-Core flow can finish the encounter.
/// </summary>
public sealed class DualRuneChallengeController : MonoBehaviour
{
    [Tooltip("Rune dau tien bat buoc charge trong Phase 3. Tu tim theo ten Rune_A neu de trong.")]
    [SerializeField] private RuneController _runeA;
    [Tooltip("Rune thu hai bat buoc charge trong Phase 3. Tu tim theo ten Rune_B neu de trong.")]
    [SerializeField] private RuneController _runeB;
    [Tooltip("Khoang thoi gian toi da de hai Rune Phase 3 deu duoc Shockwave charge.")]
    [SerializeField, Range(0.5f, 3f)] private float _dualChargeWindow = 1.5f;
    [Tooltip("Trang thai debug cua thu thach Rune kep Phase 3.")]
    [SerializeField] private DualRuneChallengeState _debugState;

    private RuneManager _runeManager;
    private BossPhaseController _phaseController;
    private float _firstChargeTime;
    private bool _runeACharged;
    private bool _runeBCharged;

    /// <summary>True after Rune_A and Rune_B have both been charged within the configured window.</summary>
    public bool IsChallengeComplete => _debugState == DualRuneChallengeState.Complete;

    private void Awake()
    {
        _runeManager = GetComponent<RuneManager>();
        _phaseController = GetComponent<BossPhaseController>();
        FindRequiredRunes();
    }

    private void OnEnable()
    {
        if (_runeManager == null) _runeManager = GetComponent<RuneManager>();
        if (_runeManager != null) _runeManager.RuneCharged += HandleRuneCharged;
    }

    private void OnDisable()
    {
        if (_runeManager != null) _runeManager.RuneCharged -= HandleRuneCharged;
    }

    private void Update()
    {
        if (!IsPhaseThree())
        {
            ResetChallenge();
            return;
        }

        if (_debugState == DualRuneChallengeState.WaitingForSecondRune &&
            Time.time >= _firstChargeTime + _dualChargeWindow)
        {
            _runeA?.ResetRune();
            _runeB?.ResetRune();
            ResetChallenge();
            Debug.Log("[DualRuneChallenge] Failed: both Runes reset because they were not charged in time.", this);
        }
    }

    private void HandleRuneCharged(RuneController rune)
    {
        if (!IsPhaseThree() || _debugState == DualRuneChallengeState.Complete || rune == null) return;
        if (rune != _runeA && rune != _runeB) return;

        if (_debugState == DualRuneChallengeState.Inactive)
        {
            _firstChargeTime = Time.time;
            _debugState = DualRuneChallengeState.WaitingForSecondRune;
        }

        if (rune == _runeA) _runeACharged = true;
        if (rune == _runeB) _runeBCharged = true;

        if (!_runeACharged || !_runeBCharged) return;

        _debugState = DualRuneChallengeState.Complete;
        Debug.Log("[DualRuneChallenge] Complete: Rune_A and Rune_B charged together. Activate both Seals.", this);
    }

    private bool IsPhaseThree() =>
        _phaseController != null && _phaseController.CurrentPhase == BossCombatPhase.PhaseThree;

    private void FindRequiredRunes()
    {
        if (_runeA != null && _runeB != null) return;

        foreach (RuneController rune in GetComponentsInChildren<RuneController>(true))
        {
            if (rune.name == "Rune_A") _runeA = rune;
            else if (rune.name == "Rune_B") _runeB = rune;
        }

        if (_runeA == null || _runeB == null)
            Debug.LogError("[DualRuneChallenge] Rune_A or Rune_B is missing from BossArena_Architecture.", this);
    }

    private void ResetChallenge()
    {
        if (_debugState == DualRuneChallengeState.Inactive && !_runeACharged && !_runeBCharged) return;

        _debugState = DualRuneChallengeState.Inactive;
        _firstChargeTime = 0f;
        _runeACharged = false;
        _runeBCharged = false;
    }

    /// <summary>Clears the Phase 3 Rune timing challenge for a complete encounter retry.</summary>
    public void ResetEncounterState()
    {
        ResetChallenge();
        enabled = true;
    }
}

/// <summary>Runtime status used to inspect the Phase 3 two-Rune timing challenge.</summary>
public enum DualRuneChallengeState
{
    Inactive,
    WaitingForSecondRune,
    Complete
}
