using System.Collections;
using UnityEngine;

/// <summary>
/// Presents the Cat Sphinx's Phase 3 telegraph, impact and Stunned poses.
/// A separate paw transform can be assigned when the art rig exposes one.
/// </summary>
public sealed class BossAnimationController : MonoBehaviour
{
    private const string PawSlamStateName = "PawSlam";

    [Tooltip("Animator cua boss, chi dung khi co Runtime Animator Controller hop le.")]
    [SerializeField] private Animator _animator;
    [Tooltip("Model boss duoc di chuyen cho telegraph, slam va Stunned pose.")]
    [SerializeField] private Transform _telegraphVisual;
    [Tooltip("Do nang local cua model trong telegraph Paw Slam.")]
    [SerializeField] private Vector3 _raisedLocalPositionOffset = new(0f, 0.75f, 0f);
    [Tooltip("Do nghieng local cua model trong telegraph Paw Slam.")]
    [SerializeField] private Vector3 _raisedLocalEulerOffset = new(-8f, 0f, 0f);
    [Tooltip("Do ha thap local cua model boss trong pose Stunned.")]
    [SerializeField] private Vector3 _stunnedLocalPositionOffset = new(0f, -0.2f, 0f);
    [Tooltip("Do nghieng local cua model boss trong pose Stunned.")]
    [SerializeField] private Vector3 _stunnedLocalEulerOffset = new(8f, 0f, 0f);
    [Tooltip("Thoi gian noi suy de boss ha xuong/hoi phuc pose Stunned, tranh dich chuyen tuc thi.")]
    [SerializeField, Min(0.05f)] private float _stunnedTransitionDuration = 0.75f;

    private Vector3 _restLocalPosition;
    private Quaternion _restLocalRotation;
    private bool _hasRestPose;
    private Coroutine _stunnedPoseTransition;

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
        StopStunnedPoseTransition();
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
        StopStunnedPoseTransition();
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

        StopStunnedPoseTransition();
        if (!CaptureRestPose()) return;
        _telegraphVisual.localPosition = _restLocalPosition;
        _telegraphVisual.localRotation = _restLocalRotation;
    }

    /// <summary>Smoothly lowers the boss into, or restores it from, the Phase 8 Stunned pose.</summary>
    public void SetStunned(bool isStunned)
    {
        if (!CaptureRestPose()) return;

        StopStunnedPoseTransition();
        Vector3 targetPosition = isStunned
            ? _restLocalPosition + _stunnedLocalPositionOffset
            : _restLocalPosition;
        Quaternion targetRotation = isStunned
            ? _restLocalRotation * Quaternion.Euler(_stunnedLocalEulerOffset)
            : _restLocalRotation;
        _stunnedPoseTransition = StartCoroutine(TransitionStunnedPose(targetPosition, targetRotation));
    }

    private IEnumerator TransitionStunnedPose(Vector3 targetPosition, Quaternion targetRotation)
    {
        Vector3 startPosition = _telegraphVisual.localPosition;
        Quaternion startRotation = _telegraphVisual.localRotation;
        float elapsed = 0f;

        while (elapsed < _stunnedTransitionDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / _stunnedTransitionDuration);
            _telegraphVisual.localPosition = Vector3.Lerp(startPosition, targetPosition, progress);
            _telegraphVisual.localRotation = Quaternion.Slerp(startRotation, targetRotation, progress);
            yield return null;
        }

        _telegraphVisual.localPosition = targetPosition;
        _telegraphVisual.localRotation = targetRotation;
        _stunnedPoseTransition = null;
    }

    private void StopStunnedPoseTransition()
    {
        if (_stunnedPoseTransition == null) return;
        StopCoroutine(_stunnedPoseTransition);
        _stunnedPoseTransition = null;
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
