using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class MemoryShard : NetworkBehaviour
{
    [SerializeField] private string _shardId;
    [SerializeField] private Renderer[] _visuals;

    private readonly NetworkVariable<bool> _collected = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Collider _trigger;

    private void Awake()
    {
        _trigger = GetComponent<Collider>();
        _trigger.isTrigger = true;
        if (_visuals == null || _visuals.Length == 0)
        {
            _visuals = GetComponentsInChildren<Renderer>(true);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _collected.OnValueChanged += HandleCollectedChanged;
        ApplyCollected(_collected.Value);
    }

    public override void OnNetworkDespawn()
    {
        _collected.OnValueChanged -= HandleCollectedChanged;
        base.OnNetworkDespawn();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || _collected.Value) return;
        var flight = other.GetComponentInParent<Level04FlightController>();
        if (flight == null || !flight.IsSpawned) return;

        _collected.Value = true;
        EventBus.RaiseLevel04MemoryShardCollected(_shardId, flight.OwnerClientId);
    }

    private void HandleCollectedChanged(bool previous, bool current)
    {
        ApplyCollected(current);
    }

    private void ApplyCollected(bool collected)
    {
        if (_trigger != null) _trigger.enabled = !collected;
        foreach (var visual in _visuals)
        {
            if (visual != null) visual.enabled = !collected;
        }
    }
}
