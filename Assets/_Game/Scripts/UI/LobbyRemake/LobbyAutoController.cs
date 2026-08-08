using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Networking.LobbySystem;
using TMPro;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UIDocument = UnityEngine.UIElements.UIDocument;
using LobbyModel = Unity.Services.Lobbies.Models.Lobby;
using LobbyPlayerModel = Unity.Services.Lobbies.Models.Player;

namespace Game.UI.LobbyAuto
{
    /// <summary>Runtime-built, two-player UGS Lobby front end used by the production Lobby scene.</summary>
    public sealed class LobbyAutoController : MonoBehaviour
    {
        private static readonly Color Navy = new(0.035f, 0.20f, 0.22f, 0.78f);
        private static readonly Color Panel = new(0.055f, 0.25f, 0.25f, 0.86f);
        private static readonly Color PanelSoft = new(0.16f, 0.48f, 0.48f, 0.90f);
        private static readonly Color Gold = new(1f, 0.70f, 0.22f, 1f);
        private static readonly Color Teal = new(0.10f, 0.78f, 0.60f, 1f);
        private static readonly Color Red = new(0.86f, 0.25f, 0.25f, 1f);
        private static readonly Color Green = new(0.18f, 0.72f, 0.38f, 1f);
        private static readonly Color Paper = new(0.95f, 0.95f, 0.89f, 1f);
        private static readonly Color Muted = new(0.60f, 0.70f, 0.73f, 1f);
        private const string GamepadFocusVisiblePref = "UI.GamepadFocusVisible";

        [SerializeField] private string _gameSceneName = Constants.Scenes.LEVEL_01;

        private readonly List<Button> _buttons = new();
        private readonly Dictionary<GameObject, Selectable> _panelDefaultSelections = new();
        private readonly List<(Button Button, char Character)> _virtualCharacterKeys = new();
        private readonly List<List<Button>> _virtualKeyboardRows = new();
        private readonly List<Button> _roomBrowserButtons = new();
        private readonly List<Slider> _settingsSliders = new();
        private LobbyManager _lobbyManager;
        private LobbyModel _currentLobby;
        private LobbyModel _selectedLobby;
        private LobbyRuntimeConfig _config;
        private CanvasGroup _shellGroup;
        private AudioSource _musicSource;
        private InputSettingsPanelController _inputSettings;

        private GameObject _landingPanel;
        private GameObject _modePanel;
        private GameObject _createPanel;
        private GameObject _joinPanel;
        private GameObject _roomPanel;
        private GameObject _settingsPanel;
        private GameObject _activePanel;
        private GameObject _virtualKeyboardPanel;
        private GameObject _activeGamepadFocusFrame;
        private GameObject _gamepadFocusOwner;
        private CanvasGroup _activeGamepadFocusGroup;

        private TMP_InputField _createPlayerName;
        private TMP_InputField _createRoomName;
        private TMP_InputField _createPassword;
        private TMP_InputField _joinPlayerName;
        private TMP_InputField _joinRoomName;
        private TMP_InputField _joinPassword;
        private TMP_InputField _roomPasswordPrompt;
        private TMP_Text _passwordPromptTitle;
        private TMP_Text _statusText;
        private TMP_Text _roomTitleText;
        private TMP_Text _roomCodeText;
        private TMP_Text _readyButtonText;
        private TMP_Text _startButtonText;
        private TMP_Text _virtualKeyboardTitle;
        private TMP_Text _virtualKeyboardPreview;
        private RectTransform _browserList;
        private GameObject _passwordPromptPanel;
        private Button _readyButton;
        private Button _startButton;
        private Button _createSubmitButton;
        private Button _createCancelButton;
        private Button _joinSubmitButton;
        private Button _joinRefreshButton;
        private Button _joinBackButton;
        private Button _passwordConfirmButton;
        private Button _passwordCancelButton;
        private Button _focusVisibilityButton;
        private TMP_Text _focusVisibilityButtonText;
        private Image[] _cardBorders = new Image[2];
        private TMP_Text[] _cardNames = new TMP_Text[2];
        private TMP_Text[] _cardRoles = new TMP_Text[2];
        private TMP_Text[] _cardStates = new TMP_Text[2];
        private Image[] _cardAvatars = new Image[2];

        private bool _busy;
        private bool _lifecycleEventsSubscribed;
        private bool _localReady;
        private float _refreshTimer;
        private string _lastRosterSignature;
        private InputAction _cancelAction;
        private InputAction _submitAction;
        private InputAction _navigateAction;
        private Coroutine _selectionCoroutine;
        private Coroutine _virtualKeyboardOpenCoroutine;
        private TMP_InputField _virtualKeyboardTarget;
        private Button _virtualKeyboardFirstKey;
        private bool _virtualKeyboardUppercase = true;
        private bool _usingGamepad;
        private bool _lastNavigationWasGamepad;
        private bool _showGamepadFocusFrames;
        private Selectable _selectionBeforeBusy;
        private Coroutine _restoreBusySelectionCoroutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallInLobbyScenes()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            InstallInScene(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => InstallInScene(scene);

        private static void InstallInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || !scene.name.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
                return;

            foreach (LobbyUI legacyController in UnityEngine.Object.FindObjectsByType<LobbyUI>(FindObjectsSortMode.None))
                legacyController.enabled = false;

            if (UnityEngine.Object.FindFirstObjectByType<LobbyAutoController>() != null) return;

            GameObject root = new("LobbyRemake_Interface");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<LobbyAutoController>();
        }

        private void Awake()
        {
            _config = Resources.Load<LobbyRuntimeConfig>("UI/LobbyRuntimeConfig");
            _usingGamepad = Gamepad.current != null;
            _lastNavigationWasGamepad = _usingGamepad;
            _showGamepadFocusFrames = PlayerPrefs.GetInt(GamepadFocusVisiblePref, 1) != 0;
            GameObject staticPreview = GameObject.Find("LobbyBackground_StaticPreview");
            if (staticPreview != null) staticPreview.SetActive(false);
            BuildInterface();
            StartLobbyMusic();
            ShowLanding();
            StartCoroutine(FadeInInterface());
        }

        private void Start()
        {
            BindLobbyManager();
            BindLobbyInput();
            FocusActivePanel();
            StartCoroutine(EnsureInitialGamepadFocus());
        }

        private void OnEnable()
        {
            SubscribeLifecycleEvents();
            BindLobbyManager();
            BindLobbyInput();
        }

        private void OnDisable()
        {
            UnsubscribeLifecycleEvents();
            UnbindLobbyInput();
            UnbindLobbyManager();
        }

        private void SubscribeLifecycleEvents()
        {
            if (_lifecycleEventsSubscribed) return;

            EventBus.OnClientConnected += HandleNetworkChanged;
            EventBus.OnClientDisconnected += HandleNetworkChanged;
            EventBus.OnSettingsChanged += ApplyAudioSettings;
            EventBus.OnInputDeviceChanged += HandleInputDeviceChanged;
            InputSystem.onDeviceChange += HandleInputSystemDeviceChange;
            _lifecycleEventsSubscribed = true;
        }

        private void UnsubscribeLifecycleEvents()
        {
            if (!_lifecycleEventsSubscribed) return;

            EventBus.OnClientConnected -= HandleNetworkChanged;
            EventBus.OnClientDisconnected -= HandleNetworkChanged;
            EventBus.OnSettingsChanged -= ApplyAudioSettings;
            EventBus.OnInputDeviceChanged -= HandleInputDeviceChanged;
            InputSystem.onDeviceChange -= HandleInputSystemDeviceChange;
            _lifecycleEventsSubscribed = false;
        }

        private void Update()
        {
            bool gamepadConnected = Gamepad.current != null;
            if (_usingGamepad != gamepadConnected)
            {
                _usingGamepad = gamepadConnected;
                if (_usingGamepad) FocusActivePanel(false);
                else HideActiveGamepadFocusFrame();
            }

            EnsureGamepadSelection();
            ReleaseGamepadInputFieldEditing();
            UpdateGamepadFocusRing();
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = 0.25f;
            BindLobbyManager();
            BindLobbyInput();
            RefreshRoomState();
        }

        private void BindLobbyManager()
        {
            if (_lobbyManager == LobbyManager.Instance) return;
            UnbindLobbyManager();
            _lobbyManager = LobbyManager.Instance;
            if (_lobbyManager == null) return;
            _lobbyManager.OnLobbyJoined += HandleLobbyJoined;
            _lobbyManager.OnLobbyUpdated += HandleLobbyUpdated;
            _lobbyManager.OnLobbyLeft += HandleLobbyLeft;
        }

        private void UnbindLobbyManager()
        {
            if (_lobbyManager == null) return;
            _lobbyManager.OnLobbyJoined -= HandleLobbyJoined;
            _lobbyManager.OnLobbyUpdated -= HandleLobbyUpdated;
            _lobbyManager.OnLobbyLeft -= HandleLobbyLeft;
            _lobbyManager = null;
        }

        private void ShowLanding()
        {
            ShowPanel(_landingPanel);
            SetStatus("Two players. One shared journey.", Paper);
            StyleColorfulCaption(_statusText);
        }

        private void ShowModeSelection()
        {
            ShowPanel(_modePanel);
            SetStatus("Choose how you want to play", Paper);
        }

        public void ReturnToDisconnectedLanding()
        {
            _currentLobby = null;
            _localReady = false;
            _busy = false;
            foreach (Button button in _buttons)
                if (button != null) button.interactable = true;
            ShowModeSelection();
            SetStatus("The host left. Create or join a new room.", Gold);
        }
        private void ShowCreate()
        {
            SyncSavedNames();
            SetPasswordVisibility(_createPassword, false);
            ShowPanel(_createPanel);
            SetStatus("Create a public room. Password is optional.", Paper);
        }

        private void ShowJoin()
        {
            SyncSavedNames();
            SetPasswordVisibility(_joinPassword, false);
            HidePasswordPrompt();
            ShowPanel(_joinPanel);
            SetStatus("Join by exact room name or choose an open room", Paper);
            RefreshRoomBrowser();
        }

        private void ShowSettings()
        {
            ShowPanel(_settingsPanel);
            SetStatus("Audio and controls are saved for the whole game", Paper);
        }

