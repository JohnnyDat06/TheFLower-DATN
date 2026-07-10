using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class LeverInteractable : InteractableBase
{
    [Header("Lever Settings")]
    public new UnityEvent OnDeactivated;

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
            OnDeactivated?.Invoke();
            Debug.Log($"[LeverInteractable] {_interactableId} da tat dong bo tren Client/Host.");
        }
    }
}
