using UnityEngine;

/// <summary>Alternates a target direction left and right to produce readable Phase 2 diagonal Shockwaves.</summary>
public sealed class DiagonalShockwavePattern : MonoBehaviour
{
    [Tooltip("Goc lech cua Shockwave cheo so voi huong Target goc.")]
    [SerializeField, Range(15f, 45f)] private float _diagonalAngle = 30f;

    private bool _nextUsesLeft;

    /// <summary>Returns the next alternating diagonal direction from a valid ground-plane base direction.</summary>
    public Vector3 GetNextDirection(Vector3 baseDirection)
    {
        Vector3 flattenedDirection = Vector3.ProjectOnPlane(baseDirection, Vector3.up).normalized;
        if (flattenedDirection.sqrMagnitude < 0.0001f) return Vector3.zero;

        float signedAngle = _nextUsesLeft ? -_diagonalAngle : _diagonalAngle;
        _nextUsesLeft = !_nextUsesLeft;
        return Quaternion.AngleAxis(signedAngle, Vector3.up) * flattenedDirection;
    }
}
