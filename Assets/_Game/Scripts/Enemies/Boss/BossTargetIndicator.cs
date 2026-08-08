using UnityEngine;

/// <summary>
/// Editor-visible target feedback for the Cat Sphinx Phase 2 selection prototype.
/// </summary>
public sealed class BossTargetIndicator : MonoBehaviour
{
    [SerializeField] private Transform _currentTarget;

    public Transform CurrentTarget => _currentTarget;

    /// <summary>Updates the player shown by the target debug indicator.</summary>
    public void SetTarget(Transform target)
    {
        _currentTarget = target;
    }

    private void OnDrawGizmos()
    {
        if (_currentTarget == null) return;

        Gizmos.color = Color.red;
        Vector3 center = _currentTarget.position + Vector3.up * 0.1f;
        Gizmos.DrawWireSphere(center, 0.8f);
        Gizmos.DrawLine(transform.position, center);
    }
}
