using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class SoloInteractable : InteractableBase
{
    private ulong _lastInteractedPlayerId;
    private bool _hasActivatingPlayer = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // Luôn cho phép kích hoạt lại để có thể mở/đóng nhiều lần
        if (IsServer)
        {
            _allowReactivation = true;
        }
    }

    public override void Interact(ulong playerId)
    {
        if (!CanInteract) return;
        ActivateServerRpc(playerId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ActivateServerRpc(ulong playerId)
    {
        if (!CanInteract) return;
        if (!CanPlayerInteract(playerId)) return;

        Debug.Log($"[SoloInteractable] {_interactableId} - Kích hoạt bởi Player {playerId}");
        
        _lastInteractedPlayerId = playerId;
        _hasActivatingPlayer = true;
        ServerActivate();
    }

    private void Update()
    {
        // Chỉ xử lý trên Server và khi đang được bật
        if (!IsServer || !IsActivated || !_hasActivatingPlayer) return;

        // Kiểm tra khoảng cách thực tế từ Player đến cần gạt
        if (!CheckPlayerInRange(_lastInteractedPlayerId))
        {
            Debug.Log($"[SoloInteractable] {_interactableId} - Player {_lastInteractedPlayerId} đã rời xa quá {_maxInteractDistance}m. Đang đóng cầu...");
            _hasActivatingPlayer = false;
            ServerDeactivate();
        }
    }

    private bool CheckPlayerInRange(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return false;
        if (client.PlayerObject == null) return false;

        // Tính khoảng cách từ tâm Player đến tâm của Cần gạt
        float distance = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
        
        // Trả về true nếu vẫn còn trong phạm vi
        return distance <= (_maxInteractDistance + 0.5f); // Thêm một chút bù trừ sai số
    }

    protected override void OnActivatedValueChanged(bool previousValue, bool newValue)
    {
        base.OnActivatedValueChanged(previousValue, newValue);
        
        Debug.Log($"[SoloInteractable] {_interactableId} thay đổi trạng thái: {previousValue} -> {newValue}");

        if (!newValue)
        {
            _hasActivatingPlayer = false;
        }
    }
}
