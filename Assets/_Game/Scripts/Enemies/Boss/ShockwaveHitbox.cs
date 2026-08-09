using UnityEngine;

/// <summary>
/// Owns the Phase 4 trigger shape for a Shockwave.
/// It intentionally applies no player effects until the dedicated damage phase.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public sealed class ShockwaveHitbox : MonoBehaviour
{
    [SerializeField] private BoxCollider _trigger;

    /// <summary>Configures a ground-level trigger matching the visible Shockwave band.</summary>
    public void Configure(float width, float depth)
    {
        if (_trigger == null) _trigger = GetComponent<BoxCollider>();
        _trigger.isTrigger = true;
        _trigger.center = new Vector3(0f, 0.1f, 0f);
        _trigger.size = new Vector3(width, 0.2f, depth);
    }
}
