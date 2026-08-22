using UnityEngine;

/// <summary>
/// Produces a clamped, smoothed lateral displacement around the Sand Boat route center.
/// Player steering input is intentionally deferred to Phase 4.
/// </summary>
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public sealed class SandBoatHorizontalOffset : MonoBehaviour
{
    [SerializeField] private SandBoatMovement _movement;
    [SerializeField, Min(0f)] private float _maxHorizontalOffset = 12f;
    [SerializeField, Min(0.01f)] private float _horizontalSmoothing = 8f;

    [Header("Phase 3 Debug")]
    [SerializeField] private float _debugTargetOffset;

    private float _currentOffset;

    /// <summary>Current smoothed lateral offset applied to the boat.</summary>
    public float CurrentOffset => _currentOffset;

    /// <summary>Maximum permitted offset from the route center.</summary>
    public float MaxHorizontalOffset => _maxHorizontalOffset;

    private void OnValidate()
    {
        _maxHorizontalOffset = Mathf.Max(0f, _maxHorizontalOffset);
        _horizontalSmoothing = Mathf.Max(0.01f, _horizontalSmoothing);
        _debugTargetOffset = ClampOffset(_debugTargetOffset);
        _currentOffset = ClampOffset(_currentOffset);
    }

    private void Awake()
    {
        _currentOffset = ClampOffset(_debugTargetOffset);
        ApplyOffset();
    }

    private void Update()
    {
        if (!Application.isPlaying || _movement == null)
        {
            return;
        }

        float targetOffset = ClampOffset(_debugTargetOffset);
        _currentOffset = Mathf.MoveTowards(
            _currentOffset,
            targetOffset,
            _horizontalSmoothing * Time.deltaTime);
        ApplyOffset();
    }

    /// <summary>Sets the clamped target offset used by the current steering source.</summary>
    public void SetTargetOffset(float offset)
    {
        _debugTargetOffset = ClampOffset(offset);
    }

    /// <summary>Sets a clamped debug offset for Phase 3 verification without player input.</summary>
    public void SetDebugTargetOffset(float offset)
    {
        SetTargetOffset(offset);
    }

    private float ClampOffset(float offset)
    {
        return Mathf.Clamp(offset, -_maxHorizontalOffset, _maxHorizontalOffset);
    }

    private void ApplyOffset()
    {
        _movement?.SetHorizontalOffset(_currentOffset);
    }
}
