using UnityEngine;

[RequireComponent(typeof(Collider))]
public class WindStream : MonoBehaviour
{
    [SerializeField] private SOWindStreamConfig _config;
    [SerializeField] private Transform _direction;
    [SerializeField] private Transform _centerLine;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerStay(Collider other)
    {
        var flight = other.GetComponentInParent<Level04FlightController>();
        if (flight == null || !flight.IsOwner || !flight.FlightEnabled) return;

        Vector3 forward = _direction != null ? _direction.forward : transform.forward;
        float forwardForce = _config != null ? _config.ForwardAcceleration : 10f;
        float liftForce = _config != null ? _config.LiftAcceleration : 5f;
        float centeringForce = _config != null ? _config.CenteringAcceleration : 3f;
        float maxForce = _config != null ? _config.MaximumAcceleration : 18f;

        Vector3 center = _centerLine != null ? _centerLine.position : transform.position;
        Vector3 toCenter = Vector3.ProjectOnPlane(center - flight.transform.position, forward);
        Vector3 acceleration = forward * forwardForce
            + Vector3.up * liftForce
            + toCenter.normalized * centeringForce;

        flight.ApplyWind(Vector3.ClampMagnitude(acceleration, maxForce));
    }
}
