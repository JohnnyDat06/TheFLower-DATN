using System;
using System.Collections;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// SceneLoader — Quản lý nạp cảnh đồng bộ. Host điều khiển, Client lắng nghe.
/// </summary>
public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }
    [SerializeField] private GameStateMachine _gameStateMachine;

    private Coroutine _loadingRoutine;
    private bool _isLoading;
    private string _loadingSceneName;
    private bool _allClientsReady;
    private bool _loadFailed;

    public bool IsLoading => _isLoading;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        
        PersistentSceneRoot.MarkDontDestroyOnLoad(transform);
    }

    /// <summary>
    /// Chỉ Host gọi.
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (!CanLoadScene(sceneName)) return;

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogError($"[SceneLoader] Only the server can load network scene '{sceneName}'.");
            return;
        }

        if (_isLoading)
        {
            Debug.LogWarning($"[SceneLoader] Ignoring duplicate load request for '{sceneName}'.");
            return;
        }

        if (string.Equals(SceneManager.GetActiveScene().name, sceneName, StringComparison.Ordinal))
        {
            Debug.LogWarning($"[SceneLoader] Scene '{sceneName}' is already active.");
            return;
        }

        _loadingRoutine = StartCoroutine(LoadSceneCoroutine(sceneName));
    }

    /// <summary>
    /// Client gọi cái này khi nhận lệnh từ LoadingSyncManager.
    /// </summary>
    public void StartClientLoadingSimulation()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsHost) return;
        StartCoroutine(ClientProgressRoutine());
    }

    private IEnumerator ClientProgressRoutine()
    {
        float progress = 0;
        // Bò dần lên 90% trong khi đợi Server báo xong
        while (progress < 0.9f)
        {
            progress = Mathf.MoveTowards(progress, 0.9f, Time.deltaTime * 0.5f);
            if (SeamlessLoadingOverlay.Instance != null) SeamlessLoadingOverlay.Instance.SetProgress(progress);
            yield return null;
        }
    }

    private IEnumerator LoadSceneCoroutine(string sceneName)
    {
        _isLoading = true;
        Debug.Log($"<color=yellow>[HOST] Loading scene: {sceneName}</color>");
        if (SeamlessLoadingOverlay.Instance != null)
        {
            SeamlessLoadingOverlay.Instance.ShowToBeContinued(false);
            SeamlessLoadingOverlay.Instance.EnsureLoadingVisible(resetProgress: true);
        }

        _allClientsReady = false;
        _loadFailed = false;
        _loadingSceneName = sceneName;

        if (NetworkManager.Singleton.IsServer)
        {
            // Do not synchronously unload assets or force a GC here. Map2 is large,
            // and a blocking cleanup can pause NGO/Relay updates long enough for the
            // transport to report the connection as inactive.
            Debug.Log("[SceneLoader] Starting network scene load without blocking memory cleanup.");

            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
            var status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[SceneLoader] Failed to start scene load: {status}");
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
                yield return AbortLoading("Network scene load did not start.");
                yield break;
            }
        }

        float fakeProgress = 0f;
        // Map2 is large and can legitimately take longer than 20 seconds.
        float timeout = 90f;
        while (!_allClientsReady && timeout > 0)
        {
            fakeProgress = Mathf.MoveTowards(fakeProgress, 0.9f, Time.deltaTime * 0.4f);
            if (SeamlessLoadingOverlay.Instance != null) SeamlessLoadingOverlay.Instance.SetProgress(fakeProgress);
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (timeout <= 0)
        {
            yield return AbortLoading($"Scene '{sceneName}' timed out before all clients completed loading.");
            yield break;
        }

        if (_loadFailed)
        {
            yield return AbortLoading($"Scene '{sceneName}' reported timed-out clients.");
            yield break;
        }

        Debug.Log("<color=green>[HOST] Scene loaded. Waiting for PlayerSpawner to position everyone...</color>");

        if (_gameStateMachine != null) _gameStateMachine.TransitionTo(GameState.Playing);
        _isLoading = false;
        _loadFailed = false;
        _loadingRoutine = null;
    }

    private IEnumerator AbortLoading(string reason)
    {
        Debug.LogError($"[SceneLoader] {reason} Player movement remains locked until the load can be retried.");
        if (NetworkManager.Singleton?.SceneManager != null)
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;

        _isLoading = false;
        _loadingRoutine = null;
        if (SeamlessLoadingOverlay.Instance != null)
        {
            SeamlessLoadingOverlay.Instance.EnsureLoadingVisible();
        }

        yield return null;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton?.SceneManager != null)
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
    }

    private void HandleLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut)
    {
        if (sceneName == _loadingSceneName)
        {
            if (clientsTimedOut != null && clientsTimedOut.Count > 0)
            {
                _loadFailed = true;
                _allClientsReady = true;
                Debug.LogError($"[SceneLoader] Scene '{sceneName}' timed out for {clientsTimedOut.Count} client(s); aborting before gameplay is released.");
            }
            else
            {
                _allClientsReady = true;
            }
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            }
        }
    }

    public void LoadMainMenu()
    {
        NetworkDisconnectCoordinator.PrepareForLocalExit();
        try { if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown(); } catch { }

        if (!CanLoadScene(Constants.Scenes.MAIN_MENU)) return;

        SceneManager.LoadScene(Constants.Scenes.MAIN_MENU);
    }

    public static bool CanLoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[SceneLoader] Cannot load scene because the scene name is empty.");
            return false;
        }

        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string buildSceneName = Path.GetFileNameWithoutExtension(scenePath);

            if (string.Equals(buildSceneName, sceneName, StringComparison.Ordinal))
                return true;
        }

        Debug.LogError($"[SceneLoader] Cannot load scene '{sceneName}' because it is not enabled in Build Settings.");
        return false;
    }
}
