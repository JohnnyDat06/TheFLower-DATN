using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI.Lobby
{
    public class JoinRoomPanel : UIPanel
    {
        [Header("Managers")]
        [SerializeField] private AuthManager _authManager;
        [SerializeField] private RelayManager _relayManager;

        [Header("UI Elements")]
        [SerializeField] private TMP_InputField _joinCodeInput;
        [SerializeField] private Button _joinRelayButton;
        [SerializeField] private Button _joinDirectButton;
        [SerializeField] private Button _backButton;
        [SerializeField] private TMP_Text _statusLog;

        protected override void Awake()
        {
            base.Awake();
            _joinRelayButton.onClick.AddListener(OnJoinRelayClicked);
            _joinDirectButton.onClick.AddListener(OnJoinDirectClicked);
            _backButton.onClick.AddListener(OnBackClicked);
        }

        private async void OnJoinRelayClicked()
        {
            if (string.IsNullOrEmpty(_joinCodeInput.text))
            {
                if (_statusLog) _statusLog.text = "Please enter a valid join code.";
                return;
            }

            _joinRelayButton.interactable = false;
            if (_statusLog) _statusLog.text = "Initializing Auth...";
            await _authManager.InitializeAsync();
            
            if (_statusLog) _statusLog.text = "Joining Relay Room...";
            try
            {
                await _relayManager.JoinRelayAsync(_joinCodeInput.text);
                LobbyUIManager.Instance.ShowLobbyRoom();
            }
            catch (System.Exception e)
            {
                if (_statusLog) _statusLog.text = "Failed to join room.";
                Debug.LogError($"[JoinRoomPanel] Join failed: {e.Message}");
                _joinRelayButton.interactable = true;
            }
        }

        private void OnJoinDirectClicked()
        {
            if (_statusLog) _statusLog.text = "Joining Direct Host (Local)...";
            var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            string ip = string.IsNullOrEmpty(_joinCodeInput.text) ? "127.0.0.1" : _joinCodeInput.text;
            utp.SetConnectionData(ip, 7777);
            NetworkManager.Singleton.StartClient();

            LobbyUIManager.Instance.ShowLobbyRoom();
        }

        private void OnBackClicked()
        {
            LobbyUIManager.Instance.ShowMainMenu();
        }
    }
}
