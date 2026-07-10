using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Netcode;
using Unity.Cinemachine;

namespace Game.Core
{
    [System.Serializable]
    public class TrailerStep
    {
        public CinemachineCamera VirtualCamera;
        [TextArea(2, 5)]
        public string DialogueText;
        public float Duration = 3f;
        public SOAudioClip StepSFX; 
    }

    public class TrailerManager : NetworkBehaviour
    {
        public static TrailerManager Instance { get; private set; }

        [Header("Trailer Config")]
        [SerializeField] private List<TrailerStep> _steps;
        [SerializeField] private GameObject _subtitlePanel;
        [SerializeField] private TextMeshProUGUI _subtitleText;
        
        [Header("Audio Config")]
        [SerializeField] private SOAudioClip _backgroundMusic;

        private Coroutine _trailerCoroutine;
        private AudioSource _musicSource;
        private bool _isTrailerFinished = false;

        private void Awake()
        {
            Instance = this;
            if (_subtitlePanel != null) _subtitlePanel.SetActive(false);
            SetupDefaultDialogues();

            foreach (var step in _steps)
            {
                if (step.VirtualCamera != null) step.VirtualCamera.Priority = 0;
            }
        }

        private void SetupDefaultDialogues()
        {
            string[] texts = {
                "Ở nơi ngọn núi Thiên Không huyền thoại ấy, mây trắng bồng bềnh như dệt nên những giấc mơ êm đềm...",
                "Người xưa vẫn truyền tai nhau về một đóa hoa mang sắc màu gìn giữ những nụ cười.",
                "Kẻ nào chạm tay vào nó, mọi muộn phiền sẽ tan biến như khói sương, nhường chỗ cho niềm vui và bình yên.",
                "Con đường phía trước có thể gập ghềnh và gian nan, nhưng những cơn gió lạnh lẽo nhất cũng chẳng thể ngăn nổi bước chân chúng ta.",
                "Chỉ khi các bạn kề vai sát cánh, mỗi bước đi mới trở thành ký ức đẹp nhất. Hãy nắm lấy tay nhau. Bắt đầu thôi!"
            };

            for (int i = 0; i < _steps.Count; i++)
            {
                if (i < texts.Length)
                {
                    _steps[i].DialogueText = texts[i];
                }
            }
        }

        [ClientRpc]
        public void StartTrailerClientRpc()
        {
            _isTrailerFinished = false;
            if (_trailerCoroutine != null) StopCoroutine(_trailerCoroutine);
            
            foreach (var step in _steps) if (step.VirtualCamera != null) step.VirtualCamera.Priority = 0;
            
            _trailerCoroutine = StartCoroutine(PlayTrailerSequence());
        }

        private IEnumerator PlayTrailerSequence()
        {
            // 1. Phát nhạc/thoại
            if (_backgroundMusic != null && AudioManager.Instance != null)
            {
                _musicSource = AudioManager.Instance.PlayMusicOnce(_backgroundMusic);
            }

            if (_subtitlePanel != null) _subtitlePanel.SetActive(true);

            // 2. MỐC THỜI GIAN THEO YÊU CẦU CỦA BẠN (CỰC KỲ CHÍNH XÁC)
            float[] endTimes = { 4.0f, 9.0f, 14.0f, 19.0f, 42.5f };

            for (int i = 0; i < _steps.Count; i++)
            {
                var step = _steps[i];
                float targetEndTime = (i < endTimes.Length) ? endTimes[i] : 0;

                // CHUYỂN CẢNH TỨC THÌ
                foreach (var s in _steps) if (s.VirtualCamera != null) s.VirtualCamera.Priority = 0;
                if (step.VirtualCamera != null) step.VirtualCamera.Priority = 1000; 
                
                if (_subtitleText != null) _subtitleText.text = step.DialogueText;

                // ĐỢI ĐẾN ĐÚNG GIÂY YÊU CẦU
                if (_musicSource != null && _musicSource.clip != null)
                {
                    while (_musicSource.isPlaying && _musicSource.time < targetEndTime)
                    {
                        yield return null; 
                    }
                }
                else
                {
                    // Fallback
                    float duration = (i == 0) ? 4f : 5f; 
                    yield return new WaitForSeconds(duration);
                }
            }

            FinishTrailer();
        }

        private void FinishTrailer()
        {
            if (_isTrailerFinished) return;
            _isTrailerFinished = true;

            if (_musicSource != null && AudioManager.Instance != null)
            {
                AudioManager.Instance.StopSFX(_musicSource);
                _musicSource = null;
            }

            if (_subtitlePanel != null) _subtitlePanel.SetActive(false);
            foreach (var step in _steps) if (step.VirtualCamera != null) step.VirtualCamera.Priority = 0;
            if (CameraManager.Instance != null) CameraManager.Instance.SwitchCamera(CameraPreset.ThirdPerson);
            ReportTrailerFinishedServerRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportTrailerFinishedServerRpc(RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            {
                if (client.PlayerObject != null && client.PlayerObject.TryGetComponent<NGOPlayerSync>(out var sync))
                {
                    sync.ReleasePlayerClientRpc();
                }
            }
        }
    }
}
