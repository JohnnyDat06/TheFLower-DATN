using Unity.Netcode;
using UnityEngine;

namespace Gameplay.Interactables
{
    /// <summary>
    /// Vật thể bẫy: Khi người chơi tương tác sẽ bị tiêu diệt ngay lập tức.
    /// Thường dùng cho các câu đố yêu cầu chọn đúng vật phẩm.
    /// </summary>
    public class TrapInteractable : InteractableBase
    {
        [Header("Trap Settings")]
        [SerializeField] private bool _isOneTimeTrap = false;
        [SerializeField] private SOAudioClip _trapActivationSound;
        [SerializeField] private GameObject _vfxPrefab;

        public override void Interact(ulong playerId)
        {
            if (!CanInteract) return;

            // Gửi yêu cầu kích hoạt bẫy lên Server
            TriggerTrapServerRpc(playerId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void TriggerTrapServerRpc(ulong playerId)
        {
            // Kiểm tra điều kiện trên Server
            if (!CanInteract) return;
            if (!CanPlayerInteract(playerId)) return;

            Debug.Log($"[TrapInteractable] <color=red>BAY KICH HOAT!</color> Player {playerId} da tuong tac nham.");

            // Tìm đối tượng Player và giết ngay lập tức
            if (TryGetPlayerObject(playerId, out NetworkObject playerObject))
            {
                if (playerObject.TryGetComponent<IDamageable>(out var damageable))
                {
                    damageable.InstantKill();
                }
            }

            // Đồng bộ trạng thái kích hoạt (Nếu là bẫy dùng 1 lần)
            if (_isOneTimeTrap)
            {
                ServerActivate();
            }

            // Gọi RPC để phát hiệu ứng hình ảnh/âm thanh trên tất cả Client
            NotifyTrapTriggeredClientRpc(playerObject.transform.position);
        }

        [ClientRpc]
        private void NotifyTrapTriggeredClientRpc(Vector3 playerPosition)
        {
            // Phát âm thanh tại vị trí bẫy hoặc người chơi
            if (_trapActivationSound != null)
            {
                // Có thể dùng một AudioSource tĩnh hoặc tạm thời ở đây
                AudioSource.PlayClipAtPoint(_trapActivationSound.Clip, transform.position, _trapActivationSound.Volume);
            }

            // Sinh hiệu ứng (VFX)
            if (_vfxPrefab != null)
            {
                Instantiate(_vfxPrefab, playerPosition + Vector3.up, Quaternion.identity);
            }
        }
    }
}
