using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI.Lobby
{
    public class MainMenuPanel : UIPanel
    {
        [Header("Managers")]
        [SerializeField] private AuthManager _authManager;
        [SerializeField] private RelayManager _relayManager;
        
        [Header("UI Elements")]
        [SerializeField] private Button _hostRelayButton;
        [SerializeField] private Button _joinRelayMenuButton;
        [SerializeField] private Button _hostDirectButton;
        [SerializeField] private TMP_Text _statusLog;

        protected override void Awake()
        {
            base.Awake();
            _hostRelayButton.onClick.AddListener(OnHostRelayClicked);
            _joinRelayMenuButton.onClick.AddListener(OnJoinRelayMenuClicked);
            _hostDirectButton.onClick.AddListener(OnHostDirectClicked);
        }

        private async void OnHostRelayClicked()
        {
            _hostRelayButton.interactable = false;
            if (_statusLog) _statusLog.text = "Initializing Auth...";
            
            await _authManager.InitializeAsync();
            
            if (_statusLog) _statusLog.text = "Creating Relay Room...";
            var code = await _relayManager.CreateRelayAsync();
            
            if (!string.IsNullOrEmpty(code))
            {
                // Move to Lobby Room and pass the join code
                LobbyUIManager.Instance.ShowLobbyRoom();
                // We'll let LobbyRoomPanel fetch the code from RelayManager or pass it via Event/Property
            }
            else
            {
                if (_statusLog) _statusLog.text = "Failed to create room.";
                _hostRelayButton.interactable = true;
            }
        }

        private void OnJoinRelayMenuClicked()
        {
            LobbyUIManager.Instance.ShowJoinRoom();
        }

        private void OnHostDirectClicked()
        {
            if (_statusLog) _statusLog.text = "Starting Direct Host (Local)...";
            var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            utp.SetConnectionData("127.0.0.1", 7777);
            NetworkManager.Singleton.StartHost();
            
            LobbyUIManager.Instance.ShowLobbyRoom();
        }
    }
}
