/// <summary>
/// WalkState — Player đang đi bộ.
/// Chuyển sang Idle/Run/CrouchWalk tùy input.
/// </summary>
public class WalkState : PlayerStateBase
{
    public WalkState(PlayerStateMachine machine, PlayerInputHandler input)
        : base(machine, input) { }

    public override void Enter() { }

    public override void Update()
    {
        // PlayerController validates ground contact before applying jump physics.
        if (Input.JumpPressed) return;

        // Crouch while walking
        if (Input.IsCrouching)
        {
            Machine.TransitionTo(PlayerStateType.CrouchWalk);
            return;
        }

        // Sprint
        if (Input.IsSprinting)
        {
            Machine.TransitionTo(PlayerStateType.Run);
            return;
        }

        // Stop
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
