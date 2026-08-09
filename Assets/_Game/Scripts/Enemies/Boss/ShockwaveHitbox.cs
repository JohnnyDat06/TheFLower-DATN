using System;
using UnityEngine;

/// <summary>
/// Owns the trigger shape for a Shockwave and reports unique collider entries.
/// </summary>
[RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
public sealed class ShockwaveHitbox : MonoBehaviour
{
    [Tooltip("Trigger collider dùng làm vùng va chạm của Shockwave.")]
    [SerializeField] private BoxCollider _trigger;
    [Tooltip("RigidBody kinematic giúp trigger nhận va chạm ổn định khi Shockwave di chuyển.")]
    [SerializeField] private Rigidbody _rigidbody;

    /// <summary>Raised when a collider first enters the moving Shockwave trigger.</summary>
    public event Action<Collider> TriggerEntered;

    /// <summary>Configures a ground-level trigger matching the visible Shockwave band.</summary>
    public void Configure(float width, float depth)
    {
        if (_trigger == null) _trigger = GetComponent<BoxCollider>();
        if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();

        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
        _trigger.isTrigger = true;
        _trigger.center = new Vector3(0f, 0.1f, 0f);
        _trigger.size = new Vector3(width, 0.2f, depth);
    }

    private void OnTriggerEnter(Collider other)
    {
        TriggerEntered?.Invoke(other);
    }
}
