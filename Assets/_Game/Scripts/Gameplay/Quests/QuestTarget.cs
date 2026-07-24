using Unity.Netcode;
using UnityEngine;

/// <summary>Điểm đích có thể được dùng cho bước requiresInteraction.</summary>
[RequireComponent(typeof(Collider))]
public sealed class QuestTarget : InteractableBase
{
    [Header("Quest Target")]
    [SerializeField] private string questTargetId = "step_01";
    public string QuestTargetId => questTargetId;

    protected override void Awake()
    {
        _interactableId = questTargetId;
        base.Awake();
    }

    public override void Interact(ulong playerId)
    {
        if (!CanInteract) return;
        if (IsServer) ActivateForPlayer(playerId);
        else RequestInteractionServerRpc(playerId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestInteractionServerRpc(ulong playerId, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != playerId) return;
        ActivateForPlayer(playerId);
    }

    private void ActivateForPlayer(ulong playerId)
    {
        if (!CanPlayerInteract(playerId)) return;
        ServerActivate();
    }

    public override void ResetInteractable()
    {
        base.ResetInteractable();
    }
}
