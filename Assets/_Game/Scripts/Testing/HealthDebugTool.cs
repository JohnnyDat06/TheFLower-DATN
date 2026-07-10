using UnityEngine;
using Unity.Netcode;

/// <summary>
/// HealthDebugTool — Công cụ debug để test trừ máu đồng bộ mạng.
/// Nhấn H: Trừ 10 máu Host (ClientId 0).
/// Nhấn C: Trừ 10 máu Client (ClientId 1).
/// </summary>
public class HealthDebugTool : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private float _damageAmount = 10f;

    private void Update()
    {
        // Chỉ nhận input từ máy cục bộ đang cầm máy
        if (Input.GetKeyDown(KeyCode.H))
        {
            Debug.Log("[HealthDebugTool] Requesting damage for Host...");
            RequestDamageServerRpc(0); // Host luôn là 0
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            Debug.Log("[HealthDebugTool] Requesting damage for Client...");
            RequestDamageServerRpc(1); // Client đầu tiên thường là 1
        }

        if (Input.GetKeyDown(KeyCode.O))
        {
            RequestRestoreFullHealthServerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestRestoreFullHealthServerRpc(RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        var allHealths = Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);

        foreach (var health in allHealths)
        {
            if (health.OwnerClientId == senderClientId)
            {
                health.RestoreFullHealth();
                return;
            }
        }
    }

    /// <summary>
    /// Gửi yêu cầu trừ máu lên Server.
    /// ServerRpc cho phép Client ra lệnh cho Server thực thi logic quan trọng.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestDamageServerRpc(ulong targetClientId)
    {
        // Server tìm tất cả PlayerHealth trong scene
        var allHealths = Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        
        foreach (var health in allHealths)
        {
            if (health.OwnerClientId == targetClientId)
            {
                health.TakeDamage(_damageAmount);
                Debug.Log($"[HealthDebugTool] Server applied {_damageAmount} damage to Player {targetClientId}");
                return;
            }
        }

        Debug.LogWarning($"[HealthDebugTool] Player with ID {targetClientId} not found!");
    }
}
