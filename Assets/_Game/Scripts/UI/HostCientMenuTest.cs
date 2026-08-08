using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HostCientMenuTest : MonoBehaviour
{
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private GameObject menu;

    private bool _ownsCursor;


    private void Awake()
    {
        hostButton?.onClick.AddListener(OnHostClicked);
        clientButton?.onClick.AddListener(OnClientClicked);
    }

    private void OnEnable()
    {
        SyncCursorOwnership();
        SelectDefaultButton();
    }

    private void Update()
    {
        // This component lives on the persistent test root while the actual menu is a
        // child object. Toggling that child does not invoke OnEnable/OnDisable here.
        SyncCursorOwnership();
    }

    private void OnDisable()
    {
        ReleaseCursor();
    }

    private void OnDestroy()
    {
        hostButton?.onClick.RemoveListener(OnHostClicked);
        clientButton?.onClick.RemoveListener(OnClientClicked);
        ReleaseCursor();
    }

    private void Start()
    {
        SelectDefaultButton();
    }

    private const string PREF_KEY_PORT = "TestHostPort";

    private void OnHostClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[HostCientMenuTest] NetworkManager.Singleton is missing from the scene!");
            return;
        }

        // Clean up previous session if NetworkManager is still running
        if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.Shutdown();
        }

        if (NetworkManager.Singleton.TryGetComponent<Unity.Netcode.Transports.UTP.UnityTransport>(out var defaultTransport))
        {
            defaultTransport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
        }

        bool success = NetworkManager.Singleton.StartHost();
        if (success)
        {
            PlayerPrefs.SetInt(PREF_KEY_PORT, 7777);
            PlayerPrefs.Save();
            Debug.Log("[HostCientMenuTest] Host started successfully on PORT 7777");
            Hide();
        }
        else
        {
            Debug.LogWarning("[HostCientMenuTest] StartHost failed on default port 7777. Retrying with fallback port 7778...");
            if (NetworkManager.Singleton.TryGetComponent<Unity.Netcode.Transports.UTP.UnityTransport>(out var transport))
            {
                ushort fallbackPort = 7778;
                transport.SetConnectionData("127.0.0.1", fallbackPort, "0.0.0.0");
                if (NetworkManager.Singleton.StartHost())
                {
                    PlayerPrefs.SetInt(PREF_KEY_PORT, fallbackPort);
                    PlayerPrefs.Save();
                    Debug.Log($"[HostCientMenuTest] Successfully started Host on fallback port {fallbackPort}!");
                    Hide();
                    return;
                }
            }
            Debug.LogError("[HostCientMenuTest] StartHost failed! UDP Port 7777/7778 is locked by another running process.");
        }
    }

    private void OnClientClicked()
    {
        if (NetworkManager.Singleton == null) return;
        
        if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.Shutdown();
        }

        ushort targetPort = (ushort)PlayerPrefs.GetInt(PREF_KEY_PORT, 7777);
        Debug.Log($"[HostCientMenuTest] Connecting as Client to 127.0.0.1:{targetPort}...");

        if (NetworkManager.Singleton.TryGetComponent<Unity.Netcode.Transports.UTP.UnityTransport>(out var transport))
        {
            transport.SetConnectionData("127.0.0.1", targetPort);
        }

        bool success = NetworkManager.Singleton.StartClient();
        if (success)
        {
            Hide();
        }
        else
        {
            ushort altPort = (targetPort == 7777) ? (ushort)7778 : (ushort)7777;
            Debug.LogWarning($"[HostCientMenuTest] StartClient failed on port {targetPort}. Retrying on port {altPort}...");
            if (NetworkManager.Singleton.TryGetComponent<Unity.Netcode.Transports.UTP.UnityTransport>(out var transportFallback))
            {
                transportFallback.SetConnectionData("127.0.0.1", altPort);
                if (NetworkManager.Singleton.StartClient())
                {
                    Hide();
                    return;
                }
            }
            Debug.LogError("[HostCientMenuTest] Failed to connect as Client to 127.0.0.1:7777 or 7778!");
        }
    }

    private void Hide()
    {
        if (menu != null) menu.SetActive(false);
        ReleaseCursor();
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void SyncCursorOwnership()
    {
        bool shouldOwnCursor = menu != null && menu.activeInHierarchy;
        if (shouldOwnCursor == _ownsCursor) return;

        _ownsCursor = shouldOwnCursor;
        if (_ownsCursor)
        {
            UICursorLockService.Request(this);
            CameraManager.Instance?.SetGameplayCameraLocked(true);
        }
        else
        {
            UICursorLockService.Release(this);
            if (!UICursorLockService.IsCursorReleased)
                CameraManager.Instance?.SetGameplayCameraLocked(false);
        }
    }

    private void ReleaseCursor()
    {
        _ownsCursor = false;
        UICursorLockService.Release(this);
        if (!UICursorLockService.IsCursorReleased)
            CameraManager.Instance?.SetGameplayCameraLocked(false);
    }

    private void SelectDefaultButton()
    {
        if (menu != null && !menu.activeInHierarchy) return;
        if (EventSystem.current == null || hostButton == null || !hostButton.interactable) return;

        EventSystem.current.SetSelectedGameObject(hostButton.gameObject);
    }
}
