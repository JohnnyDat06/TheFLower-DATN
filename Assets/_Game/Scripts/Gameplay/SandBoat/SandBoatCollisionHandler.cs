using System;
using UnityEngine;

/// <summary>
/// Receives trigger contacts from <see cref="SandBoatObstacle"/>. Authored rocks
/// fail the chase, while runtime storm rocks apply a severe temporary slowdown.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public sealed class SandBoatCollisionHandler : MonoBehaviour
{
    [SerializeField, Tooltip("Nguồn chuyển động dùng để chỉ nhận va chạm khi Sand Boat đang chạy trên Route.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Controller tốc độ nhận hiệu ứng giảm tốc khi thuyền chạm đá do bão phóng.")]
    private SandBoatSpeedController _speedController;
    [SerializeField, Tooltip("Controller thất bại dùng để chơi lại khi thuyền chạm đá chặn được đặt sẵn trên đường.")]
    private SandBoatChaseFailController _failController;
    [SerializeField, Min(0.01f), Tooltip("Số giây thuyền phục hồi tốc độ sau khi chạm đá do bão phóng.")]
    private float _stormRockRecoveryTime = 3f;
    [SerializeField, Min(0f), Tooltip("Thời gian chống nhận lặp nhiều va chạm đá bão liên tiếp.")]
    private float _collisionCooldown = 1f;

    private float _nextCollisionTime;

    /// <summary>Raised once after a valid obstacle hit has been processed.</summary>
    public event Action<SandBoatObstacle> ObstacleHit;

    private void OnValidate()
    {
        _stormRockRecoveryTime = Mathf.Max(0.01f, _stormRockRecoveryTime);
        _collisionCooldown = Mathf.Max(0f, _collisionCooldown);
    }

    private void Awake()
    {
        _failController ??= GetComponent<SandBoatChaseFailController>();
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
            || !_movement.IsRouteMovementEnabled)
        {
            return;
        }

        if (!obstacle.IsStormRock)
        {
            _failController?.TriggerObstacleFail();
            ObstacleHit?.Invoke(obstacle);
            return;
        }

        if (Time.time < _nextCollisionTime)
        {
            return;
        }

        _nextCollisionTime = Time.time + _collisionCooldown;
        _speedController?.ApplyCollisionSpeedPenalty(
            _speedController.MaxForwardSpeed,
            _stormRockRecoveryTime);
        ObstacleHit?.Invoke(obstacle);
    }

    /// <summary>Clears the obstacle-hit cooldown so the next retry begins cleanly.</summary>
    public void ResetCollisionState()
    {
        _nextCollisionTime = 0f;
    }
}
