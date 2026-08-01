/// <summary>
/// JumpState — Player đang ở trên không sau khi nhảy.
/// Chuyển sang AirGlide/WallHang/Idle tùy điều kiện.
/// Physics xử lý bởi PlayerController.
/// </summary>
public class JumpState : PlayerStateBase
{
    public JumpState(PlayerStateMachine machine, PlayerInputHandler input)
        : base(machine, input) { }

    public override void Enter() { }

    public override void Update()
    {
        // Transition logic xử lý bởi PlayerController (CheckGrounded, HandleJump, HandleAirGlide, HandleWallClimb)
        // vì cần physics data (isGrounded, velocity, raycast) mà state không có
    }

    public override void Exit() { }
}
