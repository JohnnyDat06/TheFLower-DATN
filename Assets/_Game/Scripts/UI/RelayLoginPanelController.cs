using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Relay login overlay used when Map4_Flying is launched directly.
/// The host can play while the room code remains visible; the client enters the
/// code and the overlay closes after NGO finishes the initial synchronization.
/// </summary>
public sealed class RelayLoginPanelController : MonoBehaviour
{
    [Header("Services")]
    [SerializeField] private AuthManager _authManager;
    [SerializeField] private RelayManager _relayManager;

    [Header("Login UI")]
    [SerializeField] private GameObject _loginPanel;
    [SerializeField] private Graphic _panelBackground;
    [SerializeField] private Button _hostButton;
    [SerializeField] private Button _clientButton;
    [SerializeField] private TMP_InputField _joinCodeInput;
    [SerializeField] private TMP_Text _joinCodeDisplay;
    [SerializeField] private TMP_Text _statusLog;

    private bool _isBusy;
    private bool _callbacksRegistered;

    private void Awake()
    {
        _loginPanel ??= gameObject;
        _panelBackground ??= GetComponent<Graphic>();

        _hostButton.onClick.AddListener(OnHostClicked);
        _clientButton.onClick.AddListener(OnJoinClicked);
    }

    private void OnEnable()
    {
        UICursorLockService.Request(this);
        CameraManager.Instance?.SetGameplayCameraLocked(true);

        if (_joinCodeInput != null)
        {
            _joinCodeInput.characterLimit = Constants.Gameplay.RELAY_JOINCODE_LENGTH;
            _joinCodeInput.text = _joinCodeInput.text.Trim().ToUpperInvariant();
        }

        SelectDefaultControl();
    }

    private void OnDisable()
    {
        UICursorLockService.Release(this);
        if (!UICursorLockService.IsCursorReleased)
            CameraManager.Instance?.SetGameplayCameraLocked(false);
    }

    private void OnDestroy()
    {
        UICursorLockService.Release(this);

        if (_hostButton != null)
            _hostButton.onClick.RemoveListener(OnHostClicked);
        if (_clientButton != null)
            _clientButton.onClick.RemoveListener(OnJoinClicked);

        UnregisterNetworkCallbacks();
    }

    public async void OnHostClicked()
    {
        if (!CanBeginConnection()) return;

        SetBusy(true, "Đang đăng nhập...");

        try
        {
            await _authManager.InitializeAsync();
            SetStatus("Đang tạo phòng Relay...");

            string joinCode = await _relayManager.CreateRelayAsync();
            if (string.IsNullOrWhiteSpace(joinCode))
                throw new InvalidOperationException("Relay không trả về mã phòng.");

            joinCode = joinCode.Trim().ToUpperInvariant();
            GUIUtility.systemCopyBuffer = joinCode;

            if (_joinCodeDisplay != null)
                _joinCodeDisplay.text = $"MÃ PHÒNG: {joinCode}";

            Debug.Log($"[RelayLoginPanelController] Host đã sẵn sàng. Join code: {joinCode}");
            RegisterNetworkCallbacks();
            EnterHostGameplayMode();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[RelayLoginPanelController] Không thể tạo phòng: {exception.Message}");
            SetBusy(false, $"Tạo phòng thất bại: {exception.Message}");
        }
    }

    public async void OnJoinClicked()
    {
        if (!CanBeginConnection()) return;

        string joinCode = _joinCodeInput.text.Trim().ToUpperInvariant();
        if (joinCode.Length != Constants.Gameplay.RELAY_JOINCODE_LENGTH)
        {
            SetStatus($"Mã phòng phải có {Constants.Gameplay.RELAY_JOINCODE_LENGTH} ký tự.");
            _joinCodeInput.Select();
            return;
        }

        _joinCodeInput.text = joinCode;
        SetBusy(true, "Đang đăng nhập...");
        RegisterNetworkCallbacks();

        try
        {
            await _authManager.InitializeAsync();
            SetStatus("Đang kết nối tới host...");
            await _relayManager.JoinRelayAsync(joinCode);
            SetStatus("Đã kết nối. Đang đồng bộ bản đồ...");
        }
        catch (Exception exception)
        {
            UnregisterNetworkCallbacks();
            Debug.LogError($"[RelayLoginPanelController] Không thể vào phòng: {exception.Message}");
            SetBusy(false, $"Vào phòng thất bại: {exception.Message}");
        }
    }

    private bool CanBeginConnection()
    {
        if (_isBusy) return false;

        if (_authManager == null || _relayManager == null)
        {
            SetStatus("Thiếu AuthManager hoặc RelayManager.");
            Debug.LogError("[RelayLoginPanelController] AuthManager/RelayManager reference is missing.");
            return false;
        }

        if (NetworkManager.Singleton == null)
        {
            SetStatus("Không tìm thấy NetworkManager.");
            Debug.LogError("[RelayLoginPanelController] NetworkManager is missing.");
            return false;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            SetStatus("Network đã được khởi động.");
            return false;
        }

        return true;
    }

    private void RegisterNetworkCallbacks()
    {
        if (_callbacksRegistered || NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        _callbacksRegistered = true;
    }

    private void UnregisterNetworkCallbacks()
    {
        if (!_callbacksRegistered) return;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        _callbacksRegistered = false;
    }

    private void HandleClientConnected(ulong clientId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null) return;

        bool localClientFinishedSync = manager.IsClient && clientId == manager.LocalClientId;
        bool remoteClientJoinedHost = manager.IsHost && clientId != manager.LocalClientId;
        if (!localClientFinishedSync && !remoteClientJoinedHost) return;

        UnregisterNetworkCallbacks();
        HideLoginPanel();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || clientId != manager.LocalClientId || manager.IsHost) return;

        UnregisterNetworkCallbacks();
        SetBusy(false, "Mất kết nối tới host. Kiểm tra lại mã phòng.");
    }

    private void EnterHostGameplayMode()
    {
        _isBusy = false;

        if (_panelBackground != null)
        {
            _panelBackground.raycastTarget = false;
            _panelBackground.enabled = false;
        }

        _hostButton.gameObject.SetActive(false);
        _clientButton.gameObject.SetActive(false);
        _joinCodeInput.gameObject.SetActive(false);
        if (_statusLog != null)
            _statusLog.gameObject.SetActive(false);

        if (_joinCodeDisplay != null)
        {
            _joinCodeDisplay.gameObject.SetActive(true);
            _joinCodeDisplay.raycastTarget = false;
        }

        ReleaseUiFocus();
    }

    private void HideLoginPanel()
    {
        _isBusy = false;
        ReleaseUiFocus();
        if (_loginPanel != null)
            _loginPanel.SetActive(false);
    }

    private void SetBusy(bool busy, string status)
    {
        _isBusy = busy;
        _hostButton.interactable = !busy;
        _clientButton.interactable = !busy;
        _joinCodeInput.interactable = !busy;
        SetStatus(status);
    }

    private void SetStatus(string status)
    {
        if (_statusLog != null)
            _statusLog.text = status;
    }

    private void SelectDefaultControl()
    {
        if (EventSystem.current == null) return;

        GameObject selection = _joinCodeInput != null && !string.IsNullOrEmpty(_joinCodeInput.text)
            ? _clientButton.gameObject
            : _hostButton.gameObject;
        EventSystem.current.SetSelectedGameObject(selection);
    }

    private void ReleaseUiFocus()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        UICursorLockService.Release(this);
        if (!UICursorLockService.IsCursorReleased)
            CameraManager.Instance?.SetGameplayCameraLocked(false);
    }
}
