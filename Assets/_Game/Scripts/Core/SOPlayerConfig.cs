using UnityEngine;

/// <summary>
/// SOPlayerConfig — Config data cho Player character.
/// SRS §4.1.3 · §13.3
/// </summary>
[CreateAssetMenu(fileName = "SOPlayerConfig", menuName = "CoopGame/Player/PlayerConfig")]
public class SOPlayerConfig : ScriptableObject
{
    [Header("Health")]
    [Tooltip("Máu tối đa của player")]
    public float MaxHealth = 100f;

    [Header("Environment Detection")]
    [Tooltip("Layer mặt đất (có thể đứng lên, chạy nhảy)")]
    public LayerMask GroundLayerMask;
    [Tooltip("Layer tường (có thể bám, nhảy tường)")]
    public LayerMask WallLayerMask;

    [Header("Movement")]
    [Tooltip("Tốc độ đi bộ cơ bản (m/s)")]
    public float MoveSpeed = 5f;

    [Tooltip("Nhân tốc độ khi Sprint")]
    public float SprintMultiplier = 1.6f;

    [Tooltip("Tốc độ di chuyển khi Crouch (nhân với MoveSpeed)")]
    public float CrouchSpeedMultiplier = 0.5f;

    [Header("Jump")]
    [Tooltip("Lực nhảy (áp lên Rigidbody AddForce)")]
    public float JumpForce = 7f;

    [Header("Glide")]
    [Tooltip("gravityScale khi đang Glide (< 1 = rơi chậm)")]
    public float GlideGravityScale = 0.2f;

    [Header("Wall Jump")]
    [Tooltip("Lực ngang khi WallJump")]
    public float WallJumpHorizontalForce = 5f;

    [Tooltip("Lực dọc khi WallJump")]
    public float WallJumpVerticalForce = 8f;

    [Header("Slide")]
    [Tooltip("Thời gian tối đa của GroundSlide (giây)")]
    public float GroundSlideDuration = 0.8f;

    [Tooltip("Lực quán tính ban đầu của Slide")]
    public float GroundSlideForce = 12f;

    [Header("Air Dash")]
    [Tooltip("Khoảng cách dash trên không (m)")]
    public float DashDistance = 6f;

    [Tooltip("Thời gian thực hiện air dash (giây)")]
    public float DashDuration = 0.2f;

    [Tooltip("Cooldown giữa hai lần dash (giây) — dùng chung cho cả air và roll")]
    public float DashCooldown = 1.0f;

    [Tooltip("Thời gian buffer input Dash/Roll (giây)")]
    public float DashInputBuffer = 0.15f;

    [Header("Ground Roll")]
    [Tooltip("Khoảng cách roll (m)")]
    public float RollDistance = 3f;

    [Tooltip("Thời gian roll (giây) — nên khớp với độ dài animation Player_Roll.anim")]
    public float RollDuration = 1.467f;

    [Tooltip("Cooldown riêng cho roll (giây)")]
    public float RollCooldown = 1.5f;
}
