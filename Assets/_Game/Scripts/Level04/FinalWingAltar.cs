using Unity.Netcode;

public class FinalWingAltar : CoopInteractable
{
    public override void Interact(ulong playerId)
    {
        if (Level04FlowManager.Instance != null
            && Level04FlowManager.Instance.CanUseHostSoloDebug(playerId))
        {
            ActivateSoloServerRpc(playerId);
            return;
        }

        base.Interact(playerId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ActivateSoloServerRpc(
        ulong playerId,
        ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != playerId) return;
        if (Level04FlowManager.Instance == null
            || !Level04FlowManager.Instance.CanUseHostSoloDebug(playerId))
        {
            return;
        }

        ServerActivate();
    }

    protected override void OnActivatedValueChanged(bool previousValue, bool newValue)
    {
        base.OnActivatedValueChanged(previousValue, newValue);

        if (newValue && !previousValue && IsServer)
        {
            Level04FlowManager.Instance?.BeginWingUnlockServer();
        }
    }
}