        private void ShowRoom()
        {
            ShowPanel(_roomPanel);
            _lastRosterSignature = null;
            RefreshRoomState();
        }

        private void ShowPanel(GameObject panel)
        {
            HideVirtualKeyboard(false);
            foreach (GameObject candidate in new[] { _landingPanel, _modePanel, _createPanel, _joinPanel, _roomPanel, _settingsPanel })
                if (candidate != null) candidate.SetActive(candidate == panel);
            _activePanel = panel;
            FocusActivePanel();
        }

        private void BindLobbyInput()
        {
            InputSystemUIInputModule module = EventSystem.current?.GetComponent<InputSystemUIInputModule>();
            InputAction cancelAction = module?.cancel?.action;
            InputAction submitAction = module?.submit?.action;
            InputAction navigateAction = module?.move?.action;
            if (_cancelAction == cancelAction && _submitAction == submitAction && _navigateAction == navigateAction) return;

            UnbindLobbyInput();
            _cancelAction = cancelAction;
            _submitAction = submitAction;
            _navigateAction = navigateAction;
            if (_cancelAction != null) _cancelAction.performed += HandleCancelPerformed;
            if (_submitAction != null) _submitAction.performed += HandleSubmitPerformed;
            if (_navigateAction != null) _navigateAction.performed += HandleNavigatePerformed;
        }

        private void UnbindLobbyInput()
        {
            if (_cancelAction != null) _cancelAction.performed -= HandleCancelPerformed;
            if (_submitAction != null) _submitAction.performed -= HandleSubmitPerformed;
            if (_navigateAction != null) _navigateAction.performed -= HandleNavigatePerformed;
            _cancelAction = null;
            _submitAction = null;
            _navigateAction = null;
        }

        private void HandleNavigatePerformed(InputAction.CallbackContext context)
        {
            _lastNavigationWasGamepad = context.control?.device is Gamepad;
            if (!_lastNavigationWasGamepad || context.ReadValue<Vector2>().sqrMagnitude < 0.1f) return;
            _usingGamepad = true;
            ReleaseGamepadInputFieldEditing();
            EnsureGamepadSelection();
        }

        private void HandleInputSystemDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is not Gamepad) return;

