using Unity.Netcode;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// CoopSceneTeleporter — Kích hoạt chuyển màn khi cả 2 player cùng đứng vào vùng Trigger.
/// Việc đặt vị trí người chơi ở scene mới sẽ do PlayerSpawner trong scene đó tự động xử lý.
/// </summary>
public class CoopSceneTeleporter : NetworkBehaviour
{
    [Header("Scene Settings")]
    [Tooltip("Tên scene cần chuyển tới (Phải có trong Build Settings)")]
    [SerializeField] private string _nextSceneName;

    [Header("Visual Feedback (Optional)")]
    [SerializeField] private GameObject _vfxIndicator;

    private readonly List<ulong> _playersInside = new List<ulong>();
    private bool _isTransitioning = false;

    private void Start()
    {
        if (GetComponent<NetworkObject>() == null)
        {
            Debug.LogError($"<color=red>[CoopSceneTeleporter] LỖI CỰC NẶNG: GameObject '{gameObject.name}' THIẾU COMPONENT NETWORKOBJECT. Script này sẽ không chạy được IsServer!</color>");
        }
        else
        {
            Debug.Log($"[CoopSceneTeleporter] Script đã sẵn sàng trên '{gameObject.name}'. Đang đợi người chơi...");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // CHUYỂN LOG LÊN ĐÂY: Để xem có va chạm vật lý hay không trước khi xét Server/Client
        Debug.Log($"<color=yellow>[CoopSceneTeleporter] VA CHẠM VẬT LÝ: {other.name} (Tag: {other.tag}) đã chạm vào tôi!</color>");

        if (!IsServer) 
        {
            Debug.Log("[CoopSceneTeleporter] Va chạm được ghi nhận trên Client, nhưng Server mới có quyền xử lý logic.");
            return;
        }

        if (other.CompareTag(Constants.Tags.PLAYER))
        {
            var networkObject = other.GetComponentInParent<NetworkObject>();
            if (networkObject != null)
            {
                ulong clientId = networkObject.OwnerClientId;
                if (!_playersInside.Contains(clientId))
                {
                    _playersInside.Add(clientId);
                    Debug.Log($"[CoopSceneTeleporter] Player {clientId} đã vào vùng. Tổng số trong vùng: {_playersInside.Count}. Tổng số client kết nối: {NetworkManager.Singleton.ConnectedClients.Count}");
                    CheckTransition();
                }
            }
            else
            {
                Debug.LogWarning($"[CoopSceneTeleporter] {other.name} có tag Player nhưng không tìm thấy NetworkObject ở cha!");
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;

        if (other.CompareTag(Constants.Tags.PLAYER))
        {
            var networkObject = other.GetComponentInParent<NetworkObject>();
            if (networkObject != null)
            {
                ulong clientId = networkObject.OwnerClientId;
                if (_playersInside.Contains(clientId))
                {
                    _playersInside.Remove(clientId);
                    Debug.Log($"[CoopSceneTeleporter] Player {clientId} đã rời vùng. Còn lại trong vùng: {_playersInside.Count}");
                }
            }
        }
    }

    private void CheckTransition()
    {
        if (_isTransitioning) return;

        if (NetworkManager.Singleton == null || !IsServer)
        {
            Debug.LogWarning("[CoopSceneTeleporter] Ignoring transition check because the server is not ready.");
            return;
        }

        if (!SceneLoader.CanLoadScene(_nextSceneName))
        {
            Debug.LogError($"[CoopSceneTeleporter] Cannot transition because scene '{_nextSceneName}' is not in Build Settings.");
            return;
        }

        int connectedPlayers = NetworkManager.Singleton.ConnectedClients.Count;
        int requiredPlayers = Mathf.Min(connectedPlayers, 2);
        
        Debug.Log($"[CoopSceneTeleporter] Kiểm tra điều kiện: Trong vùng({_playersInside.Count}) >= Cần thiết({requiredPlayers})");

        if (_playersInside.Count >= requiredPlayers && requiredPlayers > 0)
        {
            _isTransitioning = true;
            StartCoroutine(TransitionCoroutine());
        }
    }

    private IEnumerator TransitionCoroutine()
    {
        Debug.Log($"<color=green>[CoopSceneTeleporter] ĐỦ ĐIỀU KIỆN! Khởi động hiệu ứng Loading...</color>");

        // 1. Kích hoạt hiệu ứng Fade In (Màn hình đen/Loading) trên toàn bộ Client
        if (LoadingSyncManager.Instance != null)
        {
            LoadingSyncManager.Instance.StartLoadingFadeClientRpc();
        }
        else if (SeamlessLoadingOverlay.Instance != null)
        {
            // Keep the presentation visible even if the network sync helper is not
            // present in a standalone/test scene.
            SeamlessLoadingOverlay.Instance.EnsureLoadingVisible();
        }

        // 2. Đợi một chút để màn hình kịp mờ hẳn (giống bên LobbyManager)
        yield return new WaitForSecondsRealtime(0.8f);

        Debug.Log($"<color=green>[CoopSceneTeleporter] Bắt đầu load scene: {_nextSceneName}</color>");

        // 3. Thực hiện load scene thông qua SceneLoader
        var loader = FindFirstObjectByType<SceneLoader>();
        if (loader != null)
        {
            if (loader.IsLoading)
            {
                Debug.LogWarning("[CoopSceneTeleporter] SceneLoader is already processing a transition.");
                yield break;
            }
            loader.LoadScene(_nextSceneName);
        }
        else
        {
            Debug.LogWarning("[CoopSceneTeleporter] Không thấy SceneLoader, gọi trực tiếp NetworkSceneManager.");
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null && SceneLoader.CanLoadScene(_nextSceneName))
                NetworkManager.SceneManager.LoadScene(_nextSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

}

