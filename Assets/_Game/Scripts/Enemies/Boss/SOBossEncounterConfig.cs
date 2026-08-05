using UnityEngine;

/// <summary>Immutable tuning data for the final boss encounter.</summary>
[CreateAssetMenu(fileName = "BossEncounterConfig", menuName = "DATN/Boss Encounter Config")]
public sealed class SOBossEncounterConfig : ScriptableObject
{
    [Min(0f)] public float IntroDuration = 2f;
    [Min(0f)] public float AutoRespawnDelay = 10f;
    [Min(0f)] public float ReviveHoldDuration = 5f;
    [Range(0f, 1f)] public float ReviveHealthPercent = 0.6f;
    [Min(0.1f)] public float ReviveDistance = 3f;
    [Min(0f)] public float WipeResetDelay = 2f;
}
