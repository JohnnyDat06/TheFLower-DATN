using Unity.Netcode;
using UnityEngine;
using MoreMountains.Feedbacks;

namespace Game.Parkour
{
    /// <summary>
    /// MushroomBounce — Xử lý logic nấm bật nhảy.
    /// Sử dụng Feel (MMF_Player) cho hiệu ứng và Netcode để đồng bộ visual.
    /// </summary>
    public class MushroomBounce : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private SOMushroomConfig _config;
        
        [Header("Feel")]
        [SerializeField] private MMF_Player _feedback;

        private float _lastBounceTime;
        private float _serverLastBounceTime;

        private void OnTriggerEnter(Collider other)
        {
            // Kiểm tra cooldown (Local prediction)
            if (Time.time < _lastBounceTime + _config.CooldownTime) return;

            // Kiểm tra xem có phải Player không thông qua PlayerController
            if (other.TryGetComponent<PlayerController>(out var player))
            {
                // Logic vật lý: CHỈ thực hiện trên Owner của Player đó để tránh delay/giật
                if (player.IsOwner)
                {
                    player.Bounce(_config.BounceForce);
                    _lastBounceTime = Time.time;
                    
                    // Gửi yêu cầu tới Server để đồng bộ hiệu ứng hình ảnh cho mọi người
                    PlayBounceVisualServerRpc();
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void PlayBounceVisualServerRpc()
        {
            // Kiểm tra cooldown thực tế trên Server
            if (Time.time < _serverLastBounceTime + _config.CooldownTime) return;
            _serverLastBounceTime = Time.time;

            // Server ra lệnh cho toàn bộ Client phát hiệu ứng
            PlayBounceVisualClientRpc();
        }

        [ClientRpc]
        private void PlayBounceVisualClientRpc()
        {
            // Cập nhật cooldown local trên mọi Client để đồng bộ visual
            _lastBounceTime = Time.time;

            // Phát Feel Feedbacks (Scale, Sound, v.v.)
            if (_feedback != null)
            {
                _feedback.PlayFeedbacks();
            }

            // Sinh VFX bào tử từ Config nếu có
            if (_config != null && _config.VFXPrefab != null)
            {
                // Thường VFX môi trường đơn giản không cần NetworkObject, chỉ cần spawn local trên mỗi máy
                Instantiate(_config.VFXPrefab, transform.position + Vector3.up, Quaternion.identity);
            }
        }
    }
}
