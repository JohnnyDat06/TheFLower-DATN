using UnityEngine;

[CreateAssetMenu(fileName = "SOFlyingConfig", menuName = "CoopGame/Level04/Flying Config")]
public class SOFlyingConfig : ScriptableObject
{
    [Header("Speed")]
    [Min(0f)] public float NormalFlySpeed = 16f;
    [Min(0f)] public float BoostSpeed = 28f;
    [Min(0f)] public float GlideSpeed = 12f;
    [Min(0f)] public float BrakeSpeed = 6f;
    [Min(0f)] public float Acceleration = 8f;
    [Min(0f)] public float BrakeDeceleration = 16f;
    [Min(0f)] public float VelocityResponsiveness = 4f;
    [Min(1f)] public float GalaxyGateSpeedMultiplier = 1.35f;

    [Header("Steering")]
    [Min(0f)] public float TurnSpeed = 75f;
    [Min(0f)] public float PitchSpeed = 45f;
    [Range(0f, 45f)] public float MaxPitch = 30f;
    [Range(0f, 60f)] public float MaxBankAngle = 35f;
    [Min(0f)] public float LateralMoveSpeed = 4.5f;
    [Min(1f)] public float IdleDecelerationMultiplier = 2f;
    [Range(0f, 1f)] public float CameraSteeringWeight = 0.9f;
    [Range(0f, 45f)] public float KeyboardTurnAngle = 24f;
    [Range(0f, 1f)] public float BrakeDirectionHold = 0.7f;
    [Min(0f)] public float DirectionResponsiveness = 3.2f;
    [Min(1f)] public float DiveSteeringMultiplier = 1.4f;
    [Range(0f, 1f)] public float ModelCameraTiltWeight = 0.85f;

    [Header("Altitude Control")]
    [Min(0f)] public float ClimbSpeed = 10f;
    [Min(0f)] public float DescendSpeed = 9f;

    [Header("Glide")]
    [Range(0f, 1f)] public float GlideGravityScale = 0.3f;
    [Min(0f)] public float MaxFallSpeed = 18f;
    [Min(0f)] public float DiveSpeedBonus = 5f;
    [Min(0f)] public float ClimbSpeedPenalty = 7f;
    [Min(0f)] public float GlideSinkSpeed = 0.55f;
    [Min(0f)] public float WindAssistStrength = 6f;

    [Header("Wing Beat")]
    [Min(0.1f)] public float FlapInterval = 0.75f;
    [Min(0f)] public float FlapLiftVelocity = 2.2f;
    [Min(0f)] public float FlapLiftDecay = 5.5f;
    [Min(0f)] public float MaxFlapLiftVelocity = 2.8f;

    [Header("Guided Flight Path")]
    [Range(0f, 1f)] public float PathAssistWeight = 0.85f;
    [Range(0f, 1f)] public float PlayerSteeringInfluence = 0.35f;
    [Min(1f)] public float WaypointReachDistance = 24f;
    [Range(0f, 45f)] public float MaximumSteeringOffset = 22f;
    [Min(0f)] public float RotationResponsiveness = 4f;

    [Header("Takeoff")]
    [Min(0f)] public float TakeoffDuration = 1.1f;
    [Min(0f)] public float TakeoffForwardSpeed = 18f;
    [Min(0f)] public float TakeoffLiftSpeed = 5f;

    [Header("Boost")]
    [Min(0f)] public float BoostDuration = 1.2f;
    [Min(0f)] public float BoostDecay = 5f;
    [Range(0f, 1f)] public float RingGuidanceWeight = 0.65f;

    [Header("Recovery")]
    [Min(0f)] public float RecoveryHeightOffset = 25f;
    [Min(10f)] public float MaximumPathDeviation = 180f;
    [Min(0.1f)] public float RecoveryRequestCooldown = 2f;
}
