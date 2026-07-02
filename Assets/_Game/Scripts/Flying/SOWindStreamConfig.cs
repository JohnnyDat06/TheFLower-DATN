using UnityEngine;

[CreateAssetMenu(fileName = "SOWindStreamConfig", menuName = "CoopGame/Level04/Wind Stream Config")]
public class SOWindStreamConfig : ScriptableObject
{
    [Min(0f)] public float ForwardAcceleration = 10f;
    [Min(0f)] public float LiftAcceleration = 5f;
    [Min(0f)] public float CenteringAcceleration = 3f;
    [Min(0f)] public float MaximumAcceleration = 18f;
}
