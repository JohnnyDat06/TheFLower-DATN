using System.Collections;
using UnityEngine;

/// <summary>Runs a Phase 2 target-selected Slam that can emit either a target or alternating diagonal Shockwave.</summary>
public sealed class BossTargetSlamAttack : MonoBehaviour
{
    [Tooltip("Thoi gian red telegraph cua Target Slam truoc khi Boss dap.")]
    [SerializeField, Range(0.8f, 1.8f)] private float _telegraphDuration = 1.1f;
    [Tooltip("Thoi gian ha pose Boss tu telegraph xuong impact.")]
    [SerializeField, Min(0f)] private float _impactReturnDuration = 0.25f;
    [Tooltip("Van toc Shockwave cua Target Slam Phase 2.")]
    [SerializeField, Min(0.1f)] private float _shockwaveSpeed = 14f;
    [Tooltip("Be rong Shockwave cua Target Slam Phase 2.")]
    [SerializeField, Min(0.1f)] private float _shockwaveWidth = 5f;
    [Tooltip("Quang duong toi da cua Shockwave Target Slam.")]
    [SerializeField, Min(0.1f)] private float _shockwaveRange = 28f;

    private BossTargetSelector _targetSelector;
    private BossTargetIndicator _targetIndicator;
    private BossAnimationController _animationController;
    private BossArenaReferences _arenaReferences;
    private FloorPatternController _floorPatternController;
    private DiagonalShockwavePattern _diagonalPattern;
    private Coroutine _attackRoutine;

    /// <summary>True while the Phase 2 target telegraph, impact or recovery is running.</summary>
    public bool IsRunning => _attackRoutine != null;

    /// <summary>Starts one target-selected attack. When diagonal is true, the outgoing Shockwave alternates left/right.</summary>
    public bool TryStart(bool diagonal)
    {
        if (_attackRoutine != null || !TrySelectTarget(out Transform target)) return false;
        if (_arenaReferences == null) _arenaReferences = GetComponent<BossArenaReferences>();
        if (_arenaReferences == null || _arenaReferences.ShockwaveOrigin == null) return false;

        Vector3 targetDirection = Vector3.ProjectOnPlane(
            target.position - _arenaReferences.ShockwaveOrigin.position,
            Vector3.up).normalized;
        if (targetDirection.sqrMagnitude < 0.0001f) return false;

        if (diagonal)
        {
            if (_diagonalPattern == null) _diagonalPattern = GetComponent<DiagonalShockwavePattern>();
            targetDirection = _diagonalPattern != null
                ? _diagonalPattern.GetNextDirection(targetDirection)
                : targetDirection;
        }

        _attackRoutine = StartCoroutine(RunAttack(targetDirection, diagonal));
        return true;
    }

    private void Awake()
    {
        _targetSelector = GetComponent<BossTargetSelector>();
        _targetIndicator = GetComponent<BossTargetIndicator>();
        _animationController = GetComponent<BossAnimationController>();
        _arenaReferences = GetComponent<BossArenaReferences>();
        _floorPatternController = GetComponent<FloorPatternController>();
        _diagonalPattern = GetComponent<DiagonalShockwavePattern>();
    }

    private void OnDisable()
    {
        if (_attackRoutine != null) StopCoroutine(_attackRoutine);
        _attackRoutine = null;
        _animationController?.ResetPose();
    }

    private IEnumerator RunAttack(Vector3 direction, bool diagonal)
    {
        _animationController?.PlayPawSlam();
        _floorPatternController?.ShowTargetTelegraph(direction, _telegraphDuration);

        float elapsed = 0f;
        while (elapsed < _telegraphDuration)
        {
            elapsed += Time.deltaTime;
            if (_animationController != null && !_animationController.UsesAuthoredPawSlam)
                _animationController.SetTelegraphProgress(elapsed / _telegraphDuration);
            yield return null;
        }

        float descentElapsed = 0f;
        while (descentElapsed < _impactReturnDuration)
        {
            descentElapsed += Time.deltaTime;
            if (_animationController != null && !_animationController.UsesAuthoredPawSlam)
                _animationController.SetSlamDescentProgress(descentElapsed / _impactReturnDuration);
            yield return null;
        }

        _animationController?.ResetPose();
        ShockwaveController.Spawn(
            _arenaReferences.ShockwaveOrigin,
            direction,
            _shockwaveSpeed,
            _shockwaveWidth,
            _shockwaveRange);
        Debug.Log($"[BossTargetSlamAttack] {(diagonal ? "Diagonal" : "Target")} Slam impact.", this);
        _attackRoutine = null;
    }

    private bool TrySelectTarget(out Transform target)
    {
        target = null;
        if (_targetSelector == null) _targetSelector = GetComponent<BossTargetSelector>();
        if (_targetSelector == null || !_targetSelector.TrySelectNextTarget(out target)) return false;

        if (_targetIndicator == null) _targetIndicator = GetComponent<BossTargetIndicator>();
        _targetIndicator?.SetTarget(target);
        return true;
    }
}
