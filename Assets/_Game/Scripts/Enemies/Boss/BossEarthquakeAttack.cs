using System.Collections;
using UnityEngine;

/// <summary>Runs the Phase 3 Earthquake telegraph and impact without changing FloorTile state.</summary>
public sealed class BossEarthquakeAttack : MonoBehaviour
{
    [Tooltip("Thoi gian red telegraph cho Earthquake truoc impact.")]
    [SerializeField, Range(0.8f, 2f)] private float _telegraphDuration = 1.3f;
    private BossAnimationController _animationController;
    private FloorPatternController _floorPatternController;
    private Coroutine _routine;

    /// <summary>True while Earthquake owns the combat timeline.</summary>
    public bool IsRunning => _routine != null;

    /// <summary>Starts one Earthquake when no previous instance is running.</summary>
    public bool TryStart()
    {
        if (_routine != null) return false;
        _routine = StartCoroutine(RunAttack());
        return true;
    }

    private void Awake()
    {
        _animationController = GetComponent<BossAnimationController>();
        _floorPatternController = GetComponent<FloorPatternController>();
    }

    private IEnumerator RunAttack()
    {
        _floorPatternController?.ShowEarthquakeTelegraph(_telegraphDuration);
        _animationController?.PlayPawSlam();

        float elapsed = 0f;
        while (elapsed < _telegraphDuration)
        {
            elapsed += Time.deltaTime;
            if (_animationController != null && !_animationController.UsesAuthoredPawSlam)
                _animationController.SetTelegraphProgress(elapsed / _telegraphDuration);
            yield return null;
        }

        _animationController?.ResetPose();
        Debug.Log("[BossEarthquakeAttack] Earthquake impact completed without affecting FloorTiles.", this);
        _routine = null;
    }
}
