using System;
using UnityEngine;

/// <summary>Inspector-tunable values for the current Cat Sphinx Phase 1 encounter.</summary>
[Serializable]
public sealed class BossPhaseData
{
    [Tooltip("Tong so Core-health cua boss trong toan bo encounter.")]
    [SerializeField, Min(1)] private int _maxCoreHealth = 3;
    [Tooltip("So Core Hit can de ket thuc Phase 1 va vao Phase 2 placeholder.")]
    [SerializeField, Min(1)] private int _phaseOneCoreHitsToComplete = 1;
    [Tooltip("Khoang nghi giua hai chu ky target va Paw Slam khi boss dang Idle.")]
    [SerializeField, Min(0.1f)] private float _attackCycleInterval = 1.5f;

    /// <summary>Total Core-health available across the three planned boss phases.</summary>
    public int MaxCoreHealth => _maxCoreHealth;

    /// <summary>Core hits required before Phase 1 hands off to its Phase 2 placeholder.</summary>
    public int PhaseOneCoreHitsToComplete => _phaseOneCoreHitsToComplete;

    /// <summary>Delay before BossController starts its next automatic Phase 1 attack cycle.</summary>
    public float AttackCycleInterval => _attackCycleInterval;
}
