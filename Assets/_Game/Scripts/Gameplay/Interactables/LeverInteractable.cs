using Unity.Netcode;
using UnityEngine;

// Lever uses the serialized UnityEvents inherited from InteractableBase.
public class LeverInteractable : InteractableBase
{
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _allowReactivation = true;
    }

    public override void Interact(ulong playerId)
    {
        ToggleServerRpc(playerId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleServerRpc(ulong playerId)
    {
        if (!CanPlayerInteract(playerId)) return;

        Debug.Log($"[LeverInteractable] <color=yellow>{_interactableId}</color> - Player {playerId} toggle. Trang thai hien tai: {IsActivated}");

        if (!IsActivated)
        {
            ServerActivate();
        }
        else
        {
            ServerDeactivate();
        }
    }

    protected override void OnActivatedValueChanged(bool previousValue, bool newValue)
    {
        base.OnActivatedValueChanged(previousValue, newValue);

        if (!newValue && previousValue)
        {
            Debug.Log($"[LeverInteractable] {_interactableId} da tat dong bo tren Client/Host.");
        }
    }
}
