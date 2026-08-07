using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
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
        [SerializeField] private Button _skipButton;
        
        [Header("Audio Config")]
        [SerializeField] private SOAudioClip _backgroundMusic;

        private Coroutine _trailerCoroutine;
        private AudioSource _musicSource;
        private Canvas _subtitleCanvas;
        private bool _isTrailerFinished = false;

        // Audio playback can be paused, looped, or fail to advance on a peer.
        // Keep the intro bounded so a client cannot remain in the cutscene forever.
        private const float TrailerMaximumDuration = 50f;

        private void Awake()
        {
            Instance = this;
            if (_subtitlePanel != null) _subtitlePanel.SetActive(false);
            _subtitleCanvas = _subtitlePanel != null ? _subtitlePanel.GetComponentInParent<Canvas>() : null;
            if (_subtitleCanvas != null)
            {
                _subtitleCanvas.overrideSorting = true;
                _subtitleCanvas.sortingOrder = 500;
            }
            if (_skipButton != null)
            {
                _skipButton.gameObject.SetActive(false);
                _skipButton.onClick.AddListener(SkipTrailer);
            }
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
            
            if (_skipButton != null) _skipButton.gameObject.SetActive(true);

            _trailerCoroutine = StartCoroutine(PlayTrailerSequence());
        }

        private void Update()
        {
            if (!_isTrailerFinished && _trailerCoroutine != null)
            {
                // Check skip inputs
                if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    SkipTrailer();
                }
                else if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame) // buttonSouth is usually 'X' or 'A'
                {
                    SkipTrailer();
                }
            }
        }

        public void SkipTrailer()
        {
            if (_isTrailerFinished) return;

            if (_trailerCoroutine != null)
            {
                StopCoroutine(_trailerCoroutine);
                _trailerCoroutine = null;
            }

            FinishTrailer();
        }

        private IEnumerator PlayTrailerSequence()
        {
            float deadline = Time.realtimeSinceStartup + TrailerMaximumDuration;
            // 1. Phát nhạc/thoại
            if (_backgroundMusic != null && AudioManager.Instance != null)
            {
                _musicSource = AudioManager.Instance.PlayMusicOnce(_backgroundMusic);
            }

            if (_subtitleCanvas != null)
            {
                _subtitleCanvas.overrideSorting = true;
                _subtitleCanvas.sortingOrder = 500;
                _subtitleCanvas.enabled = true;
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
                
                if (_subtitlePanel != null && !_subtitlePanel.activeSelf) _subtitlePanel.SetActive(true);
                if (_subtitleText != null)
                {
                    _subtitleText.enabled = true;
                    _subtitleText.text = step.DialogueText;
                    _subtitleText.ForceMeshUpdate();
                }

                // ĐỢI ĐẾN ĐÚNG GIÂY YÊU CẦU
                if (_musicSource != null && _musicSource.clip != null)
                {
                    while (_musicSource.isPlaying
                           && _musicSource.time < targetEndTime
                           && Time.realtimeSinceStartup < deadline)
                    {
                        yield return null; 
                    }
                }
                else
                {
                    // Fallback
                    float duration = (i == 0) ? 4f : 5f; 
                    yield return new WaitForSecondsRealtime(duration);
                }

                if (Time.realtimeSinceStartup >= deadline)
                {
                    Debug.LogWarning("[TrailerManager] Intro exceeded its safety timeout; returning to gameplay camera.");
                    break;
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
            if (_skipButton != null) _skipButton.gameObject.SetActive(false);
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
                    sync.ReleaseServerSimulation();
                    sync.ReleasePlayerClientRpc();
                }
            }
        }
    }
}
