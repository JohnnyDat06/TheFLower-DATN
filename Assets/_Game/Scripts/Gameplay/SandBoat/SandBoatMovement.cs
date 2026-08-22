using UnityEngine;

/// <summary>
/// Moves the Sand Boat along its authored route at a fixed speed while keeping its heading stable.
/// Steering, speed control, boarding, collision, and networking are added in later phases.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandBoatMovement : MonoBehaviour
{
    private const int RouteLengthSampleCount = 256;

    [SerializeField] private SandBoatRoute _route;
    [SerializeField, Min(0.01f)] private float _baseForwardSpeed = 20f;
    [SerializeField, Range(0f, 1f)] private float _startProgress;

    private float _progress;
    private float _routeLength;
    private float _horizontalOffset;
    private bool _isComplete;

    /// <summary>Current normalized progress along the route.</summary>
    public float Progress => _progress;

    /// <summary>Current lateral displacement from the center of the route, in world units.</summary>
    public float HorizontalOffset => _horizontalOffset;

    /// <summary>True after the boat reaches the route endpoint.</summary>
    public bool IsComplete => _isComplete;

    private void OnValidate()
    {
        _startProgress = Mathf.Clamp01(_startProgress);
        _baseForwardSpeed = Mathf.Max(0.01f, _baseForwardSpeed);
    }

    private void Awake()
    {
        ResetMovement();
    }

    private void Update()
    {
        if (!Application.isPlaying || _isComplete || _route == null || !_route.IsValid || _routeLength <= 0f)
        {
            return;
        }

        float progressDelta = _baseForwardSpeed / _routeLength * Time.deltaTime;
        _progress = _route.ClampProgress(_progress + progressDelta);
        ApplyRoutePose();
        _isComplete = _route.IsComplete(_progress);
    }

    /// <summary>Places the boat at Start Progress and resumes automatic route movement.</summary>
    public void ResetMovement()
    {
        _progress = _route != null ? _route.ClampProgress(_startProgress) : 0f;
        _routeLength = CalculateRouteLength();
        _horizontalOffset = 0f;
        _isComplete = _route == null || !_route.IsValid || _route.IsComplete(_progress);
        ApplyRoutePose();
    }

    /// <summary>Applies a lateral offset supplied by the Phase 3 offset controller.</summary>
    public void SetHorizontalOffset(float horizontalOffset)
    {
        _horizontalOffset = horizontalOffset;
        if (Application.isPlaying)
        {
            ApplyRoutePose();
        }
    }

    private float CalculateRouteLength()
    {
        if (_route == null || !_route.IsValid)
        {
            return 0f;
        }

        float length = 0f;
        Vector3 previousPosition = _route.Evaluate(0f).Position;
        for (int sampleIndex = 1; sampleIndex <= RouteLengthSampleCount; sampleIndex++)
        {
            Vector3 currentPosition = _route.Evaluate(sampleIndex / (float)RouteLengthSampleCount).Position;
            length += Vector3.Distance(previousPosition, currentPosition);
            previousPosition = currentPosition;
        }

        return length;
    }

    private void ApplyRoutePose()
    {
        if (_route == null || !_route.IsValid)
        {
            return;
        }

        SandBoatRouteSample sample = _route.Evaluate(_progress);
        Vector3 position = sample.Position + sample.Right * _horizontalOffset;
        transform.position = position;
    }
}
