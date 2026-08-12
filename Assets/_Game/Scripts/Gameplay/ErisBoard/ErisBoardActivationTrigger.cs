using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Entry point for the second Eris collider. It only forwards a validated player
/// identity to the networked Eris manager; the manager owns the puzzle state.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class ErisBoardActivationTrigger : MonoBehaviour
{
    [Tooltip("Kéo object ErisMinigame_Networker đang chứa ErisMinigameManager vào đây.")]
    [SerializeField] private ErisMinigameManager _manager;
    private Collider _collider;

    private void Awake()
    {
        _collider = GetComponent<Collider>();
        _manager ??= GetComponentInParent<ErisMinigameManager>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_manager == null
            || NetworkManager.Singleton == null
            || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        NetworkObject playerObject = other.GetComponentInParent<NetworkObject>();
        if (playerObject == null) return;

        bool isPlayer = other.CompareTag(Constants.Tags.PLAYER)
            || playerObject.CompareTag(Constants.Tags.PLAYER)
            || playerObject.GetComponent<NGOPlayerSync>() != null
            || playerObject.GetComponent<PlayerController>() != null;
        if (!isPlayer) return;

        _manager.TryStartBoardFromActivationTriggerServer(playerObject.OwnerClientId);
    }

    private void OnEnable()
    {
        if (_collider == null) _collider = GetComponent<Collider>();
        if (_collider != null) _collider.enabled = false;
    }
}
