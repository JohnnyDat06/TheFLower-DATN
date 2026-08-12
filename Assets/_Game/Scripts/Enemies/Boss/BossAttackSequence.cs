using UnityEngine;

/// <summary>Owns the ordered Phase 3 attack combo and prevents its individual attacks from overlapping.</summary>
public sealed class BossAttackSequence : MonoBehaviour
{
    [Tooltip("Khoang nghi ngan giua cac don trong combo Phase 3.")]
    [SerializeField, Min(0.1f)] private float _comboStepDelay = 0.5f;

    private BossPhaseController _phaseController;
    private BossStunController _stunController;
    private BossTargetSlamAttack _targetSlamAttack;
    private BossDoublePawAttack _doublePawAttack;
    private BossEarthquakeAttack _earthquakeAttack;
    private PhaseThreeStep _nextStep;
    private bool _waitingForAttack;
    private bool _doublePawFallbackRunning;
    private float _nextStepTime;

    private void Awake()
    {
        _phaseController = GetComponent<BossPhaseController>();
        _stunController = GetComponent<BossStunController>();
        _targetSlamAttack = GetComponent<BossTargetSlamAttack>();
        _doublePawAttack = GetComponent<BossDoublePawAttack>();
        _earthquakeAttack = GetComponent<BossEarthquakeAttack>();
    }

    private void Update()
    {
        if (BossEncounterManager.Instance == null || !BossEncounterManager.Instance.IsActive)
            return;

        if (_phaseController == null || _phaseController.CurrentPhase != BossCombatPhase.PhaseThree ||
            (_stunController != null && _stunController.IsStunned))
            return;

        if (_waitingForAttack)
        {
            if (IsCurrentAttackRunning()) return;

            _waitingForAttack = false;
            _doublePawFallbackRunning = false;
            _nextStepTime = Time.time + _comboStepDelay;
            _nextStep = (PhaseThreeStep)(((int)_nextStep + 1) % 4);
            return;
        }

        if (_phaseController.IsDebugManualAttackMode)
        {
            if (!Input.GetKeyDown(KeyCode.V)) return;
        }
        else if (Time.time < _nextStepTime)
            return;

        if (!TryStartCurrentStep()) return;
        _waitingForAttack = true;
    }

    private bool TryStartCurrentStep()
    {
        bool started = _nextStep switch
        {
            PhaseThreeStep.LeftPaw => _targetSlamAttack != null && _targetSlamAttack.TryStart(false),
            PhaseThreeStep.RightPaw => _targetSlamAttack != null && _targetSlamAttack.TryStart(false),
            PhaseThreeStep.DoublePaw => TryStartDoublePawOrSingleTargetFallback(),
            PhaseThreeStep.Earthquake => _earthquakeAttack != null && _earthquakeAttack.TryStart(),
            _ => false
        };

        if (started) Debug.Log($"[BossAttackSequence] Phase 3 step: {_nextStep}.", this);
        return started;
    }

    private bool IsCurrentAttackRunning() => _nextStep switch
    {
        PhaseThreeStep.LeftPaw or PhaseThreeStep.RightPaw => _targetSlamAttack != null && _targetSlamAttack.IsRunning,
        PhaseThreeStep.DoublePaw => (_doublePawAttack != null && _doublePawAttack.IsRunning) ||
                                   (_doublePawFallbackRunning && _targetSlamAttack != null && _targetSlamAttack.IsRunning),
        PhaseThreeStep.Earthquake => _earthquakeAttack != null && _earthquakeAttack.IsRunning,
        _ => false
    };

    private bool TryStartDoublePawOrSingleTargetFallback()
    {
        _doublePawFallbackRunning = false;
        if (_doublePawAttack != null && _doublePawAttack.TryStart()) return true;

        // A downed player is not a legal Double Paw target. Continue the Phase 3 combo by
        // directing one normal target slam at the remaining living player instead of stalling.
        _doublePawFallbackRunning = _targetSlamAttack != null && _targetSlamAttack.TryStart(false);
        if (_doublePawFallbackRunning)
            Debug.Log("[BossAttackSequence] Double Paw changed to Single Target Slam because only one player is alive.", this);
        return _doublePawFallbackRunning;
    }

    /// <summary>Clears Phase 3 combo progress before a full encounter retry.</summary>
    public void ResetEncounterState()
    {
        _nextStep = PhaseThreeStep.LeftPaw;
        _waitingForAttack = false;
        _doublePawFallbackRunning = false;
        _nextStepTime = 0f;
        enabled = true;
    }
}

/// <summary>Fixed order of the Phase 3 attack combo.</summary>
public enum PhaseThreeStep
{
    LeftPaw,
    RightPaw,
    DoublePaw,
    Earthquake
}
