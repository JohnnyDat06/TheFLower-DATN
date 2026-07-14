using UnityEngine;

namespace Game.UI.Lobby
{
    public class LobbyUIManager : MonoBehaviour
    {
        public static LobbyUIManager Instance { get; private set; }

        [Header("Panels")]
        [SerializeField] private MainMenuPanel _mainMenuPanel;
        [SerializeField] private JoinRoomPanel _joinRoomPanel;
        [SerializeField] private LobbyRoomPanel _lobbyRoomPanel;

        private UIPanel _currentPanel;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            // Hide all initially
            if (_mainMenuPanel) _mainMenuPanel.HideInstant();
            if (_joinRoomPanel) _joinRoomPanel.HideInstant();
            if (_lobbyRoomPanel) _lobbyRoomPanel.HideInstant();
        }

        private void Start()
        {
            // Start with Main Menu
            ShowPanel(_mainMenuPanel);
        }

        public void ShowMainMenu() => ShowPanel(_mainMenuPanel);
        public void ShowJoinRoom() => ShowPanel(_joinRoomPanel);
        public void ShowLobbyRoom() => ShowPanel(_lobbyRoomPanel);

        private void ShowPanel(UIPanel newPanel)
        {
            if (newPanel == null || newPanel == _currentPanel) return;

            if (_currentPanel != null)
            {
                _currentPanel.Hide();
            }

            _currentPanel = newPanel;
            _currentPanel.Show();
        }
    }
}
