using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// LoadingSyncManager — Đồng bộ hiệu ứng mờ màn hình giữa các máy.
/// </summary>
public class LoadingSyncManager : NetworkBehaviour
{
    public static LoadingSyncManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private readonly System.Collections.Generic.HashSet<ulong> _readyClients = new System.Collections.Generic.HashSet<ulong>();

    public void ClearReadyClients()
    {
        if (!IsServer) return;
        _readyClients.Clear();
        Debug.Log("[LoadingSyncManager] Cleared ready clients list on Server.");
    }

    public void MarkClientReady(ulong clientId)
    {
        if (!IsServer) return;
        _readyClients.Add(clientId);
        Debug.Log($"[LoadingSyncManager] Client {clientId} marked ready. Total ready: {_readyClients.Count}");
    }

    public bool IsClientReady(ulong clientId)
    {
        if (!IsServer) return false;
        return _readyClients.Contains(clientId);
    }

    [Rpc(SendTo.Everyone)]
    public void StartLoadingFadeClientRpc()
    {
        if (IsServer)
        {
            ClearReadyClients();
        }

        if (SeamlessLoadingOverlay.Instance != null)
        {
            SeamlessLoadingOverlay.Instance.EnsureLoadingVisible();
            
            // Nếu là Client, hãy bắt đầu mô phỏng tiến trình load
            if (!IsHost && SceneLoader.Instance != null)
            {
                SceneLoader.Instance.StartClientLoadingSimulation();
            }
        }
    }

    [Rpc(SendTo.Everyone)]
    public void ShowToBeContinuedClientRpc(bool show, string text = "To Be Continued!", bool showProgressBar = true)
    {
        if (SeamlessLoadingOverlay.Instance != null)
        {
            SeamlessLoadingOverlay.Instance.ShowToBeContinued(show, text);
            SeamlessLoadingOverlay.Instance.ShowProgressBar(showProgressBar);
        }
    }

    [Rpc(SendTo.Everyone)]
    public void FadeInClientRpc()
    {
        if (SeamlessLoadingOverlay.Instance != null)
        {
            SeamlessLoadingOverlay.Instance.FadeIn();
        }
    }

    [Rpc(SendTo.Everyone)]
    public void EndLoadingFadeClientRpc()
    {
        Debug.Log("<color=green><b>[SYNC] Triggering final FadeOut on all clients!</b></color>");
        if (SeamlessLoadingOverlay.Instance != null)
        {
            SeamlessLoadingOverlay.Instance.SetProgress(1.0f);
            SeamlessLoadingOverlay.Instance.ShowToBeContinued(false); 
            SeamlessLoadingOverlay.Instance.ShowProgressBar(true); // Reset lại để dùng cho lần sau
            
            // Đợi thêm một chút để thấy 100% rồi mới mở ra
            StartCoroutine(WaitThenFadeOut());
        }
    }

    private IEnumerator WaitThenFadeOut()
    {
        yield return new WaitForSecondsRealtime(1.2f);
        if (SeamlessLoadingOverlay.Instance != null)
        {
            SeamlessLoadingOverlay.Instance.FadeOut();
        }
    }
}
