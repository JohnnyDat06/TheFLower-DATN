using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class GalaxyGate : NetworkBehaviour
{
    [SerializeField, Min(0.1f)] private float _activationWindow = 5f;
    [SerializeField, Min(0f)] private float _upwardBoost = 24f;
    [SerializeField, Min(0f)] private float _forwardBoost = 20f;
    [SerializeField] private Transform _warpDirection;

    private ulong _firstPlayerId;
    private float _firstPassTime;
    private bool _activated;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void Update()
    {
        if (!IsServer || _activated || _firstPassTime <= 0f) return;
        if (Time.time - _firstPassTime > _activationWindow)
        {
            _firstPlayerId = 0;
            _firstPassTime = 0f;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || _activated) return;

        var flight = other.GetComponentInParent<Level04FlightController>();
        if (flight == null || !flight.IsSpawned || !flight.FlightEnabled) return;

        if (Level04FlowManager.Instance != null
            && Level04FlowManager.Instance.CanUseHostSoloDebug(flight.OwnerClientId))
        {
            _firstPlayerId = flight.OwnerClientId;
            Level04FlowManager.Instance.SetPhaseServer(Level04Phase.GalaxyGate);
            ActivateGate();
            return;
        }

        if (_firstPassTime <= 0f)
        {
            _firstPlayerId = flight.OwnerClientId;
            _firstPassTime = Time.time;
            Level04FlowManager.Instance?.SetPhaseServer(Level04Phase.GalaxyGate);
            return;
        }

        if (flight.OwnerClientId == _firstPlayerId) return;
        if (Time.time - _firstPassTime > _activationWindow)
        {
            _firstPlayerId = flight.OwnerClientId;
            _firstPassTime = Time.time;
            return;
        }

        ActivateGate();
    }

    private void ActivateGate()
    {
        _activated = true;
        Level04FlowManager.Instance?.SetPhaseServer(Level04Phase.TimeWarpAscent);

        Vector3 direction = _warpDirection != null ? _warpDirection.forward : transform.forward;
        Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            client.PlayerObject.GetComponent<Level04FlightController>()
                ?.ApplyBoostServer(planarDirection, _forwardBoost, _upwardBoost);
        }
    }
}
