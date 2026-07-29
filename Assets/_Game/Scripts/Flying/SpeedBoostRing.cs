using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class SpeedBoostRing : NetworkBehaviour
{
    [SerializeField] private SOSpeedBoostRingConfig _config;
    [SerializeField] private Transform _boostDirection;

    private readonly Dictionary<ulong, float> _lastActivationTime = new();

    private void Awake()
    {
        var trigger = GetComponent<Collider>();
        trigger.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || !TryGetPlayer(other, out var flight)) return;

        ulong clientId = flight.OwnerClientId;
        float cooldown = _config != null ? _config.Cooldown : 1.5f;
        if (_lastActivationTime.TryGetValue(clientId, out float lastTime)
            && Time.time - lastTime < cooldown)
        {
            return;
        }

        if (_config != null && _config.OneShotPerPlayer && _lastActivationTime.ContainsKey(clientId))
        {
            return;
        }

        _lastActivationTime[clientId] = Time.time;
        Vector3 direction = _boostDirection != null ? _boostDirection.forward : transform.forward;
        flight.ApplyBoostServer(
            direction,
            _config != null ? _config.BoostForce : 10f,
            _config != null ? _config.LiftForce : 5f);

        PlayBoostSfxClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        });

        EventBus.RaiseLevel04RingActivated(name, clientId, false);
    }

    [ClientRpc]
    private void PlayBoostSfxClientRpc(ClientRpcParams rpcParams = default)
    {
        if (_config == null || _config.SFXClip == null) return;
        AudioManager.Instance.PlaySFX(_config.SFXClip);
    }

    private static bool TryGetPlayer(Collider other, out Level04FlightController flight)
    {
        flight = other.GetComponentInParent<Level04FlightController>();
        return flight != null && flight.IsSpawned && flight.FlightEnabled;
    }
}
