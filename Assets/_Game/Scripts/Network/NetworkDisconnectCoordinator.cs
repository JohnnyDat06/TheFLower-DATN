using System;
using System.Threading.Tasks;
using Networking.LobbySystem;
using Game.UI.LobbyAuto;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Ends a two-player session cleanly when either peer disappears.
/// </summary>
public sealed class NetworkDisconnectCoordinator : MonoBehaviour
{
    private const int DisconnectMessageMilliseconds = 2200;
    private const int LobbyCleanupTimeoutMilliseconds = 3000;

    private static NetworkDisconnectCoordinator _instance;
    private static bool _localExitRequested;

    private NetworkManager _networkManager;
    private bool _handlingDisconnect;
    private bool _sessionEstablished;
    private bool _wasHost;
    private bool _wasClient;
    private ulong _localClientId;
    private GameObject _notificationRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (_instance != null || FindFirstObjectByType<NetworkDisconnectCoordinator>() != null)
            return;

        new GameObject(nameof(NetworkDisconnectCoordinator))
            .AddComponent<NetworkDisconnectCoordinator>();
    }

    /// <summary>Prevents an intentional local shutdown from showing a false error.</summary>
    public static void PrepareForLocalExit()
    {
        _localExitRequested = true;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        UnbindNetworkManager();
        if (_instance == this)
            _instance = null;
    }

    private void Update()
    {
        BindNetworkManager();

        if (_networkManager == null || !_networkManager.IsListening)
            return;

        _wasHost = _networkManager.IsHost;
        _wasClient = _networkManager.IsClient;
        _localClientId = _networkManager.LocalClientId;

        if (_networkManager.IsConnectedClient)
            _sessionEstablished = true;

        if (_networkManager.IsHost && _networkManager.ConnectedClientsIds.Count > 1)
            _sessionEstablished = true;
    }

    private void BindNetworkManager()
    {
        NetworkManager current = NetworkManager.Singleton;
        if (_networkManager == current)
            return;

        UnbindNetworkManager();
        _networkManager = current;
        if (_networkManager == null)
            return;

        _networkManager.OnClientConnectedCallback += HandleClientConnected;
        _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        _networkManager.OnTransportFailure += HandleTransportFailure;
    }

    private void UnbindNetworkManager()
    {
        if (_networkManager == null)
            return;

        _networkManager.OnClientConnectedCallback -= HandleClientConnected;
        _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        _networkManager.OnTransportFailure -= HandleTransportFailure;
        _networkManager = null;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (_networkManager == null)
            return;

        if (clientId == _networkManager.LocalClientId)
        {
            _localClientId = clientId;
            _wasHost = _networkManager.IsHost;
            _wasClient = _networkManager.IsClient;
            _localExitRequested = false;
            _handlingDisconnect = false;
        }

        if (_networkManager.IsHost && _networkManager.ConnectedClientsIds.Count > 1)
            _sessionEstablished = true;
        else if (!_networkManager.IsHost && clientId == _networkManager.LocalClientId)
            _sessionEstablished = true;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (_handlingDisconnect || _localExitRequested || !_sessionEstablished)
            return;

        // The host owns the server. A remote client leaving must not tear down the host session.
        // Lobby polling/UI will update the vacant player slot independently.
        if (_wasHost)
            return;

        if (_wasClient && clientId == _localClientId)
        {
            BeginReturnToLobby(
                "HOST DISCONNECTED",
                "The host left the session. Returning to the lobby...");
        }
    }

    private void HandleTransportFailure()
    {
        if (_handlingDisconnect || _localExitRequested || !_sessionEstablished)
            return;

        BeginReturnToLobby(
            "CONNECTION LOST",
            "The multiplayer connection was interrupted. Returning to the lobby...");
    }

    private async void BeginReturnToLobby(string title, string message)
    {
        if (_handlingDisconnect)
            return;

        _handlingDisconnect = true;
        bool gameplayScene = !IsLobbyScene();
        Debug.LogWarning($"[NetworkDisconnectCoordinator] {title}: {message}");

        await CleanupSession();

        if (!gameplayScene)
        {
            LobbyAutoController lobbyUi = FindFirstObjectByType<LobbyAutoController>();
            if (lobbyUi != null)
            {
                lobbyUi.ReturnToDisconnectedLanding();
                _sessionEstablished = false;
                _wasHost = false;
                _wasClient = false;
                _handlingDisconnect = false;
                return;
            }
        }

        if (_notificationRoot != null)
        {
            _notificationRoot.SetActive(false);
            Destroy(_notificationRoot);
            _notificationRoot = null;
        }

        string lobbyScene = Constants.Scenes.MAIN_MENU;
        if (SceneLoader.CanLoadScene(lobbyScene))
            SceneManager.LoadScene(lobbyScene);
        else
            Debug.LogError($"[NetworkDisconnectCoordinator] Lobby scene '{lobbyScene}' is not enabled in Build Settings.");

        _sessionEstablished = false;
        _wasHost = false;
        _wasClient = false;
        _handlingDisconnect = false;
    }

    private async Task CleanupSession()
    {
        try
        {
            if (LobbyManager.Instance != null)
            {
                Task leaveTask = LobbyManager.Instance.LeaveLobby();
                await Task.WhenAny(leaveTask, Task.Delay(LobbyCleanupTimeoutMilliseconds));
                LobbyManager.Instance.ResetAfterDisconnect();
            }
            else if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NetworkDisconnectCoordinator] Session cleanup failed: {exception.Message}");
            try
            {
                if (_networkManager != null && _networkManager.IsListening)
                    _networkManager.Shutdown();
            }
            catch
            {
                // Scene reload remains the final recovery path.
            }
        }
    }

    private static bool IsLobbyScene()
    {
        return SceneManager.GetActiveScene().name.Contains(
            "Lobby",
            StringComparison.OrdinalIgnoreCase);
    }

    private void ShowNotification(string title, string message)
    {
        if (_notificationRoot != null)
            return;

        _notificationRoot = new GameObject(
            "NetworkDisconnectNotification",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        Canvas canvas = _notificationRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;

        CanvasScaler scaler = _notificationRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Image dimmer = CreateImage(
            "Dimmer",
            _notificationRoot.transform,
            new Color(0.01f, 0.025f, 0.03f, 0.82f));
        Stretch(dimmer.rectTransform);

        Image card = CreateImage(
            "MessageCard",
            dimmer.transform,
            new Color(0.035f, 0.14f, 0.16f, 0.98f));
        RectTransform cardRect = card.rectTransform;
        cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = Vector2.zero;
        cardRect.sizeDelta = new Vector2(760f, 280f);

        TextMeshProUGUI titleText = CreateText(
            "Title",
            card.transform,
            title,
            42f,
            FontStyles.Bold,
            new Color(1f, 0.76f, 0.25f, 1f));
        RectTransform titleRect = titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 0.5f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.offsetMin = new Vector2(50f, 0f);
        titleRect.offsetMax = new Vector2(-50f, -30f);

        TextMeshProUGUI messageText = CreateText(
            "Message",
            card.transform,
            message,
            26f,
            FontStyles.Normal,
            new Color(0.9f, 0.97f, 0.96f, 1f));
        RectTransform messageRect = messageText.rectTransform;
        messageRect.anchorMin = new Vector2(0f, 0f);
        messageRect.anchorMax = new Vector2(1f, 0.55f);
        messageRect.offsetMin = new Vector2(60f, 35f);
        messageRect.offsetMax = new Vector2(-60f, 0f);
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject item = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        item.transform.SetParent(parent, false);
        Image image = item.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        string value,
        float fontSize,
        FontStyles style,
        Color color)
    {
        GameObject item = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        item.transform.SetParent(parent, false);

        TextMeshProUGUI text = item.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}