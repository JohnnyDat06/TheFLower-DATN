using System;
using UnityEngine;

/// <summary>
/// Receives trigger contacts from <see cref="SandBoatObstacle"/> and applies one
/// temporary speed penalty per cooldown without blocking spline movement.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public sealed class SandBoatCollisionHandler : MonoBehaviour
{
    [SerializeField] private SandBoatMovement _movement;
    [SerializeField] private SandBoatSpeedController _speedController;
    [SerializeField, Min(0f)] private float _collisionSpeedPenalty = 6f;
    [SerializeField, Min(0.01f)] private float _collisionRecoveryTime = 1.5f;
    [SerializeField, Min(0f)] private float _collisionCooldown = 1f;

    private float _nextCollisionTime;

    /// <summary>Raised once after a valid obstacle hit has applied its speed penalty.</summary>
    public event Action<SandBoatObstacle> ObstacleHit;

    private void Awake()
    {
        Rigidbody rigidbody = GetComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        // SandBoatMovement writes an authored spline pose every rendered frame.
        // Rigidbody interpolation uses fixed-timestep snapshots and causes visible
        // vertical jitter on slopes when combined with that transform-driven motion.
        rigidbody.interpolation = RigidbodyInterpolation.None;
    }

    private void OnTriggerEnter(Collider other)
    {
        SandBoatObstacle obstacle = other.GetComponentInParent<SandBoatObstacle>();
        if (obstacle == null)
        {
            return;
        }

        TryHandleObstacleHit(obstacle);
    }

    private void TryHandleObstacleHit(SandBoatObstacle obstacle)
    {
        if (!Application.isPlaying
            || _movement == null
            || !_movement.IsRouteMovementEnabled
            || Time.time < _nextCollisionTime)
        {
            return;
        }

        _nextCollisionTime = Time.time + _collisionCooldown;
        _speedController?.ApplyCollisionSpeedPenalty(
            _collisionSpeedPenalty,
            _collisionRecoveryTime);
        ObstacleHit?.Invoke(obstacle);
    }

    /// <summary>Clears the obstacle-hit cooldown so the next retry begins cleanly.</summary>
    public void ResetCollisionState()
    {
        _nextCollisionTime = 0f;
    }
}
