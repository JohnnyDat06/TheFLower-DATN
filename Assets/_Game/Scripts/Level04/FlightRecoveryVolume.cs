using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class FlightRecoveryVolume : NetworkBehaviour
{
    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        other.GetComponentInParent<Level04FlightController>()?.RecoverToCheckpointServer();
    }
}
