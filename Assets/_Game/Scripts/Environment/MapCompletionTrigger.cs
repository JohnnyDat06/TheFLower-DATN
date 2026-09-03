using Unity.Netcode;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// MapCompletionTrigger — Kích hoạt hiệu ứng "The End!" và quay về Lobby sau khi đủ số người chơi vào cổng.
/// </summary>
public class MapCompletionTrigger : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField, Tooltip("Tên scene Lobby sẽ tải sau khi Credit kết thúc.")]
    private string _lobbySceneName = Constants.Scenes.LOBBY;
    [SerializeField, Min(0f), Tooltip("Thời gian chờ tối thiểu trước khi kiểm tra Credit đã chạy xong.")]
    private float _delayBeforeLoad = 4.0f;
    [SerializeField, Min(1), Tooltip("Số Network Player khác nhau phải cùng ở trong cổng trước khi Credit bắt đầu.")]
    private int _requiredPlayerCount = 1;

    private bool _isTriggered = false;
    private readonly HashSet<ulong> _playersInside = new();

    private void OnEnable()
    {
        _playersInside.Clear();
    }

    private void OnDisable()
    {
        _playersInside.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_isTriggered || !IsServer || !TryGetPlayerClientId(other, out ulong playerClientId)) return;

        if (_playersInside.Add(playerClientId))
        {
            Debug.Log(
                $"[MapCompletionTrigger] Player {playerClientId} entered completion zone. " +
                $"{_playersInside.Count}/{Mathf.Max(1, _requiredPlayerCount)} players ready.",
                this);
        }

        if (_playersInside.Count >= Mathf.Max(1, _requiredPlayerCount))
            TriggerCompletion();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer || !TryGetPlayerClientId(other, out ulong playerClientId)) return;
        _playersInside.Remove(playerClientId);
    }

    private bool TryGetPlayerClientId(Collider other, out ulong playerClientId)
    {
        playerClientId = default;
        if (other == null) return false;

        NetworkObject playerObject = other.GetComponentInParent<NetworkObject>();
        if (playerObject == null || !playerObject.IsPlayerObject || !playerObject.IsSpawned) return false;
        if (!other.CompareTag(Constants.Tags.PLAYER) && !playerObject.CompareTag(Constants.Tags.PLAYER)) return false;

        playerClientId = playerObject.OwnerClientId;
        return NetworkManager != null && NetworkManager.ConnectedClients.ContainsKey(playerClientId);
    }

    /// <summary>
    /// Bắt đầu Credit authoritative sau khi điều kiện của cổng đã được đáp ứng.
    /// Hàm public được giữ để các completion flow một người chơi hiện có tiếp tục tái sử dụng trigger này.
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
            while (!SeamlessLoadingOverlay.Instance.IsEndCreditsComplete)
            {
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
