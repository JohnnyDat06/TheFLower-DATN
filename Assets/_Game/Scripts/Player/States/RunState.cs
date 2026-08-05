/// <summary>
/// RunState — Player đang chạy (Sprint).
/// Chuyển sang Idle/Walk/GroundSlide tùy input.
/// </summary>
public class RunState : PlayerStateBase
{
    public RunState(PlayerStateMachine machine, PlayerInputHandler input)
        : base(machine, input) { }

    public override void Enter() { }

    public override void Update()
    {
        // PlayerController validates ground contact before applying jump physics.
        if (Input.JumpPressed) return;

        // Crouch while running → GroundSlide
        if (Input.IsCrouching)
        {
            Machine.TransitionTo(PlayerStateType.GroundSlide);
            return;
        }

        // Stop sprinting
        if (!Input.IsSprinting)
        {
            Machine.TransitionTo(PlayerStateType.Walk);
            return;
        }

        // Stop moving
        if (!Input.IsMoving)
        {
            Machine.TransitionTo(PlayerStateType.Idle);
            return;
        }

        // Attack
        if (Input.AttackPressed && Machine.GetComponent<PlayerController>().IsGrounded)
        {
            Machine.TransitionTo(PlayerStateType.Attack1);
            return;
        }
    }

    public override void Exit() { }
}
