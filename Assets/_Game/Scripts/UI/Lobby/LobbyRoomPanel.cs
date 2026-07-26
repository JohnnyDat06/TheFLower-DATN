using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI.Lobby
{
    public class LobbyRoomPanel : UIPanel
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text _roomCodeText;
        [SerializeField] private Transform _playerListContainer;
        [SerializeField] private LobbyPlayerEntry _playerEntryPrefab;
        [SerializeField] private Button _startGameButton;
        [SerializeField] private Button _leaveButton;

        [Header("Scene Settings")]
        [Tooltip("Name of the scene to load when Start Game is clicked.")]
        [SerializeField] private string _gameSceneName = "Level04"; // Defaulting to one of your folders, change in Editor

        private Dictionary<ulong, LobbyPlayerEntry> _playerEntries = new Dictionary<ulong, LobbyPlayerEntry>();

        protected override void Awake()
        {
            base.Awake();
            _startGameButton.onClick.AddListener(OnStartGameClicked);
            _leaveButton.onClick.AddListener(OnLeaveClicked);
        }

        private void OnEnable()
        {
            EventBus.OnClientConnected += HandleClientConnected;
            EventBus.OnClientDisconnected += HandleClientDisconnected;
        }

        private void OnDisable()
        {
            EventBus.OnClientConnected -= HandleClientConnected;
            EventBus.OnClientDisconnected -= HandleClientDisconnected;
        }

        public override void Show()
        {
            base.Show();
            RefreshPlayerList();

            // Only the Host can start the game
            _startGameButton.gameObject.SetActive(NetworkManager.Singleton.IsHost);
        }

        private void HandleClientConnected(ulong clientId)
        {
            RefreshPlayerList();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            RefreshPlayerList();
        }

        private void RefreshPlayerList()
        {
            if (!NetworkManager.Singleton.IsListening) return;

            // Clear existing
            foreach (var entry in _playerEntries.Values)
            {
                if (entry != null) Destroy(entry.gameObject);
            }
            _playerEntries.Clear();

            // Create new list based on connected clients
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                var newEntry = Instantiate(_playerEntryPrefab, _playerListContainer);
                bool isHost = (clientId == NetworkManager.ServerClientId);
                newEntry.Initialize(clientId, isHost);
                _playerEntries.Add(clientId, newEntry);
            }
        }

        private void OnStartGameClicked()
        {
            if (NetworkManager.Singleton.IsHost)
            {
                // Call the SceneLoader to seamlessly transition everyone
                if (SceneLoader.Instance != null)
                {
                    SceneLoader.Instance.LoadScene(_gameSceneName);
                }
                else
                {
                    Debug.LogError("[LobbyRoomPanel] SceneLoader Instance is missing!");
                }
            }
        }

        private void OnLeaveClicked()
        {
            NetworkDisconnectCoordinator.PrepareForLocalExit();
            NetworkManager.Singleton.Shutdown();
            LobbyUIManager.Instance.ShowMainMenu();
        }
    }
}
