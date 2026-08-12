using Unity.Netcode;
using UnityEngine;

/// <summary>
/// CheckpointTrigger — Kích hoạt Checkpoint khi 1 player đi qua.
/// Bắn sự kiện kèm theo vị trí Host và Client để RespawnManager lưu lại.
/// Mặc định chỉ Server (Host) mới kích hoạt trigger để tránh trùng lặp.
/// </summary>
public class CheckpointTrigger : MonoBehaviour
{
    [Header("Spawn Points")]
    [SerializeField] private Transform _hostSpawnPoint;
    [SerializeField] private Transform _clientSpawnPoint;
    
    // Đảm bảo chỉ trigger 1 lần duy nhất cho mỗi điểm
    private bool _isTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        TryActivateCheckpoint(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryActivateCheckpoint(other);
    }

    private void TryActivateCheckpoint(Collider other)
    {
        if (_isTriggered) return;

        // Chỉ Server mới xác nhận Checkpoint để tránh gọi sự kiện nhiều lần rác
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;
        NetworkObject playerObject = other.GetComponentInParent<NetworkObject>();
        if (playerObject == null) return;
        if (!other.CompareTag("Player")
            && !playerObject.CompareTag("Player")
            && playerObject.GetComponent<PlayerController>() == null)
        {
            return;
        }

        // Nếu designer quên gắn Transform vào inspector, lấy biến GameObject hiện tại làm gốc
        Vector3 hostPos = _hostSpawnPoint != null ? _hostSpawnPoint.position : transform.position + Vector3.right;
        Vector3 clientPos = _clientSpawnPoint != null ? _clientSpawnPoint.position : transform.position + Vector3.left;

        // Relay có thể spawn player trước khi RespawnManager đăng ký EventBus.
        // Chỉ khóa checkpoint sau khi đã có hệ thống nhận; OnTriggerStay sẽ retry.
        if (!EventBus.RaiseCheckpointReached(gameObject.name, hostPos, clientPos)) return;

        _isTriggered = true;
        Debug.Log($"[CheckpointTrigger] Reached: {gameObject.name}");
    }
}
