using Unity.Netcode;
using UnityEngine;
using System.Collections;

/// <summary>
/// MapCompletionTrigger — Kích hoạt hiệu ứng "The End!" và quay về Lobby khi có bất kỳ player nào chạm vào.
/// </summary>
public class MapCompletionTrigger : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private string _lobbySceneName = Constants.Scenes.LOBBY;
    [SerializeField] private float _delayBeforeLoad = 4.0f;
    [SerializeField] private float _endCreditsTimeout = 120.0f;

    private bool _isTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (_isTriggered) return;

        if (other.CompareTag(Constants.Tags.PLAYER))
        {
            Debug.Log($"[MapCompletionTrigger] Player {other.name} entered completion zone.");
            TriggerCompletion();
        }
    }

    /// <summary>
    /// Bắt đầu credit authoritative. TheFlower gọi hàm này ngay sau khi hai người
    /// chơi tương tác thành công; trigger vật lý vẫn được giữ làm đường dự phòng.
    /// </summary>
    public void TriggerCompletion()
    {
        if (_isTriggered) return;

        if (IsServer)
        {
            StartCompletionSequence();
            return;
        }

        _isTriggered = true;
        StartCompletionSequenceServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void StartCompletionSequenceServerRpc()
    {
        StartCompletionSequence();
    }

    private void StartCompletionSequence()
    {
        if (!IsServer || _isTriggered) return;
        
        _isTriggered = true;
        StartCoroutine(CompletionRoutine());
    }

    private IEnumerator CompletionRoutine()
    {
        Debug.Log("<color=cyan>[MapCompletionTrigger] Starting Completion Sequence...</color>");

        // 1. Hiển thị end credits và ẨN thanh progress bar trên tất cả các máy
        bool endCreditsStarted = false;
        if (LoadingSyncManager.Instance != null)
        {
            LoadingSyncManager.Instance.ShowEndCreditsClientRpc();
            endCreditsStarted = true;
        }

        // 2. Chờ end credits chạy hết trước khi quay về Lobby.
        yield return new WaitForSecondsRealtime(_delayBeforeLoad);
        if (endCreditsStarted && SeamlessLoadingOverlay.Instance != null)
        {
            float creditsElapsed = 0f;
            while (!SeamlessLoadingOverlay.Instance.IsEndCreditsComplete)
            {
                creditsElapsed += Time.unscaledDeltaTime;
                if (creditsElapsed >= _endCreditsTimeout)
                {
                    Debug.LogWarning("[MapCompletionTrigger] End credits timed out; continuing to Lobby.");
                    break;
                }

                yield return null;
            }
        }

        // 3. Chuyển về scene Lobby
        Debug.Log($"[MapCompletionTrigger] Loading Lobby: {_lobbySceneName}");
        
        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadScene(_lobbySceneName);
        }
        else
        {
            if (SceneLoader.CanLoadScene(_lobbySceneName))
                NetworkManager.SceneManager.LoadScene(_lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        // 4. (Tùy chọn) Ẩn end credits sau khi load xong (thường thì scene mới sẽ reset UI này)
        // Nhưng để chắc chắn, LoadingSyncManager có thể tắt nó khi EndLoadingFadeClientRpc được gọi ở scene mới.
    }
}
