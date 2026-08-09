using UnityEngine;

/// <summary>
/// Presents the Cat Sphinx's Phase 3 telegraph and impact poses.
/// A separate paw transform can be assigned when the art rig exposes one.
/// </summary>
public sealed class BossAnimationController : MonoBehaviour
{
    private const string PawSlamStateName = "PawSlam";

    [SerializeField] private Animator _animator;
    [SerializeField] private Transform _telegraphVisual;
    [SerializeField] private Vector3 _raisedLocalPositionOffset = new(0f, 0.75f, 0f);
    [SerializeField] private Vector3 _raisedLocalEulerOffset = new(-8f, 0f, 0f);

    private Vector3 _restLocalPosition;
    private Quaternion _restLocalRotation;
    private bool _hasRestPose;

    /// <summary>True when the Cat Sphinx Animator has the generated Paw Slam state.</summary>
    public bool UsesAuthoredPawSlam => _animator != null && _animator.runtimeAnimatorController != null;

    private void Awake()
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        ResolveTelegraphVisual();
        CaptureRestPose();
    }

    /// <summary>Starts the authored Paw Slam clip when the Cat Sphinx rig is available.</summary>
    public void PlayPawSlam()
    {
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            _animator.Play(PawSlamStateName, 0, 0f);
            return;
        }

        SetTelegraphProgress(0f);
    }

    /// <summary>Moves the boss from its rest pose into the raised telegraph pose.</summary>
    public void SetTelegraphProgress(float progress)
    {
        if (!CaptureRestPose()) return;

        float easedProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
        _telegraphVisual.localPosition = Vector3.Lerp(
            _restLocalPosition,
            _restLocalPosition + _raisedLocalPositionOffset,
            easedProgress);
        _telegraphVisual.localRotation = Quaternion.Slerp(
            _restLocalRotation,
            _restLocalRotation * Quaternion.Euler(_raisedLocalEulerOffset),
            easedProgress);
    }

    /// <summary>Moves the raised boss pose smoothly back to ground for the slam descent.</summary>
    public void SetSlamDescentProgress(float progress)
    {
        if (!CaptureRestPose()) return;

        float easedProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
        _telegraphVisual.localPosition = Vector3.Lerp(
            _restLocalPosition + _raisedLocalPositionOffset,
            _restLocalPosition,
            easedProgress);
        _telegraphVisual.localRotation = Quaternion.Slerp(
            _restLocalRotation * Quaternion.Euler(_raisedLocalEulerOffset),
            _restLocalRotation,
            easedProgress);
    }

    /// <summary>Returns the boss to its rest pose after the slam impact.</summary>
    public void ResetPose()
    {
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            _animator.Play("Idle", 0, 0f);
            return;
        }

        if (!CaptureRestPose()) return;
        _telegraphVisual.localPosition = _restLocalPosition;
        _telegraphVisual.localRotation = _restLocalRotation;
    }

    private bool CaptureRestPose()
    {
        ResolveTelegraphVisual();
        if (_telegraphVisual == null) return false;
        if (_hasRestPose) return true;

        _restLocalPosition = _telegraphVisual.localPosition;
        _restLocalRotation = _telegraphVisual.localRotation;
        _hasRestPose = true;
        return true;
    }

    private void ResolveTelegraphVisual()
    {
        if (_telegraphVisual != null) return;

        Transform bossModel = transform.Find("CatFinalBoss");
        if (bossModel != null) _telegraphVisual = bossModel;
    }
}
