using UnityEngine;

/// <summary>Locks Cat Sphinx combat while both Phase 7 Seals are simultaneously Active.</summary>
public sealed class BossStunController : MonoBehaviour
{
    private SealManager _sealManager;
    private BossController _bossController;
    private BossPawSlamAttack _pawSlamAttack;
    private BossAnimationController _animationController;
    private bool _pawSlamWasEnabled;

    /// <summary>True while the dual-seal condition prevents target selection and attacks.</summary>
    public bool IsStunned { get; private set; }

    private void Awake()
    {
        _sealManager = GetComponent<SealManager>();
        _bossController = GetComponent<BossController>();
        _pawSlamAttack = GetComponent<BossPawSlamAttack>();
        _animationController = GetComponent<BossAnimationController>();
    }

    private void Update()
    {
        if (_sealManager == null) _sealManager = GetComponent<SealManager>();
        bool shouldBeStunned = _sealManager != null && _sealManager.AreAllSealsActive;

        if (!IsStunned && shouldBeStunned) EnterStun();
        else if (IsStunned && !shouldBeStunned) ExitStun();
    }

    private void EnterStun()
    {
        IsStunned = true;
        _pawSlamWasEnabled = _pawSlamAttack != null && _pawSlamAttack.enabled;
        if (_pawSlamAttack != null) _pawSlamAttack.enabled = false;

        _bossController?.ResetToIdleAfterStun();
        _animationController?.SetStunned(true);
        Debug.Log("[BossStunController] Dual Seal condition met. Boss is Stunned.", this);
    }

    private void ExitStun()
    {
        IsStunned = false;
        _animationController?.SetStunned(false);
        if (_pawSlamAttack != null && _pawSlamWasEnabled) _pawSlamAttack.enabled = true;

        _bossController?.ResetToIdleAfterStun();
        Debug.Log("[BossStunController] Dual Seal condition ended. Boss combat restored.", this);
    }

    /// <summary>Releases the stun after the Core window has reset the dual-Seal condition.</summary>
    public void ReleaseStunAfterCoreTimeout()
    {
        if (!IsStunned) return;
        ExitStun();
    }
}
