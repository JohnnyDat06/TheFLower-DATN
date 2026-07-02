using UnityEngine;

[CreateAssetMenu(fileName = "SOResonanceRingConfig", menuName = "CoopGame/Level04/Resonance Ring Config")]
public class SOResonanceRingConfig : ScriptableObject
{
    [Min(0.1f)] public float ActivationWindow = 2.5f;
    [Min(0f)] public float TeamBoostForce = 14f;
    [Min(0f)] public float TeamLiftForce = 7f;
    public bool OneShot = true;
}
