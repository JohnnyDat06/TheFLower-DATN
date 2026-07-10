using Unity.Netcode;
using UnityEngine;
using System.Collections;

/// <summary>
/// MapCompletionTrigger — Kích hoạt hiệu ứng "To Be Continued" và quay về Lobby khi có bất kỳ player nào chạm vào.
/// </summary>
public class MapCompletionTrigger : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private string _lobbySceneName = Constants.Scenes.LOBBY;
    [SerializeField] private float _delayBeforeLoad = 4.0f;

    private bool _isTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (_isTriggered) return;

        if (other.CompareTag(Constants.Tags.PLAYER))
        {
            Debug.Log($"[MapCompletionTrigger] Player {other.name} entered completion zone.");
            
            if (IsServer)
            {
                StartCompletionSequence();
            }
            else
            {
                // Nếu là Client chạm vào, gửi Request lên Server
                StartCompletionSequenceServerRpc();
            }
            
            _isTriggered = true;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void StartCompletionSequenceServerRpc()
    {
        StartCompletionSequence();
    }

    private void StartCompletionSequence()
    {
        if (!IsServer) return;
        
        _isTriggered = true;
        StartCoroutine(CompletionRoutine());
    }

    private IEnumerator CompletionRoutine()
    {
        Debug.Log("<color=cyan>[MapCompletionTrigger] Starting Completion Sequence...</color>");

        // 1. Hiển thị chữ "To Be Continued" và ẨN thanh progress bar trên tất cả các máy
        if (LoadingSyncManager.Instance != null)
        {
            LoadingSyncManager.Instance.ShowToBeContinuedClientRpc(true, "To Be Continued!", false);
            LoadingSyncManager.Instance.FadeInClientRpc();
        }

        // 2. Đợi một khoảng thời gian để người chơi đọc được chữ
        yield return new WaitForSecondsRealtime(_delayBeforeLoad);

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

        // 4. (Tùy chọn) Ẩn chữ "To Be Continued" sau khi load xong (thường thì scene mới sẽ reset UI này)
        // Nhưng để chắc chắn, LoadingSyncManager có thể tắt nó khi EndLoadingFadeClientRpc được gọi ở scene mới.
    }
}
