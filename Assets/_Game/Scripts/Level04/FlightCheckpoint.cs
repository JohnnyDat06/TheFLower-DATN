using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class FlightCheckpoint : NetworkBehaviour
{
    [SerializeField] private string _checkpointId = "Level04_CP";
    [SerializeField] private Transform _hostSpawnPoint;
    [SerializeField] private Transform _clientSpawnPoint;

    private bool _activated;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var flight = other.GetComponentInParent<Level04FlightController>();
        if (flight == null || !flight.IsSpawned) return;

        bool isHost = flight.OwnerClientId == NetworkManager.ServerClientId;
        Transform spawn = isHost ? _hostSpawnPoint : _clientSpawnPoint;
        Vector3 position = spawn != null ? spawn.position : transform.position;
        Quaternion rotation = spawn != null ? spawn.rotation : transform.rotation;
        flight.SetCheckpointServer(position, rotation);

        if (_activated) return;
        _activated = true;

        Vector3 hostPosition = _hostSpawnPoint != null
            ? _hostSpawnPoint.position
            : transform.position + transform.right * 3f;
        Vector3 clientPosition = _clientSpawnPoint != null
            ? _clientSpawnPoint.position
            : transform.position - transform.right * 3f;

        EventBus.RaiseCheckpointReached(_checkpointId, hostPosition, clientPosition);
    }
}
