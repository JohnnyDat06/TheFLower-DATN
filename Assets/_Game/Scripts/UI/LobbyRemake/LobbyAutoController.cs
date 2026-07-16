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

        [SerializeField] private string _gameSceneName = Constants.Scenes.LEVEL_01;

        private readonly List<Button> _buttons = new();
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
        private RectTransform _browserList;
        private GameObject _passwordPromptPanel;
        private Button _readyButton;
        private Button _startButton;
        private Image[] _cardBorders = new Image[2];
        private TMP_Text[] _cardNames = new TMP_Text[2];
        private TMP_Text[] _cardRoles = new TMP_Text[2];
        private TMP_Text[] _cardStates = new TMP_Text[2];
        private Image[] _cardAvatars = new Image[2];

        private bool _busy;
        private bool _localReady;
        private float _refreshTimer;
        private string _lastRosterSignature;

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
            BuildInterface();
            StartLobbyMusic();
            ShowLanding();
            StartCoroutine(FadeInInterface());
        }

        private void Start() => BindLobbyManager();

        private void OnEnable()
        {
            EventBus.OnClientConnected += HandleNetworkChanged;
            EventBus.OnClientDisconnected += HandleNetworkChanged;
            EventBus.OnSettingsChanged += ApplyAudioSettings;
            BindLobbyManager();
        }

        private void OnDisable()
        {
            EventBus.OnClientConnected -= HandleNetworkChanged;
            EventBus.OnClientDisconnected -= HandleNetworkChanged;
            EventBus.OnSettingsChanged -= ApplyAudioSettings;
            UnbindLobbyManager();
        }

        private void Update()
        {
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = 0.25f;
            BindLobbyManager();
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

        private void ShowCreate()
        {
            SyncSavedNames();
            SetPasswordVisibility(_createPassword, false);
            ShowPanel(_createPanel);
            SetStatus("Create a public room. Password is optional.", Paper);
            Select(_createPlayerName);
        }

        private void ShowJoin()
        {
            SyncSavedNames();
            SetPasswordVisibility(_joinPassword, false);
            HidePasswordPrompt();
            ShowPanel(_joinPanel);
            SetStatus("Join by exact room name or choose an open room", Paper);
            Select(_joinPlayerName);
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
            Select(_readyButton);
        }

        private void ShowPanel(GameObject panel)
        {
            foreach (GameObject candidate in new[] { _landingPanel, _modePanel, _createPanel, _joinPanel, _roomPanel, _settingsPanel })
                if (candidate != null) candidate.SetActive(candidate == panel);
            _activePanel = panel;
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
                bool soloLobby = (_currentLobby?.Players?.Count ?? 0) == 1;
                SetStatus(next
                    ? (soloLobby ? "Ready - solo start enabled" : "Ready - waiting for your companion")
                    : "Not ready", next ? Green : Red);
            }
            catch (Exception exception) { SetStatus(FriendlyError(exception), Red); }
            finally { SetBusy(false); }
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
            _startButton.interactable = canStart;
            _startButtonText.text = canStart
                ? (players.Count == 1 ? "START SOLO" : "START GAME")
                : "READY UP TO START";
            ApplyButtonArt(_startButton, canStart ? _config?.RoomStartButton : _config?.RoomWaitingButton);
        }

        private bool CanStartJourney()
        {
            NetworkManager manager = NetworkManager.Singleton;
            int lobbyPlayerCount = _currentLobby?.Players?.Count ?? 0;
            int connectedPlayerCount = manager?.ConnectedClientsIds.Count ?? 0;
            return manager != null
                && manager.IsHost
                && lobbyPlayerCount >= 1
                && lobbyPlayerCount <= 2
                && connectedPlayerCount == lobbyPlayerCount
                && _currentLobby.Players.All(IsReady);
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
            HidePasswordPrompt();
            for (int i = _browserList.childCount - 1; i >= 0; i--) Destroy(_browserList.GetChild(i).gameObject);

            if (rooms.Count == 0)
            {
                CreateListLabel(_browserList, "No rooms are currently open", Muted);
                return;
            }

            foreach (LobbyModel room in rooms)
            {
                string lockText = room.HasPassword ? "  [PASSWORD]" : string.Empty;
                Button row = CreateButton(_browserList, $"{room.Name}{lockText}    {room.Players.Count}/{room.MaxPlayers}", PanelSoft, Vector2.zero, 590f, 70f, 16f);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 70f;
                row.onClick.AddListener(() => SelectRoom(room, row));
            }
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

            RectTransform browser = CreateCard(panel, "RoomBrowser", new Vector2(-2f, -120f), new Vector2(680f, 570f), new Vector2(1f, 1f));
            TMP_Text browserTitle = CreateText(browser, "OPEN ROOMS", 20f, Gold, FontStyles.Bold, TextAlignmentOptions.Left);
            Place(browserTitle.rectTransform, new Vector2(30f, -28f), new Vector2(300f, 36f), new Vector2(0f, 1f));
            Button refresh = CreateButton(browser, "REFRESH", PanelSoft, new Vector2(-30f, -22f), 180f, 48f, 14f, null, new Vector2(1f, 1f));
            refresh.onClick.AddListener(RefreshRoomBrowser);
            _browserList = Rect("RoomList", browser, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -92f), new Vector2(-60f, 470f), new Vector2(0f, 1f));
            VerticalLayoutGroup listLayout = _browserList.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 10f;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = false;
            listLayout.childForceExpandWidth = true;
            Button back = CreateButton(panel, "BACK", PanelSoft, new Vector2(0f, 8f), 250f, 52f, 15f, null, new Vector2(0.5f, 0f));
            back.onClick.AddListener(ShowModeSelection);

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
            return panel.gameObject;
        }

        private GameObject CreateSettingsPanel(RectTransform parent)
        {
            RectTransform panel = Stretch(new GameObject("SettingsPanel", typeof(RectTransform)), parent);
            CreateHeading(panel, "SETTINGS", "Changes apply to lobby and gameplay");
            RectTransform card = CreateCard(panel, "SettingsCard", new Vector2(0f, -170f), new Vector2(820f, 500f));
            CreateVolumeSlider(card, "MASTER VOLUME", Constants.PlayerPrefsKeys.MASTER_VOLUME, -65f);
            CreateVolumeSlider(card, "MUSIC VOLUME", Constants.PlayerPrefsKeys.BGM_VOLUME, -165f);
            CreateVolumeSlider(card, "SFX VOLUME", Constants.PlayerPrefsKeys.SFX_VOLUME, -265f);
            Button controls = CreateButton(card, "KEY BINDINGS / CONTROLS", Teal, new Vector2(0f, -370f), 620f, 100f, 18f);
            controls.onClick.AddListener(OpenControls);
            Button back = CreateButton(panel, "BACK", PanelSoft, new Vector2(0f, -700f), 360f, 62f, 16f);
            back.onClick.AddListener(ShowLanding);
            return panel.gameObject;
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
                "KEY BINDINGS / CONTROLS" => _config?.KeyBindingsButton,
                _ => null
            };
            if (themedSprite != null)
            {
                graphic.sprite = themedSprite;
                graphic.color = Color.white;
                bool dynamicLabel = label is "JOIN" or "REFRESH" or "KEY BINDINGS / CONTROLS";
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
            _busy = busy;
            foreach (Button button in _buttons) if (button != null) button.interactable = !busy;
            if (_startButton != null) _startButton.interactable = !busy && CanStartJourney();
            if (!string.IsNullOrEmpty(message)) SetStatus(message, Gold);
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
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        }
    }
}