            if (change is InputDeviceChange.Added or InputDeviceChange.Reconnected or InputDeviceChange.Enabled)
            {
                _usingGamepad = true;
                _lastNavigationWasGamepad = true;
                FocusActivePanel(false);
            }
            else if (change is InputDeviceChange.Removed or InputDeviceChange.Disconnected or InputDeviceChange.Disabled)
            {
                _usingGamepad = Gamepad.current != null;
                if (!_usingGamepad) HideActiveGamepadFocusFrame();
            }
        }

        private void HandleCancelPerformed(InputAction.CallbackContext context)
        {
            if (context.control?.device is Gamepad) _usingGamepad = true;
            if (_busy || (_inputSettings != null && _inputSettings.IsVisible)) return;

            if (_virtualKeyboardPanel != null && _virtualKeyboardPanel.activeSelf)
            {
                HideVirtualKeyboard();
                return;
            }

            if (EventSystem.current?.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.TryGetComponent(out TMP_InputField input) &&
                input.isFocused)
            {
                input.DeactivateInputField();
                return;
            }

            if (_passwordPromptPanel != null && _passwordPromptPanel.activeSelf)
            {
                HidePasswordPrompt();
                FocusActivePanel();
                return;
            }

            if (_activePanel == _modePanel) ShowLanding();
            else if (_activePanel == _createPanel || _activePanel == _joinPanel) ShowModeSelection();
            else if (_activePanel == _settingsPanel) ShowLanding();
            else if (_activePanel == _roomPanel) LeaveRoom();
        }

        private void HandleSubmitPerformed(InputAction.CallbackContext context)
        {
            if (context.control?.device is not Gamepad || _busy ||
                (_inputSettings != null && _inputSettings.IsVisible) ||
                (_virtualKeyboardPanel != null && _virtualKeyboardPanel.activeSelf))
                return;

            _usingGamepad = true;
            _lastNavigationWasGamepad = true;
            GameObject selected = EventSystem.current?.currentSelectedGameObject;
            if (selected == null || !selected.activeInHierarchy)
            {
                FocusActivePanel();
                return;
            }
            if (selected != null && selected.TryGetComponent(out TMP_InputField input))
            {
                if (_virtualKeyboardOpenCoroutine != null) StopCoroutine(_virtualKeyboardOpenCoroutine);
                _virtualKeyboardOpenCoroutine = StartCoroutine(OpenVirtualKeyboardNextFrame(input));
            }
        }

        private void HandleInputDeviceChanged(InputDeviceType deviceType)
        {
            _usingGamepad = Gamepad.current != null;
            _lastNavigationWasGamepad = deviceType == InputDeviceType.Gamepad;
            if (_usingGamepad) FocusActivePanel(false);
        }

        private IEnumerator EnsureInitialGamepadFocus()
        {
            for (int i = 0; i < 3; i++)
            {
                yield return null;
                BindLobbyInput();
                if (Gamepad.current == null) continue;
                _usingGamepad = true;
                _lastNavigationWasGamepad = true;
                EnsureGamepadSelection();
            }
        }

        private void FocusActivePanel(bool force = true)
        {
            if (_activePanel == null || EventSystem.current == null) return;
            if (_virtualKeyboardPanel != null && _virtualKeyboardPanel.activeSelf)
            {
                Select(_virtualKeyboardFirstKey);
                return;
            }
            if (_passwordPromptPanel != null && _passwordPromptPanel.activeSelf)
            {
                Select(_roomPasswordPrompt);
                return;
            }
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (!force && selected != null && selected.activeInHierarchy && selected.transform.IsChildOf(_activePanel.transform))
                return;
            if (!_panelDefaultSelections.TryGetValue(_activePanel, out Selectable selectable)) return;

            if (_selectionCoroutine != null) StopCoroutine(_selectionCoroutine);
            Select(selectable);
            _selectionCoroutine = StartCoroutine(SelectNextFrame(selectable, _activePanel));
        }

        private void EnsureGamepadSelection()
        {
            if (!_usingGamepad || EventSystem.current == null || _busy ||
                (_inputSettings != null && _inputSettings.IsVisible))
                return;

            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.activeInHierarchy && selected.GetComponent<Selectable>() != null)
                return;

            FocusActivePanel();
        }

        private void ReleaseGamepadInputFieldEditing()
        {
            if (!_lastNavigationWasGamepad || _virtualKeyboardPanel == null || _virtualKeyboardPanel.activeSelf ||
                EventSystem.current?.currentSelectedGameObject == null)
                return;

            if (EventSystem.current.currentSelectedGameObject.TryGetComponent(out TMP_InputField input) && input.isFocused)
                input.DeactivateInputField();
        }

        private IEnumerator SelectNextFrame(Selectable selectable, GameObject expectedPanel)
        {
            yield return null;
            _selectionCoroutine = null;
            if (_activePanel == expectedPanel && selectable != null && selectable.IsActive() && selectable.IsInteractable())
                Select(selectable);
        }

        private async void CreateRoom()
        {
            if (_busy || !ValidateServices()) return;
            string playerName = Normalize(_createPlayerName.text);
            string roomName = Normalize(_createRoomName.text);
            string password = _createPassword.text.Trim();
            if (!ValidateNames(playerName, roomName)) return;

            SavePlayerName(playerName);
            SetBusy(true, "Creating room and secure Relay...");
            try
            {
                await _lobbyManager.Authenticate(playerName);
                await _lobbyManager.CreateLobby(roomName, 2, false, password);
            }
            catch (Exception exception) { SetStatus(FriendlyError(exception), Red); }
            finally { SetBusy(false); }
        }

        private async void JoinRoomByName()
        {
            if (_busy || !ValidateServices()) return;
            string playerName = Normalize(_joinPlayerName.text);
            string roomName = Normalize(_joinRoomName.text);
            string password = _joinPassword.text.Trim();
            if (!ValidateNames(playerName, roomName)) return;

            SavePlayerName(playerName);
            SetBusy(true, "Connecting to room...");
            try
            {
                await _lobbyManager.Authenticate(playerName);
                await _lobbyManager.JoinLobbyByName(roomName, password);
            }
            catch (Exception exception) { SetStatus(FriendlyError(exception), Red); }
            finally { SetBusy(false); }
        }

        private async void JoinSelectedRoom(string password)
        {
            if (_busy || _selectedLobby == null || !ValidateServices()) return;
            string playerName = Normalize(_joinPlayerName.text);
            if (string.IsNullOrWhiteSpace(playerName))
            {
                SetStatus("Enter your character name first", Red);
                return;
            }
            SavePlayerName(playerName);
            SetBusy(true, $"Joining {_selectedLobby.Name}...");
            try
            {
                await _lobbyManager.Authenticate(playerName);
                await _lobbyManager.JoinLobbyById(_selectedLobby.Id, password);
            }
            catch (Exception exception) { SetStatus(FriendlyError(exception), Red); }
            finally { SetBusy(false); }
        }

        private void JoinSelectedRoomFromPrompt()
        {
            string password = _roomPasswordPrompt.text.Trim();
            JoinSelectedRoom(password);
        }

        private async void RefreshRoomBrowser()
        {
            if (_busy || !ValidateServices(false)) return;
            string playerName = Normalize(_joinPlayerName?.text);
            if (string.IsNullOrEmpty(playerName)) playerName = "Traveler";
            SetBusy(true, "Refreshing open rooms...");
            try
            {
                await _lobbyManager.Authenticate(playerName);
                IReadOnlyList<LobbyModel> rooms = await _lobbyManager.QueryPublicLobbies();
                RebuildRoomBrowser(rooms);
                SetStatus(rooms.Count == 0 ? "No open rooms yet. Create one or refresh." : $"Found {rooms.Count} open room(s)", rooms.Count == 0 ? Gold : Teal);
            }
            catch (Exception exception)
            {
                RebuildRoomBrowser(Array.Empty<LobbyModel>());
                SetStatus(FriendlyError(exception), Red);
            }
            finally { SetBusy(false); }
        }

        private async void ToggleReady()
        {
            if (_busy || _lobbyManager?.CurrentLobby == null) return;
            bool next = !_localReady;
            SetBusy(true, next ? "Marking ready..." : "Marking not ready...");
            try
            {
                await _lobbyManager.SetPlayerReady(next);
                _localReady = next;

                if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
                {
                    if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent<LobbyPlayerState>(out var localPlayer))
                    {
                        if (localPlayer.IsReady.Value != next)
                        {
                            localPlayer.ToggleReadyServerRpc();
                        }
                    }
                }

                bool soloLobby = (_currentLobby?.Players?.Count ?? 0) == 1;
                SetStatus(next
                    ? (soloLobby ? "Ready - solo start enabled" : "Ready - waiting for your companion")
                    : "Not ready", next ? Green : Red);
            }
            catch (Exception exception) { SetStatus(FriendlyError(exception), Red); }
            finally
            {
                SetBusy(false);
                RefreshRoomState();
            }
        }

        private void StartJourney()
        {
            if (!CanStartJourney()) return;
            int playerCount = _currentLobby?.Players?.Count ?? 0;
            SetStatus(playerCount == 1
                ? "Starting solo test journey..."
                : "Both players ready. Starting journey...", Gold);
            _lobbyManager.StartGame(_gameSceneName);
        }

        private async void LeaveRoom()
        {
            if (_busy) return;
            NetworkDisconnectCoordinator.PrepareForLocalExit();
            SetBusy(true, "Leaving room...");
            try { if (_lobbyManager != null) await _lobbyManager.LeaveLobby(); }
            finally
            {
                _currentLobby = null;
                _localReady = false;
                SetBusy(false);
                ShowModeSelection();
            }
        }

        private void HandleLobbyJoined(LobbyModel lobby)
        {
            _currentLobby = lobby;
            _localReady = false;
            _roomTitleText.text = lobby.Name;
            _roomCodeText.text = GetRoomCode(lobby);
            ShowRoom();
            SetStatus(lobby.HostId == _lobbyManager.GetPlayerId()
                ? "Room created - ready up to test solo or invite a friend"
                : "Connected - choose Ready when prepared", Paper);
        }

        private void HandleLobbyUpdated(LobbyModel lobby)
        {
            if (_currentLobby == null || _currentLobby.Id != lobby.Id) return;
            _currentLobby = lobby;
            RefreshRoomState();
        }

        private void HandleLobbyLeft()
        {
            _currentLobby = null;
            _localReady = false;
            _lastRosterSignature = null;
            if (_activePanel == _roomPanel) ShowModeSelection();
        }

        private void HandleNetworkChanged(ulong _) => RefreshRoomState();

        private void RefreshRoomState()
        {
            if (_roomPanel == null || !_roomPanel.activeSelf || _currentLobby == null) return;
            List<LobbyPlayerModel> players = _currentLobby.Players?
                .OrderBy(player => player.Id == _currentLobby.HostId ? 0 : 1).ToList()
                ?? new List<LobbyPlayerModel>();

            string signature = string.Join("|", players.Select(player => $"{player.Id}:{GetPlayerName(player)}:{IsReady(player)}"));
            if (signature != _lastRosterSignature)
            {
                _lastRosterSignature = signature;
                for (int i = 0; i < 2; i++) UpdatePlayerCard(i, i < players.Count ? players[i] : null);
            }

            LobbyPlayerModel local = players.FirstOrDefault(player => player.Id == _lobbyManager.GetPlayerId());
            _localReady = local != null && IsReady(local);
            _readyButtonText.text = _localReady ? "UNREADY" : "READY";
            ApplyButtonArt(_readyButton, _config?.RoomReadyButton);

            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            _startButton.gameObject.SetActive(isHost);
            bool canStart = CanStartJourney();
            _startButton.interactable = !_busy && canStart;
            _startButtonText.text = canStart
                ? (players.Count == 1 ? "START SOLO" : "START GAME")
                : "READY UP TO START";
            ApplyButtonArt(_startButton, canStart ? _config?.RoomStartButton : _config?.RoomWaitingButton);
        }

        private bool CanStartJourney()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsHost) return false;

            int lobbyPlayerCount = _currentLobby?.Players?.Count ?? 0;
            int connectedPlayerCount = manager.ConnectedClientsIds.Count;

            if (lobbyPlayerCount < 1 || lobbyPlayerCount > 2) return false;
            if (connectedPlayerCount != lobbyPlayerCount) return false;

            bool ugsAllReady = _currentLobby?.Players != null && _currentLobby.Players.Count > 0 && _currentLobby.Players.All(IsReady);

            var playerStates = UnityEngine.Object.FindObjectsByType<LobbyPlayerState>(FindObjectsSortMode.None);
            bool netcodeAllReady = playerStates.Length > 0 && playerStates.Length == connectedPlayerCount && playerStates.All(p => p.IsReady.Value);

            return ugsAllReady || netcodeAllReady;
        }

        private void UpdatePlayerCard(int index, LobbyPlayerModel player)
        {
            bool occupied = player != null;
            bool ready = occupied && IsReady(player);
            bool isHostPlayer = occupied && player.Id == _currentLobby.HostId;
            _cardBorders[index].color = ready ? Green : Red;
            _cardNames[index].text = occupied ? GetPlayerName(player) : "OPEN SLOT";
            _cardRoles[index].text = isHostPlayer ? "HOST" : occupied ? "CLIENT" : "WAITING";
            _cardStates[index].text = ready ? "READY" : occupied ? "NOT READY" : "INVITE A FRIEND";
            _cardStates[index].color = ready ? Green : Red;
            _cardAvatars[index].sprite = isHostPlayer ? _config?.HostPortrait : _config?.ClientPortrait;
            _cardAvatars[index].color = Color.white;
            _cardAvatars[index].enabled = occupied;
        }

        private void RebuildRoomBrowser(IReadOnlyList<LobbyModel> rooms)
        {
            _selectedLobby = null;
            _roomBrowserButtons.Clear();
            HidePasswordPrompt();
            for (int i = _browserList.childCount - 1; i >= 0; i--) Destroy(_browserList.GetChild(i).gameObject);

            if (rooms.Count == 0)
            {
                CreateListLabel(_browserList, "No rooms are currently open", Muted);
                ConfigureJoinPanelNavigation();
                return;
            }

            foreach (LobbyModel room in rooms)
            {
                string lockText = room.HasPassword ? "  [PASSWORD]" : string.Empty;
                Button row = CreateButton(_browserList, $"{room.Name}{lockText}    {room.Players.Count}/{room.MaxPlayers}", PanelSoft, Vector2.zero, 590f, 70f, 16f);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 70f;
                row.onClick.AddListener(() => SelectRoom(room, row));
                _roomBrowserButtons.Add(row);
            }

            ConfigureJoinPanelNavigation();
        }

        private void SelectRoom(LobbyModel room, Button row)
        {
            _selectedLobby = room;
            foreach (Transform child in _browserList)
            {
                Image image = child.GetComponent<Image>();
                if (image != null) image.color = child == row.transform ? Teal : PanelSoft;
            }
            if (room.HasPassword)
            {
                ShowPasswordPrompt(room);
                SetStatus($"Enter the password for {room.Name}", Gold);
            }
            else
            {
                SetStatus($"Joining {room.Name}...", Teal);
                JoinSelectedRoom(string.Empty);
            }
        }

        private void ShowPasswordPrompt(LobbyModel room)
        {
            _passwordPromptTitle.text = $"JOIN {room.Name.ToUpperInvariant()}";
            _roomPasswordPrompt.SetTextWithoutNotify(string.Empty);
            SetPasswordVisibility(_roomPasswordPrompt, false);
            _passwordPromptPanel.SetActive(true);
            _passwordPromptPanel.transform.SetAsLastSibling();
            Select(_roomPasswordPrompt);
        }

        private void HidePasswordPrompt()
        {
            if (_passwordPromptPanel == null) return;
            _roomPasswordPrompt?.SetTextWithoutNotify(string.Empty);
            _passwordPromptPanel.SetActive(false);
        }

        private void OpenControls()
        {
            _inputSettings ??= FindFirstObjectByType<InputSettingsPanelController>();
            if (_inputSettings != null && !_inputSettings.TryInitializeVisualTree())
                _inputSettings = null;
            if (_inputSettings == null)
                _inputSettings = CreateInputSettingsSystem();

            if (_inputSettings == null)
            {
                SetStatus("Controls panel could not be loaded", Red);
                return;
            }

            // Thuận's rebinding panel is a UIDocument. Keep its original controller/service and
            // only raise its presentation above the runtime lobby Canvas (sorting order 1000).
            foreach (UIDocument document in
                     _inputSettings.GetComponentsInParent<UIDocument>(true)
                         .Concat(_inputSettings.GetComponentsInChildren<UIDocument>(true)))
            {
                document.sortingOrder = 2000;
            }

            _inputSettings.Show(() =>
            {
                _settingsPanel.SetActive(true);
                _activePanel = _settingsPanel;
                FocusActivePanel();
            }, managePlayerInput: false);

            if (_inputSettings.IsVisible)
                _settingsPanel.SetActive(false);
            else
                SetStatus("Controls panel failed to initialize", Red);
        }

        private InputSettingsPanelController CreateInputSettingsSystem()
        {
            if (_config == null || _config.InputPanelSettings == null ||
                _config.InputSettingsUxml == null || _config.InputActions == null)
                return null;

            GameObject systems = new("Lobby_InputSettings_System");
            systems.SetActive(false);

            PlayerPrefsBindingPersistence persistence = systems.AddComponent<PlayerPrefsBindingPersistence>();
            InputRebindService rebindService = systems.AddComponent<InputRebindService>();
            rebindService.Configure(_config.InputActions, persistence);

            GameObject panelObject = new("InputSettingsPanel");
            panelObject.transform.SetParent(systems.transform, false);
            UIDocument document = panelObject.AddComponent<UIDocument>();
            UnityEngine.UIElements.PanelSettings runtimePanelSettings = Instantiate(_config.InputPanelSettings);
            runtimePanelSettings.name = "Lobby_InputSettings_PanelSettings";
            runtimePanelSettings.sortingOrder = 3000;
            document.panelSettings = runtimePanelSettings;
            document.visualTreeAsset = _config.InputSettingsUxml;
            document.sortingOrder = 2000;

            InputSettingsPanelController controller = panelObject.AddComponent<InputSettingsPanelController>();
            controller.Configure(rebindService, _config.InputIconMap, document);
            systems.SetActive(true);
            return controller;
        }

        private void BuildInterface()
        {
            Canvas canvas = new GameObject("Canvas_ProfessionalLobby", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.transform.SetParent(transform, false);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform root = Stretch(new GameObject("Background", typeof(RectTransform), typeof(Image)), canvas.transform);
            Image background = root.GetComponent<Image>();
            background.sprite = Resources.Load<Sprite>("UI/LobbyAutoBackground");
            background.color = background.sprite == null ? new Color(0.05f, 0.13f, 0.20f, 1f) : Color.white;
            Stretch(new GameObject("Vignette", typeof(RectTransform), typeof(Image)), root).GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.04f, 0.35f);

            RectTransform shell = Rect("LobbyShell", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1480f, 920f), new Vector2(0.5f, 0.5f));
            shell.gameObject.AddComponent<Image>().color = Navy;
            _shellGroup = shell.gameObject.AddComponent<CanvasGroup>();
            RectTransform accent = Rect("Accent", shell, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 6f), new Vector2(0.5f, 1f));
            accent.gameObject.AddComponent<Image>().color = Gold;

            if (_config != null && _config.LobbyLogo != null)
            {
                RectTransform logo = Rect("TheFlowerLogo", shell, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(38f, -18f), new Vector2(390f, 135f), new Vector2(0f, 1f));
                Image logoImage = logo.gameObject.AddComponent<Image>();
                logoImage.sprite = _config.LobbyLogo;
                logoImage.preserveAspect = true;
                logoImage.raycastTarget = false;
            }
            else
            {
                TMP_Text brand = CreateText(shell, "THE FLOWER", 38f, Paper, FontStyles.Bold, TextAlignmentOptions.Left);
                brand.characterSpacing = 5f;
                Place(brand.rectTransform, new Vector2(48f, -38f), new Vector2(500f, 60f), new Vector2(0f, 1f));
            }
            TMP_Text chapter = CreateText(shell, "CO-OP LOBBY", 15f, Gold, FontStyles.Bold, TextAlignmentOptions.Right);
            Place(chapter.rectTransform, new Vector2(-48f, -48f), new Vector2(400f, 32f), new Vector2(1f, 1f));

            RectTransform content = Rect("Content", shell, Vector2.zero, Vector2.one, new Vector2(42f, 68f), new Vector2(-84f, -170f), Vector2.zero);
            _landingPanel = CreateLandingPanel(content);
            _modePanel = CreateModePanel(content);
            _createPanel = CreateCreatePanel(content);
            _joinPanel = CreateJoinPanel(content);
            _roomPanel = CreateRoomPanel(content);
            _settingsPanel = CreateSettingsPanel(content);

            _statusText = CreateText(shell, string.Empty, 17f, Paper, FontStyles.Normal, TextAlignmentOptions.Left);
            Place(_statusText.rectTransform, new Vector2(48f, 28f), new Vector2(1320f, 42f), Vector2.zero);
            _virtualKeyboardPanel = CreateVirtualKeyboard(shell);
            EnsureEventSystem();
        }

        private GameObject CreateLandingPanel(RectTransform parent)
        {
            RectTransform panel = Stretch(new GameObject("LandingPanel", typeof(RectTransform)), parent);
            TMP_Text title = CreateText(panel, "BEGIN YOUR JOURNEY", 48f, Paper, FontStyles.Bold, TextAlignmentOptions.Center);
            StyleDisplayHeading(title);
            Place(title.rectTransform, new Vector2(0f, -105f), new Vector2(900f, 70f), new Vector2(0.5f, 1f));
            TMP_Text subtitle = CreateText(panel, "A cooperative adventure for two", 22f, new Color(0.85f, 1f, 0.90f, 1f), FontStyles.Italic, TextAlignmentOptions.Center);
            StyleColorfulCaption(subtitle);
            Place(subtitle.rectTransform, new Vector2(0f, -178f), new Vector2(800f, 40f), new Vector2(0.5f, 1f));
            Button start = CreateButton(panel, "START", Teal, new Vector2(0f, -300f), 520f, 130f, 24f);
            start.onClick.AddListener(ShowModeSelection);
            Button settings = CreateButton(panel, "SETTINGS", PanelSoft, new Vector2(0f, -448f), 520f, 110f, 19f);
            settings.onClick.AddListener(ShowSettings);
            SetExplicitNavigation(start, null, settings, null, null);
            SetExplicitNavigation(settings, start, null, null, null);
            _panelDefaultSelections[panel.gameObject] = start;
            return panel.gameObject;
        }

        private GameObject CreateModePanel(RectTransform parent)
        {
            RectTransform panel = Stretch(new GameObject("ModePanel", typeof(RectTransform)), parent);
            CreateHeading(panel, "PLAY TOGETHER", "Create a new room or find your companion");
            Button create = CreateButton(panel, "CREATE ROOM", Teal, new Vector2(-300f, -285f), 500f, 130f, 22f);
            create.onClick.AddListener(ShowCreate);
            Button join = CreateButton(panel, "JOIN ROOM", Gold, new Vector2(300f, -285f), 500f, 130f, 22f, Color.black);
            join.onClick.AddListener(ShowJoin);
            Button back = CreateButton(panel, "BACK", PanelSoft, new Vector2(0f, -455f), 360f, 62f, 17f);
            back.onClick.AddListener(ShowLanding);
            SetExplicitNavigation(create, null, back, null, join);
            SetExplicitNavigation(join, null, back, create, null);
            SetExplicitNavigation(back, create, null, create, join);
            _panelDefaultSelections[panel.gameObject] = create;
            return panel.gameObject;
        }

        private GameObject CreateCreatePanel(RectTransform parent)
        {
            RectTransform panel = Stretch(new GameObject("CreatePanel", typeof(RectTransform)), parent);
            CreateHeading(panel, "CREATE ROOM", "Set your identity and room details");
            RectTransform form = CreateCard(panel, "CreateForm", new Vector2(0f, -170f), new Vector2(720f, 500f));
            CreateFieldLabel(form, "CHARACTER NAME", -36f);
            _createPlayerName = CreateInput(form, "Your character name", new Vector2(0f, -66f), 620f);
            CreateFieldLabel(form, "ROOM NAME", -150f);
            _createRoomName = CreateInput(form, "Example: Cloud Garden", new Vector2(0f, -180f), 620f);
            CreateFieldLabel(form, "PASSWORD - OPTIONAL", -264f);
            _createPassword = CreateInput(form, "Leave empty for no password", new Vector2(0f, -294f), 620f, true);
            Button create = CreateButton(form, "CREATE", Teal, new Vector2(-160f, -394f), 300f, 66f, 18f);
            create.onClick.AddListener(CreateRoom);
            Button back = CreateButton(form, "CANCEL", PanelSoft, new Vector2(160f, -394f), 300f, 66f, 18f);
            back.onClick.AddListener(ShowModeSelection);
            _createSubmitButton = create;
            _createCancelButton = back;
            ConfigureCreatePanelNavigation();
            _panelDefaultSelections[panel.gameObject] = create;
            return panel.gameObject;
        }

        private GameObject CreateJoinPanel(RectTransform parent)
        {
            RectTransform panel = Stretch(new GameObject("JoinPanel", typeof(RectTransform)), parent);
            TMP_Text title = CreateText(panel, "JOIN ROOM", 34f, Paper, FontStyles.Bold, TextAlignmentOptions.Left);
            StyleDisplayHeading(title);
            Place(title.rectTransform, new Vector2(18f, -52f), new Vector2(500f, 50f), new Vector2(0f, 1f));

            RectTransform manual = CreateCard(panel, "ManualJoin", new Vector2(0f, -120f), new Vector2(590f, 570f), new Vector2(0f, 1f));
            TMP_Text manualTitle = CreateText(manual, "JOIN BY ROOM NAME", 20f, Gold, FontStyles.Bold, TextAlignmentOptions.Left);
            Place(manualTitle.rectTransform, new Vector2(30f, -28f), new Vector2(500f, 36f), new Vector2(0f, 1f));
            CreateFieldLabel(manual, "CHARACTER NAME", -92f, 30f);
            _joinPlayerName = CreateInput(manual, "Your character name", new Vector2(30f, -122f), 530f, false, new Vector2(0f, 1f));
            CreateFieldLabel(manual, "ROOM NAME", -206f, 30f);
            _joinRoomName = CreateInput(manual, "Exact room name", new Vector2(30f, -236f), 530f, false, new Vector2(0f, 1f));
            CreateFieldLabel(manual, "PASSWORD", -320f, 30f);
            _joinPassword = CreateInput(manual, "Only if protected", new Vector2(30f, -350f), 530f, true, new Vector2(0f, 1f));
            Button join = CreateButton(manual, "JOIN", Teal, new Vector2(30f, -452f), 530f, 90f, 19f, null, new Vector2(0f, 1f));
            join.onClick.AddListener(JoinRoomByName);
            _joinSubmitButton = join;

            RectTransform browser = CreateCard(panel, "RoomBrowser", new Vector2(-2f, -120f), new Vector2(680f, 570f), new Vector2(1f, 1f));
            TMP_Text browserTitle = CreateText(browser, "OPEN ROOMS", 20f, Gold, FontStyles.Bold, TextAlignmentOptions.Left);
            Place(browserTitle.rectTransform, new Vector2(30f, -28f), new Vector2(300f, 36f), new Vector2(0f, 1f));
            Button refresh = CreateButton(browser, "RELOAD", PanelSoft, new Vector2(-30f, -22f), 180f, 48f, 14f, null, new Vector2(1f, 1f));
            refresh.onClick.AddListener(RefreshRoomBrowser);
            _joinRefreshButton = refresh;
            _browserList = Rect("RoomList", browser, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -92f), new Vector2(-60f, 470f), new Vector2(0f, 1f));
            VerticalLayoutGroup listLayout = _browserList.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 10f;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = false;
            listLayout.childForceExpandWidth = true;
            Button back = CreateButton(panel, "BACK", PanelSoft, new Vector2(0f, 8f), 250f, 52f, 15f, null, new Vector2(0.5f, 0f));
            back.onClick.AddListener(ShowModeSelection);
            _joinBackButton = back;
            _panelDefaultSelections[panel.gameObject] = join;

            RectTransform promptOverlay = Stretch(new GameObject("RoomPasswordPrompt", typeof(RectTransform), typeof(Image)), panel);
            promptOverlay.GetComponent<Image>().color = new Color(0.01f, 0.10f, 0.09f, 0.88f);
            _passwordPromptPanel = promptOverlay.gameObject;
            RectTransform promptCard = Rect("PasswordCard", promptOverlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(650f, 390f), new Vector2(0.5f, 0.5f));
            promptCard.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.32f, 0.27f, 0.98f);
            _passwordPromptTitle = CreateText(promptCard, "JOIN PROTECTED ROOM", 29f, Gold, FontStyles.Bold, TextAlignmentOptions.Center);
            StyleDisplayHeading(_passwordPromptTitle);
            Place(_passwordPromptTitle.rectTransform, new Vector2(0f, -42f), new Vector2(570f, 50f), new Vector2(0.5f, 1f));
            TMP_Text promptHint = CreateText(promptCard, "This room is protected. Enter its password to continue.", 16f, Paper, FontStyles.Normal, TextAlignmentOptions.Center);
            Place(promptHint.rectTransform, new Vector2(0f, -104f), new Vector2(570f, 34f), new Vector2(0.5f, 1f));
            _roomPasswordPrompt = CreateInput(promptCard, "Room password", new Vector2(0f, -166f), 540f, true);
            Button confirmPassword = CreateButton(promptCard, "JOIN", Gold, new Vector2(-145f, -270f), 250f, 62f, 17f, Color.black);
            confirmPassword.onClick.AddListener(JoinSelectedRoomFromPrompt);
            Button cancelPassword = CreateButton(promptCard, "CANCEL", PanelSoft, new Vector2(145f, -270f), 250f, 62f, 17f);
            cancelPassword.onClick.AddListener(HidePasswordPrompt);
            _passwordConfirmButton = confirmPassword;
            _passwordCancelButton = cancelPassword;
            ConfigureJoinPanelNavigation();
            ConfigurePasswordPromptNavigation();
            _passwordPromptPanel.SetActive(false);
            return panel.gameObject;
        }

        private GameObject CreateRoomPanel(RectTransform parent)
        {
            RectTransform panel = Stretch(new GameObject("RoomPanel", typeof(RectTransform)), parent);
            _roomTitleText = CreateText(panel, "ROOM", 32f, Paper, FontStyles.Bold, TextAlignmentOptions.Left);
            StyleDisplayHeading(_roomTitleText);
            Place(_roomTitleText.rectTransform, new Vector2(10f, -50f), new Vector2(700f, 48f), new Vector2(0f, 1f));
            TMP_Text codeLabel = CreateText(panel, "ROOM CODE", 13f, Muted, FontStyles.Bold, TextAlignmentOptions.Right);
            Place(codeLabel.rectTransform, new Vector2(-10f, -4f), new Vector2(300f, 24f), new Vector2(1f, 1f));
            _roomCodeText = CreateText(panel, "----", 30f, Gold, FontStyles.Bold, TextAlignmentOptions.Right);
            Place(_roomCodeText.rectTransform, new Vector2(-10f, -26f), new Vector2(300f, 44f), new Vector2(1f, 1f));

            CreatePlayerCard(panel, 0, new Vector2(0f, -130f));
            CreatePlayerCard(panel, 1, new Vector2(-2f, -130f), new Vector2(1f, 1f));
            _readyButton = CreateButton(panel, "READY", Teal, new Vector2(-300f, 12f), 440f, 90f, 20f, null, new Vector2(0.5f, 0f));
            _readyButtonText = _readyButton.GetComponentInChildren<TMP_Text>();
            ApplyButtonArt(_readyButton, _config?.RoomReadyButton);
            _readyButton.onClick.AddListener(ToggleReady);
            _startButton = CreateButton(panel, "READY UP TO START", Gold, new Vector2(200f, 12f), 520f, 90f, 18f, Paper, new Vector2(0.5f, 0f));
            _startButtonText = _startButton.GetComponentInChildren<TMP_Text>();
            ApplyButtonArt(_startButton, _config?.RoomWaitingButton);
            _startButton.onClick.AddListener(StartJourney);
            Button leave = CreateButton(panel, "LEAVE", PanelSoft, new Vector2(570f, 12f), 180f, 90f, 16f, Paper, new Vector2(0.5f, 0f));
            ApplyButtonArt(leave, _config?.RoomLeaveButton);
            leave.onClick.AddListener(LeaveRoom);
            SetExplicitNavigation(_readyButton, null, null, null, _startButton);
            SetExplicitNavigation(_startButton, null, null, _readyButton, leave);
            SetExplicitNavigation(leave, null, null, _startButton, null);
            _panelDefaultSelections[panel.gameObject] = _readyButton;
            return panel.gameObject;
        }

        private GameObject CreateSettingsPanel(RectTransform parent)
        {
            RectTransform panel = Stretch(new GameObject("SettingsPanel", typeof(RectTransform)), parent);
            CreateHeading(panel, "SETTINGS", "Changes apply to lobby and gameplay");
            RectTransform card = CreateCard(panel, "SettingsCard", new Vector2(0f, -150f), new Vector2(820f, 560f));
            CreateVolumeSlider(card, "MASTER VOLUME", Constants.PlayerPrefsKeys.MASTER_VOLUME, -65f);
            CreateVolumeSlider(card, "MUSIC VOLUME", Constants.PlayerPrefsKeys.BGM_VOLUME, -165f);
            CreateVolumeSlider(card, "SFX VOLUME", Constants.PlayerPrefsKeys.SFX_VOLUME, -265f);
            _focusVisibilityButton = CreateButton(card, "GAMEPAD FOCUS: ON", PanelSoft, new Vector2(0f, -325f), 620f, 64f, 17f);
            _focusVisibilityButtonText = _focusVisibilityButton.GetComponentInChildren<TMP_Text>();
            _focusVisibilityButton.onClick.AddListener(ToggleGamepadFocusVisibility);
            RefreshGamepadFocusVisibilityLabel();
            Button controls = CreateButton(card, "KEY BINDINGS / CONTROLS", Teal, new Vector2(0f, -410f), 620f, 82f, 18f);
            controls.onClick.AddListener(OpenControls);
            Button back = CreateButton(panel, "BACK", PanelSoft, new Vector2(0f, -700f), 360f, 62f, 16f);
            back.onClick.AddListener(ShowLanding);
            ConfigureSettingsPanelNavigation(controls, back);
            SetExplicitNavigation(back, controls, null, null, null);
            _panelDefaultSelections[panel.gameObject] = controls;
            return panel.gameObject;
        }

        private void ConfigureSettingsPanelNavigation(Button controls, Button back)
        {
            for (int i = 0; i < _settingsSliders.Count; i++)
            {
                Selectable up = i > 0 ? _settingsSliders[i - 1] : null;
                Selectable down = i < _settingsSliders.Count - 1 ? _settingsSliders[i + 1] : _focusVisibilityButton;
                SetExplicitNavigation(_settingsSliders[i], up, down, null, null);
            }

            Selectable lastSlider = _settingsSliders.Count > 0 ? _settingsSliders[_settingsSliders.Count - 1] : null;
            SetExplicitNavigation(_focusVisibilityButton, lastSlider, controls, null, null);
            SetExplicitNavigation(controls, _focusVisibilityButton, back, null, null);
        }

        private void ToggleGamepadFocusVisibility()
        {
            _showGamepadFocusFrames = !_showGamepadFocusFrames;
            PlayerPrefs.SetInt(GamepadFocusVisiblePref, _showGamepadFocusFrames ? 1 : 0);
            PlayerPrefs.Save();
            RefreshGamepadFocusVisibilityLabel();
            if (!_showGamepadFocusFrames) HideActiveGamepadFocusFrame();
        }

        private void RefreshGamepadFocusVisibilityLabel()
        {
            if (_focusVisibilityButtonText != null)
                _focusVisibilityButtonText.text = _showGamepadFocusFrames ? "GAMEPAD FOCUS: ON" : "GAMEPAD FOCUS: OFF";
        }

        private GameObject CreateVirtualKeyboard(RectTransform parent)
        {
            RectTransform overlay = Stretch(new GameObject("GamepadVirtualKeyboard", typeof(RectTransform), typeof(Image)), parent);
            Image overlayImage = overlay.GetComponent<Image>();
            overlayImage.color = new Color(0.01f, 0.04f, 0.05f, 0.98f);

            RectTransform card = CreateCard(overlay, "VirtualKeyboardCard", Vector2.zero, new Vector2(1200f, 760f), new Vector2(0.5f, 0.5f));
            card.GetComponent<Image>().color = new Color(0.035f, 0.25f, 0.25f, 1f);
            _virtualKeyboardTitle = CreateText(card, "VIRTUAL KEYBOARD", 30f, Gold, FontStyles.Bold, TextAlignmentOptions.Center);
            StyleDisplayHeading(_virtualKeyboardTitle);
            Place(_virtualKeyboardTitle.rectTransform, new Vector2(0f, -34f), new Vector2(1050f, 54f), new Vector2(0.5f, 1f));

            RectTransform previewRoot = Rect("KeyboardPreview", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -102f), new Vector2(1020f, 74f), new Vector2(0.5f, 1f));
            previewRoot.gameObject.AddComponent<Image>().color = new Color(0.94f, 0.95f, 0.91f, 1f);
            _virtualKeyboardPreview = CreateText(previewRoot, string.Empty, 24f, new Color(0.03f, 0.08f, 0.09f, 1f), FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(_virtualKeyboardPreview.rectTransform.gameObject, previewRoot);

            AddVirtualKeyboardRow(card, "ABCDEFGHIJ", -215f);
            AddVirtualKeyboardRow(card, "KLMNOPQRS", -310f);
            AddVirtualKeyboardRow(card, "TUVWXYZ123", -405f);
            AddVirtualKeyboardRow(card, "456789-_.@", -500f);

            Button shift = CreateButton(card, "Aa", PanelSoft, new Vector2(-475f, -620f), 150f, 76f, 19f);
            shift.onClick.AddListener(ToggleVirtualKeyboardCase);
            Button space = CreateButton(card, "SPACE", PanelSoft, new Vector2(-230f, -620f), 300f, 76f, 18f);
            space.onClick.AddListener(() => AppendVirtualKeyboardCharacter(' '));
            Button backspace = CreateButton(card, "BACKSPACE", PanelSoft, new Vector2(65f, -620f), 220f, 76f, 16f);
            backspace.onClick.AddListener(RemoveVirtualKeyboardCharacter);
            Button done = CreateButton(card, "DONE", Teal, new Vector2(285f, -620f), 170f, 76f, 18f);
            done.onClick.AddListener(HideVirtualKeyboard);
            Button hide = CreateButton(card, "HIDE", Gold, new Vector2(465f, -620f), 150f, 76f, 18f, Color.black);
            hide.onClick.AddListener(HideVirtualKeyboard);

            _virtualKeyboardRows.Add(new List<Button> { shift, space, backspace, done, hide });
            ConfigureVirtualKeyboardNavigation();

            overlay.gameObject.SetActive(false);
            return overlay.gameObject;
        }

        private void AddVirtualKeyboardRow(Transform parent, string characters, float y)
        {
            const float keyWidth = 82f;
            const float spacing = 14f;
            float rowWidth = characters.Length * keyWidth + (characters.Length - 1) * spacing;
            float startX = -rowWidth * 0.5f + keyWidth * 0.5f;
            List<Button> row = new(characters.Length);

            for (int i = 0; i < characters.Length; i++)
            {
                char character = characters[i];
                Button key = CreateButton(parent, character.ToString(), PanelSoft,
                    new Vector2(startX + i * (keyWidth + spacing), y), keyWidth, 70f, 21f);
                key.onClick.AddListener(() => AppendVirtualKeyboardCharacter(character));
                _virtualCharacterKeys.Add((key, character));
                row.Add(key);
                _virtualKeyboardFirstKey ??= key;
            }

            _virtualKeyboardRows.Add(row);
        }

        private void ConfigureCreatePanelNavigation()
        {
            DisablePasswordToggleNavigation(_createPassword);
            SetExplicitNavigation(_createPlayerName, null, _createRoomName, null, null);
            SetExplicitNavigation(_createRoomName, _createPlayerName, _createPassword, null, null);
            SetExplicitNavigation(_createPassword, _createRoomName, _createSubmitButton, null, null);
            SetExplicitNavigation(_createSubmitButton, _createPassword, _createCancelButton, null, _createCancelButton);
            SetExplicitNavigation(_createCancelButton, _createPassword, null, _createSubmitButton, null);
        }

        private void ConfigureJoinPanelNavigation()
        {
            DisablePasswordToggleNavigation(_joinPassword);
            SetExplicitNavigation(_joinPlayerName, null, _joinRoomName, null, _joinRefreshButton);
            SetExplicitNavigation(_joinRoomName, _joinPlayerName, _joinPassword, null, _joinRefreshButton);
            SetExplicitNavigation(_joinPassword, _joinRoomName, _joinSubmitButton, null, _joinRefreshButton);
            SetExplicitNavigation(_joinSubmitButton, _joinPassword, _joinBackButton, null, _joinRefreshButton);

            Selectable browserDown = _roomBrowserButtons.Count > 0 ? _roomBrowserButtons[0] : _joinBackButton;
            SetExplicitNavigation(_joinRefreshButton, null, browserDown, _joinSubmitButton, null);
            SetExplicitNavigation(_joinBackButton, _joinSubmitButton, null, _joinSubmitButton, _joinRefreshButton);

            for (int i = 0; i < _roomBrowserButtons.Count; i++)
            {
                Button row = _roomBrowserButtons[i];
                Selectable up = i == 0 ? _joinRefreshButton : _roomBrowserButtons[i - 1];
                Selectable down = i == _roomBrowserButtons.Count - 1 ? _joinBackButton : _roomBrowserButtons[i + 1];
                SetExplicitNavigation(row, up, down, _joinSubmitButton, null);
            }
        }

        private void ConfigurePasswordPromptNavigation()
        {
            DisablePasswordToggleNavigation(_roomPasswordPrompt);
            SetExplicitNavigation(_roomPasswordPrompt, null, _passwordConfirmButton, null, null);
            SetExplicitNavigation(_passwordConfirmButton, _roomPasswordPrompt, null, null, _passwordCancelButton);
            SetExplicitNavigation(_passwordCancelButton, _roomPasswordPrompt, null, _passwordConfirmButton, null);
        }

        private void ConfigureVirtualKeyboardNavigation()
        {
            for (int rowIndex = 0; rowIndex < _virtualKeyboardRows.Count; rowIndex++)
            {
                List<Button> row = _virtualKeyboardRows[rowIndex];
                for (int column = 0; column < row.Count; column++)
                {
                    Button key = row[column];
                    Selectable left = column > 0 ? row[column - 1] : null;
                    Selectable right = column < row.Count - 1 ? row[column + 1] : null;
                    Selectable up = rowIndex > 0
                        ? FindNearestHorizontalKey(key, _virtualKeyboardRows[rowIndex - 1])
                        : null;
                    Selectable down = rowIndex < _virtualKeyboardRows.Count - 1
                        ? FindNearestHorizontalKey(key, _virtualKeyboardRows[rowIndex + 1])
                        : null;
                    SetExplicitNavigation(key, up, down, left, right);
                }
            }
        }

        private static Button FindNearestHorizontalKey(Button source, IReadOnlyList<Button> candidates)
        {
            if (source == null || candidates == null || candidates.Count == 0) return null;
            float sourceX = ((RectTransform)source.transform).anchoredPosition.x;
            Button nearest = candidates[0];
            float nearestDistance = Mathf.Abs(((RectTransform)nearest.transform).anchoredPosition.x - sourceX);
            for (int i = 1; i < candidates.Count; i++)
            {
                float distance = Mathf.Abs(((RectTransform)candidates[i].transform).anchoredPosition.x - sourceX);
                if (distance >= nearestDistance) continue;
                nearest = candidates[i];
                nearestDistance = distance;
            }
            return nearest;
        }

        private static void DisablePasswordToggleNavigation(TMP_InputField input)
        {
            Button toggle = input != null ? input.transform.Find("PasswordVisibility")?.GetComponent<Button>() : null;
            if (toggle == null) return;
            Navigation navigation = toggle.navigation;
            navigation.mode = Navigation.Mode.None;
            toggle.navigation = navigation;
        }

        private static void SetExplicitNavigation(
            Selectable selectable,
            Selectable up,
            Selectable down,
            Selectable left,
            Selectable right)
        {
            if (selectable == null) return;
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = up;
            navigation.selectOnDown = down;
            navigation.selectOnLeft = left;
            navigation.selectOnRight = right;
            selectable.navigation = navigation;
        }

        private IEnumerator OpenVirtualKeyboardNextFrame(TMP_InputField input)
        {
            yield return null;
            _virtualKeyboardOpenCoroutine = null;
            if (input != null && input.gameObject.activeInHierarchy) OpenVirtualKeyboard(input);
        }

        private void OpenVirtualKeyboard(TMP_InputField input)
        {
            if (input == null || _virtualKeyboardPanel == null) return;
            _virtualKeyboardTarget = input;
            _virtualKeyboardUppercase = true;
            input.DeactivateInputField();

            bool playerName = input == _createPlayerName || input == _joinPlayerName;
            bool password = input == _createPassword || input == _joinPassword || input == _roomPasswordPrompt;
            _virtualKeyboardTitle.text = playerName ? "ENTER CHARACTER NAME" : password ? "ENTER PASSWORD" : "ENTER ROOM NAME";
            UpdateVirtualKeyboardLabels();
            RefreshVirtualKeyboardPreview();
            _virtualKeyboardPanel.SetActive(true);
            _virtualKeyboardPanel.transform.SetAsLastSibling();
            Select(_virtualKeyboardFirstKey);
        }

        private void AppendVirtualKeyboardCharacter(char character)
        {
            if (_virtualKeyboardTarget == null) return;
            int characterLimit = _virtualKeyboardTarget.characterLimit;
            int effectiveLimit = characterLimit > 0 ? characterLimit : 32;
            if (_virtualKeyboardTarget.text.Length >= effectiveLimit) return;

            if (char.IsLetter(character))
                character = _virtualKeyboardUppercase ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character);
            _virtualKeyboardTarget.SetTextWithoutNotify(_virtualKeyboardTarget.text + character);
            RefreshVirtualKeyboardPreview();
        }

        private void RemoveVirtualKeyboardCharacter()
        {
            if (_virtualKeyboardTarget == null || _virtualKeyboardTarget.text.Length == 0) return;
            string value = _virtualKeyboardTarget.text;
            _virtualKeyboardTarget.SetTextWithoutNotify(value.Substring(0, value.Length - 1));
            RefreshVirtualKeyboardPreview();
        }

        private void ToggleVirtualKeyboardCase()
        {
            _virtualKeyboardUppercase = !_virtualKeyboardUppercase;
            UpdateVirtualKeyboardLabels();
        }

        private void UpdateVirtualKeyboardLabels()
        {
            foreach ((Button button, char character) in _virtualCharacterKeys)
            {
                if (button == null || !char.IsLetter(character)) continue;
                TMP_Text label = button.GetComponentInChildren<TMP_Text>();
                if (label != null)
                    label.text = (_virtualKeyboardUppercase ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character)).ToString();
            }
        }

        private void RefreshVirtualKeyboardPreview()
        {
            if (_virtualKeyboardPreview == null || _virtualKeyboardTarget == null) return;
            string value = _virtualKeyboardTarget.text;
            _virtualKeyboardPreview.text = _virtualKeyboardTarget.contentType == TMP_InputField.ContentType.Password
                ? new string('\u2022', value.Length)
                : value;
        }

        private void HideVirtualKeyboard() => HideVirtualKeyboard(true);

        private void HideVirtualKeyboard(bool restorePanelFocus)
        {
            if (_virtualKeyboardPanel == null || !_virtualKeyboardPanel.activeSelf) return;
            if (_virtualKeyboardTarget != null)
            {
                _virtualKeyboardTarget.DeactivateInputField();
                _virtualKeyboardTarget.ForceLabelUpdate();
            }
            _virtualKeyboardTarget = null;
            _virtualKeyboardPanel.SetActive(false);
            if (restorePanelFocus) FocusActivePanel();
        }

        private static void CreateFocusRingEdge(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
        {
            RectTransform edge = Rect(name, parent, anchorMin, anchorMax, Vector2.zero, size, new Vector2(0.5f, 0.5f));
            Image image = edge.gameObject.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;
        }

        private void UpdateGamepadFocusRing()
        {
            GameObject selected = EventSystem.current?.currentSelectedGameObject;
            Selectable selectable = selected != null ? selected.GetComponent<Selectable>() : null;
            RectTransform targetRect = selected != null ? selected.GetComponent<RectTransform>() : null;
            bool visible = _showGamepadFocusFrames && _usingGamepad && (_inputSettings == null || !_inputSettings.IsVisible) &&
                           selected != null && selected.activeInHierarchy &&
                           selectable != null && targetRect != null;
            if (!visible)
            {
                HideActiveGamepadFocusFrame();
                return;
            }

            if (_gamepadFocusOwner != selected || _activeGamepadFocusFrame == null)
            {
                HideActiveGamepadFocusFrame();
                _gamepadFocusOwner = selected;
                Transform existing = targetRect.Find("GamepadFocusFrame");
                _activeGamepadFocusFrame = existing != null
                    ? existing.gameObject
                    : CreateGamepadFocusFrame(targetRect);
                _activeGamepadFocusGroup = _activeGamepadFocusFrame.GetComponent<CanvasGroup>();
            }

            _activeGamepadFocusFrame.SetActive(true);
            _activeGamepadFocusFrame.transform.SetAsLastSibling();
            _activeGamepadFocusGroup.alpha = selectable.IsInteractable()
                ? 0.82f + Mathf.Sin(Time.unscaledTime * 5f) * 0.14f
                : 0.42f;
        }

        private static GameObject CreateGamepadFocusFrame(RectTransform target)
        {
            RectTransform frame = new GameObject("GamepadFocusFrame", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            frame.SetParent(target, false);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = new Vector2(-8f, -8f);
            frame.offsetMax = new Vector2(8f, 8f);

            CanvasGroup group = frame.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            CreateFocusRingEdge(frame, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 4f));
            CreateFocusRingEdge(frame, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 4f));
            CreateFocusRingEdge(frame, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(4f, 0f));
            CreateFocusRingEdge(frame, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(4f, 0f));
            return frame.gameObject;
        }

        private void HideActiveGamepadFocusFrame()
        {
            if (_activeGamepadFocusFrame != null) _activeGamepadFocusFrame.SetActive(false);
            _activeGamepadFocusFrame = null;
            _activeGamepadFocusGroup = null;
            _gamepadFocusOwner = null;
        }

        private void CreatePlayerCard(Transform parent, int index, Vector2 position, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0f, 1f);
            RectTransform border = Rect($"PlayerCard{index + 1}", parent, a, a, position, new Vector2(640f, 500f), a);
            _cardBorders[index] = border.gameObject.AddComponent<Image>();
            _cardBorders[index].color = Red;
            RectTransform inner = Stretch(new GameObject("Inner", typeof(RectTransform), typeof(Image)), border);
            inner.offsetMin = new Vector2(5f, 5f);
            inner.offsetMax = new Vector2(-5f, -5f);
            inner.GetComponent<Image>().color = Panel;
            RectTransform avatar = Rect("CharacterPortrait", inner, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(250f, 250f), new Vector2(0.5f, 1f));
            avatar.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.06f, 0.09f, 1f);
            RectTransform portrait = Stretch(new GameObject("PortraitImage", typeof(RectTransform), typeof(Image)), avatar);
            portrait.offsetMin = new Vector2(8f, 8f);
            portrait.offsetMax = new Vector2(-8f, -8f);
            _cardAvatars[index] = portrait.GetComponent<Image>();
            _cardAvatars[index].sprite = index == 0 ? _config?.HostPortrait : _config?.ClientPortrait;
            _cardAvatars[index].preserveAspect = true;
            _cardAvatars[index].color = Color.white;
            _cardAvatars[index].enabled = false;
            _cardNames[index] = CreateText(inner, "OPEN SLOT", 28f, Paper, FontStyles.Bold, TextAlignmentOptions.Center);
            Place(_cardNames[index].rectTransform, new Vector2(0f, -312f), new Vector2(560f, 48f), new Vector2(0.5f, 1f));
            _cardRoles[index] = CreateText(inner, index == 0 ? "HOST" : "CLIENT", 14f, Muted, FontStyles.Bold, TextAlignmentOptions.Center);
            Place(_cardRoles[index].rectTransform, new Vector2(0f, -360f), new Vector2(400f, 28f), new Vector2(0.5f, 1f));
            _cardStates[index] = CreateText(inner, "NOT READY", 18f, Red, FontStyles.Bold, TextAlignmentOptions.Center);
            Place(_cardStates[index].rectTransform, new Vector2(0f, -408f), new Vector2(420f, 38f), new Vector2(0.5f, 1f));
        }

        private void CreateHeading(Transform parent, string titleValue, string subtitleValue)
        {
            TMP_Text title = CreateText(parent, titleValue, 38f, Paper, FontStyles.Bold, TextAlignmentOptions.Center);
            StyleDisplayHeading(title);
            Place(title.rectTransform, new Vector2(0f, -35f), new Vector2(900f, 58f), new Vector2(0.5f, 1f));
            TMP_Text subtitle = CreateText(parent, subtitleValue, 18f, new Color(0.84f, 0.98f, 0.90f, 1f), FontStyles.Normal, TextAlignmentOptions.Center);
            StyleColorfulCaption(subtitle);
            Place(subtitle.rectTransform, new Vector2(0f, -95f), new Vector2(900f, 36f), new Vector2(0.5f, 1f));
        }

        private void StyleDisplayHeading(TMP_Text text)
        {
            if (text == null) return;
            if (_config != null && _config.HeadingFont != null) text.font = _config.HeadingFont;
            text.fontStyle = FontStyles.Bold;
            text.characterSpacing = 3f;
            text.enableVertexGradient = true;
            text.colorGradient = new VertexGradient(
                new Color(1f, 1f, 0.78f, 1f),
                new Color(1f, 0.94f, 0.38f, 1f),
                new Color(1f, 0.72f, 0.12f, 1f),
                new Color(1f, 0.56f, 0.08f, 1f));
            text.outlineColor = new Color32(8, 76, 45, 255);
            text.outlineWidth = 0.18f;
        }

        private static void StyleColorfulCaption(TMP_Text text)
        {
            if (text == null) return;
            text.enableVertexGradient = true;
            text.color = Color.white;
            text.colorGradient = new VertexGradient(
                new Color(0.62f, 1f, 0.78f, 1f),
                new Color(1f, 0.94f, 0.40f, 1f),
                new Color(0.30f, 0.90f, 0.86f, 1f),
                new Color(1f, 0.60f, 0.66f, 1f));
            text.outlineColor = new Color32(9, 64, 49, 210);
            text.outlineWidth = 0.08f;
        }

        private RectTransform CreateCard(Transform parent, string name, Vector2 position, Vector2 size, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0.5f, 1f);
            RectTransform card = Rect(name, parent, a, a, position, size, a);
            card.gameObject.AddComponent<Image>().color = Panel;
            return card;
        }

        private void CreateFieldLabel(Transform parent, string value, float y, float x = 50f)
        {
            TMP_Text label = CreateText(parent, value, 13f, Muted, FontStyles.Bold, TextAlignmentOptions.Left);
            Place(label.rectTransform, new Vector2(x, y), new Vector2(540f, 24f), new Vector2(0f, 1f));
        }

        private TMP_InputField CreateInput(Transform parent, string placeholderValue, Vector2 position, float width, bool password = false, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0.5f, 1f);
            RectTransform root = Rect("Input_" + placeholderValue.Replace(" ", string.Empty), parent, a, a, position, new Vector2(width, 60f), a);
            root.gameObject.AddComponent<Image>().color = new Color(0.93f, 0.94f, 0.90f, 0.98f);
            TMP_InputField input = root.gameObject.AddComponent<TMP_InputField>();
            RectTransform viewport = Stretch(new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D)), root);
            viewport.offsetMin = new Vector2(18f, 6f);
            viewport.offsetMax = new Vector2(-18f, -6f);
            TMP_Text placeholder = CreateText(viewport, placeholderValue, 18f, new Color(0.28f, 0.34f, 0.36f, 0.72f), FontStyles.Italic, TextAlignmentOptions.MidlineLeft);
            Stretch(placeholder.rectTransform.gameObject, viewport);
            TMP_Text value = CreateText(viewport, string.Empty, 18f, new Color(0.03f, 0.07f, 0.09f, 1f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            Stretch(value.rectTransform.gameObject, viewport);
            input.textViewport = viewport;
            input.textComponent = value;
            input.placeholder = placeholder;
            input.characterLimit = password ? 0 : 24;
            input.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            input.caretColor = Teal;
            input.selectionColor = new Color(Teal.r, Teal.g, Teal.b, 0.35f);
            if (password) AddPasswordVisibilityToggle(root, viewport, input);
            return input;
        }

        private void AddPasswordVisibilityToggle(RectTransform inputRoot, RectTransform viewport, TMP_InputField input)
        {
            viewport.offsetMax = new Vector2(-112f, -6f);

            RectTransform toggleRoot = Rect(
                "PasswordVisibility",
                inputRoot,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-8f, 0f),
                new Vector2(94f, 42f),
                new Vector2(1f, 0.5f));
            Image graphic = toggleRoot.gameObject.AddComponent<Image>();
            graphic.color = new Color(0.08f, 0.55f, 0.43f, 1f);
            Button toggle = toggleRoot.gameObject.AddComponent<Button>();
            toggle.targetGraphic = graphic;

            ColorBlock colors = toggle.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.14f, 1.14f, 1.14f, 1f);
            colors.pressedColor = new Color(0.72f, 0.78f, 0.75f, 1f);
            toggle.colors = colors;

            TMP_Text label = CreateText(toggleRoot, "SHOW", 13f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            label.gameObject.name = "VisibilityLabel";
            Stretch(label.rectTransform.gameObject, toggleRoot);
            toggle.onClick.AddListener(() =>
                SetPasswordVisibility(input, input.contentType == TMP_InputField.ContentType.Password));
        }

        private static void SetPasswordVisibility(TMP_InputField input, bool visible)
        {
            if (input == null) return;

            input.contentType = visible
                ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;
            input.ForceLabelUpdate();

            Transform toggle = input.transform.Find("PasswordVisibility/VisibilityLabel");
            if (toggle != null && toggle.TryGetComponent(out TMP_Text label))
                label.text = visible ? "HIDE" : "SHOW";
        }

        private Button CreateButton(Transform parent, string label, Color color, Vector2 position, float width, float height, float fontSize, Color? textColor = null, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0.5f, 1f);
            RectTransform root = Rect("Button_" + label.Replace(" ", string.Empty), parent, a, a, position, new Vector2(width, height), a);
            Image graphic = root.gameObject.AddComponent<Image>();
            graphic.color = color;
            Button button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.disabledColor = new Color(0.38f, 0.42f, 0.44f, 0.68f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            TMP_Text text = CreateText(root, label, fontSize, textColor ?? Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            text.characterSpacing = 1.5f;
            Stretch(text.rectTransform.gameObject, root);
            Sprite themedSprite = label switch
            {
                "CREATE ROOM" => _config?.CreateRoomButton,
                "JOIN ROOM" => _config?.JoinRoomButton,
                "START" => _config?.StartButton,
                "SETTINGS" => _config?.SettingsButton,
                "BACK" => _config?.BackButton,
                "CREATE" => _config?.CreateButton,
                "CANCEL" => _config?.CancelButton,
                "JOIN" => _config?.RoomJoinButton,
                "REFRESH" => _config?.RoomRefreshButton,
                "RELOAD" => _config?.RoomRefreshButton,
                "KEY BINDINGS / CONTROLS" => _config?.KeyBindingsButton,
                _ => null
            };
            if (themedSprite != null)
            {
                graphic.sprite = themedSprite;
                graphic.color = Color.white;
                bool dynamicLabel = label is "JOIN" or "REFRESH" or "RELOAD" or "KEY BINDINGS / CONTROLS";
                graphic.preserveAspect = !dynamicLabel;
                if (!dynamicLabel) text.gameObject.SetActive(false);
            }
            UIButtonJuice juice = root.gameObject.AddComponent<UIButtonJuice>();
            juice.hoverScale = 1.035f;
            juice.wiggleIntensity = 0f;
            if (_config != null)
            {
                juice.hoverSFX = _config.HoverSfx;
                juice.clickSFX = _config.ClickSfx;
            }
            _buttons.Add(button);
            return button;
        }

        private static void ApplyButtonArt(Button button, Sprite sprite)
        {
            if (button == null || sprite == null) return;
            Image image = button.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = Color.white;
        }

        private void CreateListLabel(Transform parent, string value, Color color)
        {
            TMP_Text label = CreateText(parent, value, 17f, color, FontStyles.Italic, TextAlignmentOptions.Center);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 70f;
        }

        private void CreateVolumeSlider(Transform parent, string labelValue, string prefKey, float y)
        {
            TMP_Text label = CreateText(parent, labelValue, 15f, Paper, FontStyles.Bold, TextAlignmentOptions.Left);
            Place(label.rectTransform, new Vector2(60f, y), new Vector2(260f, 30f), new Vector2(0f, 1f));
            RectTransform sliderRoot = Rect("Slider_" + labelValue, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(320f, y - 2f), new Vector2(420f, 32f), new Vector2(0f, 1f));
            Slider slider = sliderRoot.gameObject.AddComponent<Slider>();
            RectTransform track = Stretch(new GameObject("Track", typeof(RectTransform), typeof(Image)), sliderRoot);
            track.offsetMin = new Vector2(0f, 10f); track.offsetMax = new Vector2(0f, -10f);
            track.GetComponent<Image>().color = new Color(0.12f, 0.18f, 0.22f, 1f);
            RectTransform fill = Stretch(new GameObject("Fill", typeof(RectTransform), typeof(Image)), track);
            fill.GetComponent<Image>().color = Teal;
            RectTransform handle = Rect("Handle", sliderRoot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(28f, 28f), new Vector2(0.5f, 0.5f));
            Image handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Paper;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.minValue = 0f; slider.maxValue = 1f; slider.value = PlayerPrefs.GetFloat(prefKey, 1f);
            _settingsSliders.Add(slider);
            slider.onValueChanged.AddListener(value =>
            {
                PlayerPrefs.SetFloat(prefKey, value);
                PlayerPrefs.Save();
                EventBus.RaiseSettingsChanged();
            });
        }

        private void SyncSavedNames()
        {
            string saved = PlayerPrefs.GetString(Constants.PlayerPrefsKeys.PLAYER_NAME, string.Empty);
            _createPlayerName?.SetTextWithoutNotify(saved);
            _joinPlayerName?.SetTextWithoutNotify(saved);
        }

        private bool ValidateServices(bool showError = true)
        {
            BindLobbyManager();
            bool valid = _lobbyManager != null && NetworkManager.Singleton != null;
            if (!valid && showError) SetStatus("Lobby services are missing from this scene", Red);
            return valid;
        }

        private bool ValidateNames(string playerName, string roomName)
        {
            if (string.IsNullOrWhiteSpace(playerName)) { SetStatus("Enter your character name", Red); return false; }
            if (string.IsNullOrWhiteSpace(roomName)) { SetStatus("Enter a room name", Red); return false; }
            return true;
        }

        private void SetBusy(bool busy, string message = null)
        {
            if (busy && !_busy)
            {
                GameObject selected = EventSystem.current?.currentSelectedGameObject;
                _selectionBeforeBusy = selected != null ? selected.GetComponent<Selectable>() : null;
            }

            _busy = busy;
            foreach (Button button in _buttons) if (button != null) button.interactable = !busy;
            if (_startButton != null) _startButton.interactable = !busy && CanStartJourney();
            if (!string.IsNullOrEmpty(message)) SetStatus(message, Gold);

            if (!busy && _usingGamepad)
            {
                if (_restoreBusySelectionCoroutine != null) StopCoroutine(_restoreBusySelectionCoroutine);
                _restoreBusySelectionCoroutine = StartCoroutine(
                    RestoreSelectionAfterBusy(_selectionBeforeBusy, _activePanel));
            }
        }

        private IEnumerator RestoreSelectionAfterBusy(Selectable preferred, GameObject expectedPanel)
        {
            yield return null;
            _restoreBusySelectionCoroutine = null;
            _selectionBeforeBusy = null;
            if (_busy) yield break;

            if (_activePanel == expectedPanel && preferred != null && preferred.IsActive() && preferred.IsInteractable())
                Select(preferred);
            else
                FocusActivePanel();
        }

        private void SetStatus(string message, Color color)
        {
            if (_statusText == null) return;
            _statusText.text = message;
            _statusText.enableVertexGradient = false;
            _statusText.outlineWidth = 0f;
            _statusText.color = color;
        }

        private void StartLobbyMusic()
        {
            if (_config == null || _config.LobbyMusic == null) return;
            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.clip = _config.LobbyMusic;
            _musicSource.loop = true;
            _musicSource.playOnAwake = false;
            _musicSource.spatialBlend = 0f;
            ApplyAudioSettings();
            _musicSource.Play();
        }

        private void ApplyAudioSettings()
        {
            if (_musicSource == null) return;
            float master = PlayerPrefs.GetFloat(Constants.PlayerPrefsKeys.MASTER_VOLUME, 1f);
            float music = PlayerPrefs.GetFloat(Constants.PlayerPrefsKeys.BGM_VOLUME, 1f);
            _musicSource.volume = Mathf.Clamp01(master * music * 0.55f);
        }

        private IEnumerator FadeInInterface()
        {
            _shellGroup.alpha = 0f;
            float elapsed = 0f;
            while (elapsed < 0.45f)
            {
                elapsed += Time.unscaledDeltaTime;
                _shellGroup.alpha = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / 0.45f), 3f);
                yield return null;
            }
            _shellGroup.alpha = 1f;
        }

        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        private static string FriendlyError(Exception exception) => exception.InnerException?.Message ?? exception.Message;
        private static void SavePlayerName(string value) { PlayerPrefs.SetString(Constants.PlayerPrefsKeys.PLAYER_NAME, value); PlayerPrefs.Save(); }
        private static string GetRoomCode(LobbyModel lobby) => lobby?.Data != null && lobby.Data.TryGetValue("RoomCode", out DataObject data) ? data.Value : lobby?.LobbyCode ?? "----";
        private static string GetPlayerName(LobbyPlayerModel player) => player?.Data != null && player.Data.TryGetValue("PlayerName", out PlayerDataObject data) && !string.IsNullOrWhiteSpace(data.Value) ? data.Value : "Traveler";
        private static bool IsReady(LobbyPlayerModel player) => player?.Data != null && player.Data.TryGetValue("PlayerReady", out PlayerDataObject data) && data.Value == "1";

        private static TMP_Text CreateText(Transform parent, string value, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI text = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            text.text = value;
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform Stretch(GameObject gameObject, Transform parent)
        {
            RectTransform rect = gameObject.GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2 pivot)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.pivot = pivot;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size, Vector2 anchor)
        {
            rect.anchorMin = anchor; rect.anchorMax = anchor; rect.pivot = anchor;
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }

        private static void Select(Selectable selectable)
        {
            if (selectable == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
            if (selectable is TMP_InputField input) input.ActivateInputField();
        }

        private static void EnsureEventSystem()
        {
            EventSystem current = EventSystem.current;
            if (current != null)
            {
                current.enabled = true;
                InputSystemUIInputModule module = current.GetComponent<InputSystemUIInputModule>();
                if (module != null) module.enabled = true;
                return;
            }

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
