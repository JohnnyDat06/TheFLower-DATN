using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Provides world-space samples for the Sand Boat Chase route.
/// This component is deliberately read-only for gameplay systems: boat movement,
/// steering, speed, collision, and networking are implemented in later phases.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class SandBoatRoute : MonoBehaviour
{
    [SerializeField] private SplineContainer _splineContainer;
    [SerializeField] private Transform _endpointReference;

    [Header("Phase 1 Debug")]
    [SerializeField] private Transform _debugPoint;
    [SerializeField] private bool _animateDebugPoint;
    [SerializeField, Min(0.01f)] private float _debugProgressPerSecond = 0.1f;

    private float _debugProgress;

    /// <summary>True when the route has a spline with at least two knots.</summary>
    public bool IsValid => _splineContainer != null && _splineContainer.Spline != null && _splineContainer.Spline.Count >= 2;

    /// <summary>Normalized debug progress in the inclusive range 0..1.</summary>
    public float DebugProgress => _debugProgress;

    /// <summary>World-space endpoint of the route, or this transform position when no route is assigned.</summary>
    public Vector3 Endpoint => Evaluate(1f).Position;

    private void Reset()
    {
        _splineContainer = GetComponent<SplineContainer>();
    }

    private void OnValidate()
    {
        _splineContainer ??= GetComponent<SplineContainer>();
        _debugProgress = Mathf.Clamp01(_debugProgress);
        SyncEndpointWithReference();
        UpdateDebugPoint();
    }

    private void Update()
    {
        SyncEndpointWithReference();

        if (!_animateDebugPoint || _debugPoint == null || !Application.isPlaying || !IsValid)
        {
            return;
        }

        _debugProgress = Mathf.Clamp01(_debugProgress + _debugProgressPerSecond * Time.deltaTime);
        UpdateDebugPoint();
    }

    /// <summary>Sets the debug marker's normalized route progress without affecting gameplay.</summary>
    public void SetDebugProgress(float progress)
    {
        _debugProgress = ClampProgress(progress);
        UpdateDebugPoint();
    }

    /// <summary>Clamps a normalized route progress value to the valid inclusive range.</summary>
    public float ClampProgress(float progress)
    {
        return Mathf.Clamp01(progress);
    }

    /// <summary>Returns whether a normalized route progress has reached the endpoint.</summary>
    public bool IsComplete(float progress)
    {
        return ClampProgress(progress) >= 1f;
    }

    /// <summary>Samples the route in world space at a normalized progress value.</summary>
    public SandBoatRouteSample Evaluate(float progress)
    {
        float clampedProgress = ClampProgress(progress);
        if (!IsValid)
        {
            return new SandBoatRouteSample(transform.position, transform.forward, transform.right, clampedProgress);
        }

        Vector3 localPosition = SplineUtility.EvaluatePosition(_splineContainer.Spline, clampedProgress);
        Vector3 localForward = SplineUtility.EvaluateTangent(_splineContainer.Spline, clampedProgress);
        Vector3 localUp = SplineUtility.EvaluateUpVector(_splineContainer.Spline, clampedProgress);

        Vector3 forward = _splineContainer.transform.TransformDirection(localForward).normalized;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = _splineContainer.transform.forward;
        }

        Vector3 up = _splineContainer.transform.TransformDirection(localUp).normalized;
        if (up.sqrMagnitude < 0.0001f)
        {
            up = Vector3.up;
        }

        Vector3 right = Vector3.Cross(up, forward).normalized;
        return new SandBoatRouteSample(
            _splineContainer.transform.TransformPoint(localPosition),
            forward,
            right,
            clampedProgress);
    }

    private void UpdateDebugPoint()
    {
        if (_debugPoint == null)
        {
            return;
        }

        SandBoatRouteSample sample = Evaluate(_debugProgress);
        _debugPoint.SetPositionAndRotation(sample.Position, Quaternion.LookRotation(sample.Forward, Vector3.up));
    }

    private void SyncEndpointWithReference()
    {
        if (_endpointReference == null || !IsValid)
        {
            return;
        }

        int endpointIndex = _splineContainer.Spline.Count - 1;
        BezierKnot endpoint = _splineContainer.Spline[endpointIndex];
        Vector3 localEndpoint = _splineContainer.transform.InverseTransformPoint(_endpointReference.position);
        if (Vector3.SqrMagnitude((Vector3)endpoint.Position - localEndpoint) < 0.000001f)
        {
            return;
        }

        endpoint.Position = localEndpoint;
        _splineContainer.Spline[endpointIndex] = endpoint;

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(_splineContainer);
#endif
    }

    private void OnDrawGizmosSelected()
    {
        if (!IsValid)
        {
            return;
        }

        SandBoatRouteSample sample = Evaluate(_debugProgress);
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(sample.Position, 0.8f);
        Gizmos.DrawRay(sample.Position, sample.Forward * 4f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawRay(sample.Position, sample.Right * 3f);
    }
}

/// <summary>Immutable world-space route data sampled by <see cref="SandBoatRoute"/>.</summary>
public readonly struct SandBoatRouteSample
{
    public SandBoatRouteSample(Vector3 position, Vector3 forward, Vector3 right, float progress)
    {
        Position = position;
        Forward = forward;
        Right = right;
        Progress = progress;
    }

    public Vector3 Position { get; }
    public Vector3 Forward { get; }
    public Vector3 Right { get; }
    public float Progress { get; }
    public bool IsComplete => Progress >= 1f;
}
