using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace Environment
{
    /// <summary>
    /// Script test mở cửa đơn giản cho Co-op: di chuyển lên cao, phát âm thanh và có thể tắt active.
    /// </summary>
    public class SimpleSlidingDoor : NetworkBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private Vector3 _moveDirection = Vector3.up;
        [SerializeField] private float _moveDistance = 3f;
        [SerializeField] private float _moveSpeed = 2f;
        [SerializeField] private bool _deactivateAfterOpen = false;
        [SerializeField] private float _deactivateDelay = 0.5f;

        [Header("Audio")]
        [SerializeField] private SOAudioClip _openSound;
        [SerializeField] private AudioSource _audioSource;

        // Đồng bộ trạng thái mở cửa qua mạng
        private NetworkVariable<bool> _isOpen = new NetworkVariable<bool>(
            false, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server);

        private Vector3 _startPosition;
        private Vector3 _targetPosition;
        private void Awake()
        {
            _startPosition = transform.position;
            _targetPosition = _startPosition + (_moveDirection.normalized * _moveDistance);

            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
                if (_audioSource == null)
                {
                    _audioSource = gameObject.AddComponent<AudioSource>();
                    _audioSource.spatialBlend = 1f; // 3D Sound
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            _isOpen.OnValueChanged += OnDoorStateChanged;
            
            // Nếu đã mở từ trước (client join muộn), đặt vị trí ngay lập tức
            if (_isOpen.Value)
            {
                transform.position = _targetPosition;
                if (_deactivateAfterOpen) gameObject.SetActive(false);
            }
        }

        public override void OnNetworkDespawn()
        {
            _isOpen.OnValueChanged -= OnDoorStateChanged;
        }

        /// <summary>
        /// Hàm này sẽ được gọi từ UnityEvent của PuzzleObjectiveManager (Chỉ Server gọi được hàm này qua NetworkVariable).
        /// </summary>
        [ContextMenu("Open Door")]
        public void Open()
        {
            if (!IsServer) return;
            if (_isOpen.Value) return;

            _isOpen.Value = true;
        }

        private void OnDoorStateChanged(bool previousValue, bool newValue)
        {
            if (newValue && !previousValue)
            {
                StartCoroutine(OpenDoorRoutine());
            }
        }

        private IEnumerator OpenDoorRoutine()
        {
            
            // Phát âm thanh
            PlaySound(_openSound);

            float elapsed = 0f;
            Vector3 initialPos = transform.position;

            while (elapsed < 1f)
            {
                elapsed += Time.deltaTime * (_moveSpeed / _moveDistance);
                transform.position = Vector3.Lerp(initialPos, _targetPosition, elapsed);
                yield return null;
            }

            transform.position = _targetPosition;

            // Tắt active nếu được cấu hình
            if (_deactivateAfterOpen)
            {
                yield return new WaitForSeconds(_deactivateDelay);
                
                // Lưu ý: Trong NetworkObject, tắt active gameObject cần cẩn thận. 
                // Ở đây ta chỉ tắt Renderer/Collider hoặc chính nó nếu đơn giản.
                if (IsServer) 
                {
                    // Tắt trên server sẽ tự đồng bộ nếu nó là NetworkObject (thường là vậy)
                    gameObject.SetActive(false); 
                }
                else
                {
                    // Client tự tắt cục bộ để tránh lỗi visual
                    gameObject.SetActive(false);
                }
            }
        }

        private void PlaySound(SOAudioClip soClip)
        {
            if (soClip == null || _audioSource == null) return;

            _audioSource.clip = soClip.Clip;
            _audioSource.volume = soClip.Volume;
            _audioSource.pitch = Random.Range(soClip.PitchMin, soClip.PitchMax);
            _audioSource.Play();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Vector3 target = transform.position + (_moveDirection.normalized * _moveDistance);
            Gizmos.DrawLine(transform.position, target);
            Gizmos.DrawWireCube(target, transform.localScale);
        }
    }
}
