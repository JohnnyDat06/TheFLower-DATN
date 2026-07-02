using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Level04PhaseTrigger : NetworkBehaviour
{
    [SerializeField] private Level04Phase _phase;
    [SerializeField] private bool _oneShot = true;

    private bool _triggered;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || (_oneShot && _triggered)) return;
        if (other.GetComponentInParent<Level04FlightController>() == null) return;

        _triggered = true;
        Level04FlowManager.Instance?.SetPhaseServer(_phase);
    }
}
