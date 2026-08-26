using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Local trigger bridge for VaoDen. It only forwards the owning player's entry
/// to the authoritative completion controller; the server verifies the pose.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public sealed class SandBoatTempleShelter : MonoBehaviour
{
    [SerializeField, Tooltip("Nguồn trạng thái authoritative của Sand Boat Chase dùng để yêu cầu dừng bão tại VaoDen.")]
    private SandBoatChaseCompletion _completion;

    private void OnTriggerEnter(Collider other)
    {
        if (!Application.isPlaying || _completion == null)
        {
            return;
        }

        NetworkObject playerObject = other.GetComponentInParent<NetworkObject>();
        if (playerObject == null || !playerObject.IsOwner)
        {
            return;
        }

        _completion.RequestStormStopAtShelterServerRpc();
    }
}
