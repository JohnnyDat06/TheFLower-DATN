using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Runs one Paw Slam sequence: telegraph, a Shockwave prototype, recovery and idle.
/// </summary>
public sealed class BossPawSlamAttack : MonoBehaviour
{
    [Tooltip("Thời gian boss nâng lên để báo trước cú đập.")]
    [SerializeField, Range(1.2f, 1.8f)] private float _telegraphDuration = 1.5f;
    [Tooltip("Thời gian boss hạ từ pose báo trước xuống pose impact.")]
    [SerializeField, Min(0f)] private float _impactReturnDuration = 0.25f;
    [Tooltip("Thời gian nghỉ sau impact trước khi boss có thể quay lại Idle.")]
    [SerializeField, Min(0f)] private float _recoveryDuration = 0.8f;
    [Header("Phase 4 Shockwave Prototype")]
    [Tooltip("Vận tốc Shockwave prototype di chuyển vào arena, tính theo mét mỗi giây.")]
    [SerializeField, Min(0.1f)] private float _shockwaveSpeed = 12f;
    [Tooltip("Bề rộng của dải Shockwave prototype.")]
    [SerializeField, Min(0.1f)] private float _shockwaveWidth = 5f;
    [Tooltip("Quãng đường tối đa Shockwave đi trước khi tự hủy.")]
    [SerializeField, Min(0.1f)] private float _shockwaveRange = 28f;

    private BossController _bossController;
    private BossAnimationController _animationController;
    private BossArenaReferences _arenaReferences;
    private Coroutine _attackRoutine;

    /// <summary>Raised exactly once when the paw reaches the slam impact moment.</summary>
    public event Action SlamImpact;

    /// <summary>Whether a telegraph, impact or recovery is currently in progress.</summary>
    public bool IsRunning => _attackRoutine != null;

    /// <summary>Telegraph duration replicated to remote peers by BossNetworkState.</summary>
    public float TelegraphDuration => _telegraphDuration;

    /// <summary>Slam descent duration replicated to remote peers by BossNetworkState.</summary>
    public float ImpactReturnDuration => _impactReturnDuration;

    private void Awake()
    {
        _bossController = GetComponent<BossController>();
        _animationController = GetComponent<BossAnimationController>();
        _arenaReferences = GetComponent<BossArenaReferences>();
    }

    private void OnDisable()
    {
        if (_attackRoutine != null) StopCoroutine(_attackRoutine);
        _attackRoutine = null;
        _animationController?.ResetPose();
    }

    /// <summary>Starts the slam after BossController has entered Telegraph.</summary>
    public bool TryBeginFromTelegraph()
    {
        if (_attackRoutine != null || _bossController == null ||
            _bossController.CurrentState != BossState.Telegraph)
            return false;

        _attackRoutine = StartCoroutine(RunAttackRoutine());
        return true;
    }

    private IEnumerator RunAttackRoutine()
    {
        _animationController?.PlayPawSlam();

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
        RaiseImpactOnce();
        _bossController.TryTransitionTo(BossState.Recovery);

        if (_recoveryDuration > 0f) yield return new WaitForSeconds(_recoveryDuration);

        _attackRoutine = null;
        if (_bossController.CurrentState == BossState.Recovery)
            _bossController.TryTransitionTo(BossState.Idle);
    }

    private void RaiseImpactOnce()
    {
        Debug.Log("[BossPawSlamAttack] Slam impact.", this);
        SpawnShockwavePrototype();
        SlamImpact?.Invoke();
    }

    private void SpawnShockwavePrototype()
    {
        if (_arenaReferences == null) _arenaReferences = GetComponent<BossArenaReferences>();
        if (_arenaReferences == null || _arenaReferences.ShockwaveOrigin == null)
        {
            Debug.LogError("[BossPawSlamAttack] Shockwave Origin is missing.", this);
            return;
        }

        Vector3 direction = _arenaReferences.ShockwaveDirection;
        if (direction.sqrMagnitude < 0.0001f)
        {
            Debug.LogError("[BossPawSlamAttack] Shockwave direction markers are invalid.", this);
            return;
        }

        ShockwaveController.Spawn(
            _arenaReferences.ShockwaveOrigin,
            direction,
            _shockwaveSpeed,
            _shockwaveWidth,
            _shockwaveRange);
    }

    private void OnDrawGizmosSelected()
    {
        BossArenaReferences references = GetComponent<BossArenaReferences>();
        if (references == null || references.ShockwaveOrigin == null) return;

        Vector3 direction = references.ShockwaveDirection;
        if (direction.sqrMagnitude < 0.0001f) return;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(references.ShockwaveOrigin.position,
            references.ShockwaveOrigin.position + direction * _shockwaveRange);
    }
}
