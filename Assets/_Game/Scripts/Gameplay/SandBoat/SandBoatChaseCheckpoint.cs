using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Restores the Sand Boat chase to its authored start after a Phase 12 failure.
/// Player seating is delegated to <see cref="SandBoatBoarding"/> so the existing
/// NGO teleport path remains the only player-position authority.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandBoatChaseCheckpoint : MonoBehaviour
{
    [Header("Sand Boat References")]
    [SerializeField, Tooltip("Khôi phục chuyển động theo Route về tiến độ bắt đầu đã cấu hình khi thử lại.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Đặt component lệch ngang về tâm Route.")]
    private SandBoatHorizontalOffset _horizontalOffset;
    [SerializeField, Tooltip("Xóa trạng thái đánh lái P1 trước khi bật lại điều khiển chase.")]
    private SandBoatSteering _steering;
    [SerializeField, Tooltip("Đặt lại tốc độ P2 và trạng thái hồi phục va chạm về tốc độ cơ bản đã cấu hình.")]
    private SandBoatSpeedController _speedController;
    [SerializeField, Tooltip("Đặt lại thời gian chờ chướng ngại để lần thử lại không bị chặn bởi lần trước.")]
    private SandBoatCollisionHandler _collisionHandler;
    [SerializeField, Tooltip("Đặt lại khoảng cách bão về giá trị an toàn ban đầu đã cấu hình.")]
    private SandstormChaseController _stormLogic;
    [SerializeField, Tooltip("Cổng thất bại Phase 12 yêu cầu bắt đầu reset checkpoint này.")]
    private SandBoatChaseFailController _failController;
    [SerializeField, Tooltip("Controller đá bão Phase 14; xóa cảnh báo và đá đang chờ khi thử lại.")]
    private SandstormRockAttackController _stormRockAttack;
    [SerializeField, Tooltip("Component boarding hiện có; đặt người chơi lại vào ghế và tiếp tục chase qua NGO.")]
    private SandBoatBoarding _boarding;

    [Header("Runtime Debug")]
    [SerializeField, Tooltip("Chỉ đúng trong lúc một giao dịch reset checkpoint đang chạy.")]
    private bool _isResetting;

    /// <summary>True while the current retry transaction is restoring the chase state.</summary>
    public bool IsResetting => _isResetting;

    private void Update()
    {
        if (_isResetting || _failController == null || !_failController.IsReadyForReset)
        {
            return;
        }

        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        StartCoroutine(ResetChaseRoutine());
    }

    private IEnumerator ResetChaseRoutine()
    {
        _isResetting = true;
        _movement?.SetRouteMovementEnabled(false);
        _movement?.ResetMovement();
        _horizontalOffset?.ResetOffset();
        _steering?.ResetSteeringState();
        _speedController?.ResetSpeed();
        _collisionHandler?.ResetCollisionState();
        _stormLogic?.ResetStormDistance();
        _stormRockAttack?.ResetAttackState();
        _failController?.ResetFailState();

        // Allow the movement pose reset to update the authored seat transforms first.
        yield return null;
        _boarding?.ResumeChaseAfterReset();
        _isResetting = false;
    }
}
