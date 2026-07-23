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

        StartCoroutine(LoadSceneCoroutine(sceneName));
    }

    /// <summary>
    /// Client gọi cái này khi nhận lệnh từ LoadingSyncManager.
    /// </summary>
    public void StartClientLoadingSimulation()
    {
        if (NetworkManager.Singleton.IsHost) return;
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

    private string _loadingSceneName;
    private bool _allClientsReady;

    private IEnumerator LoadSceneCoroutine(string sceneName)
    {
        Debug.Log($"<color=yellow>[HOST] Loading scene: {sceneName}</color>");
        if (SeamlessLoadingOverlay.Instance != null) SeamlessLoadingOverlay.Instance.SetProgress(0f);

        _allClientsReady = false;
        _loadingSceneName = sceneName;

        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
            var status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[SceneLoader] Failed to start scene load: {status}");
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
                _allClientsReady = true; // Giải phóng để không treo game
            }
        }

        float fakeProgress = 0f;
        float timeout = 20f; // Timeout sau 20 giây nếu không load được
        while (!_allClientsReady && timeout > 0)
        {
            fakeProgress = Mathf.MoveTowards(fakeProgress, 0.9f, Time.deltaTime * 0.4f);
            if (SeamlessLoadingOverlay.Instance != null) SeamlessLoadingOverlay.Instance.SetProgress(fakeProgress);
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (timeout <= 0) Debug.LogWarning("[SceneLoader] Scene load timed out!");

        Debug.Log("<color=green>[HOST] Scene loaded. Waiting for PlayerSpawner to position everyone...</color>");

        if (_gameStateMachine != null) _gameStateMachine.TransitionTo(GameState.Playing);
    }

    private void HandleLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut)
    {
        if (sceneName == _loadingSceneName)
        {
            _allClientsReady = true;
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
