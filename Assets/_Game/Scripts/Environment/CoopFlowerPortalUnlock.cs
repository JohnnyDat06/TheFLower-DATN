using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Requires two different network players to interact with the final flower inside one sync window,
/// then hides the flower visual and enables the game-completion portal.
/// </summary>
[RequireComponent(typeof(NetworkObject), typeof(Collider))]
public sealed class CoopFlowerPortalUnlock : NetworkBehaviour, IInteractable
{
    private const ulong NoPlayer = ulong.MaxValue;

    [Tooltip("Text displayed by the shared Interact prompt while the flower is available.")]
    [SerializeField] private string _interactionPrompt = "Interact with the Flower";
    [Tooltip("Maximum server-validated distance between a player and the flower.")]
    [SerializeField, Min(0.5f)] private float _interactionDistance = 3f;
    [Tooltip("Seconds allowed for the second player to interact after the first player.")]
    [SerializeField, Min(0.1f)] private float _syncWindow = 3f;
    [Tooltip("Visual child hidden after both players complete the interaction.")]
    [SerializeField] private GameObject _flowerVisual;
    [Tooltip("Completion trigger enabled only after the flower has been unlocked.")]
    [SerializeField] private MapCompletionTrigger _completionPortal;

    private readonly NetworkVariable<bool> _isUnlocked = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Collider _interactionCollider;
    private ulong _firstPlayerId = NoPlayer;
    private float _firstInteractionExpiresAt;

    public string InteractionPrompt => _interactionPrompt;
    public bool CanInteract => !_isUnlocked.Value;
    public bool IsActivated => _isUnlocked.Value;

    private void Awake()
    {
        _interactionCollider = GetComponent<Collider>();
        ApplyUnlockedPresentation(_isUnlocked.Value);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isUnlocked.OnValueChanged += HandleUnlockedChanged;
        ApplyUnlockedPresentation(_isUnlocked.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isUnlocked.OnValueChanged -= HandleUnlockedChanged;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsServer || _firstPlayerId == NoPlayer) return;
        if (Time.time <= _firstInteractionExpiresAt && IsPlayerInRange(_firstPlayerId)) return;

        _firstPlayerId = NoPlayer;
        _firstInteractionExpiresAt = 0f;
        Debug.Log("[CoopFlowerPortalUnlock] Ready state cleared. Both players must interact together near the flower.", this);
    }

    public void Interact(ulong playerId)
    {
        if (!CanInteract || !IsSpawned) return;
        RequestInteractionRpc(playerId);
    }

    public void OnHoverEnter()
    {
    }

    public void OnHoverExit()
    {
    }

    public Transform GetPromptTransform() => transform;

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestInteractionRpc(ulong playerId, RpcParams rpcParams = default)
    {
        if (_isUnlocked.Value || rpcParams.Receive.SenderClientId != playerId || !IsPlayerInRange(playerId)) return;

        if (_firstPlayerId == NoPlayer || Time.time > _firstInteractionExpiresAt)
        {
            _firstPlayerId = playerId;
            _firstInteractionExpiresAt = Time.time + _syncWindow;
            Debug.Log($"[CoopFlowerPortalUnlock] Player {playerId} is ready. Waiting for the second player.", this);
            return;
        }

        if (_firstPlayerId == playerId) return;

        _isUnlocked.Value = true;
        ApplyUnlockedPresentation(true);
        Debug.Log("[CoopFlowerPortalUnlock] Both players activated TheFlower. Completion portal unlocked.", this);
    }

    private bool IsPlayerInRange(ulong playerId)
    {
        if (NetworkManager == null ||
            !NetworkManager.ConnectedClients.TryGetValue(playerId, out NetworkClient client) ||
            client.PlayerObject == null)
            return false;

        Vector3 playerPosition = client.PlayerObject.transform.position;
        Vector3 interactionPoint = _interactionCollider != null
            ? _interactionCollider.ClosestPoint(playerPosition)
            : transform.position;
        return Vector3.Distance(playerPosition, interactionPoint) <= _interactionDistance;
    }

    private void HandleUnlockedChanged(bool previousValue, bool newValue)
    {
        ApplyUnlockedPresentation(newValue);
    }

    private void ApplyUnlockedPresentation(bool isUnlocked)
    {
        if (_flowerVisual != null) _flowerVisual.SetActive(!isUnlocked);
        if (_interactionCollider != null) _interactionCollider.enabled = !isUnlocked;
        if (_completionPortal != null) _completionPortal.enabled = isUnlocked;
    }
}
