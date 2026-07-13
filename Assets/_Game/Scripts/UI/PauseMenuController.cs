using Game.Testing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Local pause/menu layer for co-op sessions. It locks local player input and
/// leaves network simulation/time scale untouched.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private UIDocument _uiDocument;
    [SerializeField] private InputSettingsPanelController _inputSettingsPanel;
    [SerializeField] private GameStateMachine _gameStateMachine;

    [Header("Scene Rules")]
    [SerializeField] private bool _disableInLobby = true;
    [SerializeField] private string _lobbySceneName = Constants.Scenes.LOBBY;

    private VisualElement _overlay;
    private VisualElement _settingsPanel;
    private Button _continueButton;
    private Button _teleportButton;
    private Button _settingsButton;
    private Button _controlsButton;
    private Button _quitButton;
    private Button _settingsBackButton;
    private Slider _masterVolumeSlider;
    private Slider _musicVolumeSlider;
    private Slider _sfxVolumeSlider;
    private Toggle _cameraShakeToggle;

    private bool _isOpen;

    public bool IsOpen => _isOpen;

    private void Awake()
    {
        _uiDocument ??= GetComponent<UIDocument>();
        _inputSettingsPanel ??= FindFirstObjectByType<InputSettingsPanelController>();
        _gameStateMachine ??= FindFirstObjectByType<GameStateMachine>();

        if (_uiDocument == null)
        {
            Debug.LogError("[PauseMenuController] UIDocument is not assigned.");
            enabled = false;
            return;
        }

        BindUI();
        Hide(false);
    }

    private void OnDestroy()
    {
        if (_continueButton != null) _continueButton.clicked -= Resume;
        if (_teleportButton != null) _teleportButton.clicked -= OpenTeleportUI;
        if (_settingsButton != null) _settingsButton.clicked -= ShowSettingsPanel;
        if (_controlsButton != null) _controlsButton.clicked -= OpenInputSettings;
        if (_quitButton != null) _quitButton.clicked -= QuitToMainMenu;
        if (_settingsBackButton != null) _settingsBackButton.clicked -= HideSettingsPanel;
    }

    private void Update()
    {
        if (IsBlockedByScene() || IsInputSettingsTakingFocus()) return;

        if (WasPausePressed())
            Toggle();
    }

    public void Toggle()
    {
        if (_isOpen) Resume();
        else Show();
    }

    public void Show()
    {
        if (_isOpen || IsBlockedByScene()) return;

        _isOpen = true;
        _overlay?.RemoveFromClassList("hidden");
        HideSettingsPanel();
        LockPlayerInput(true);
        UICursorLockService.Request(this);
        TransitionToPauseState();
        _continueButton?.Focus();
    }

    public void Resume()
    {
        Hide(true);
    }

    private void Hide(bool resumeGame)
    {
        if (!_isOpen && resumeGame) return;

        _isOpen = false;
        _overlay?.AddToClassList("hidden");
        HideSettingsPanel();
        UICursorLockService.Release(this);
        LockPlayerInput(false);

        if (resumeGame)
            TransitionToPlayingState();
    }

    private void BindUI()
    {
        var root = _uiDocument.rootVisualElement;
        _overlay = root.Q<VisualElement>("pause-overlay");
        _settingsPanel = root.Q<VisualElement>("settings-panel");
        _continueButton = root.Q<Button>("btn-continue");
        _teleportButton = root.Q<Button>("btn-teleport");
        _settingsButton = root.Q<Button>("btn-settings");
        _controlsButton = root.Q<Button>("btn-controls");
        _quitButton = root.Q<Button>("btn-quit");
        _settingsBackButton = root.Q<Button>("btn-settings-back");
        _masterVolumeSlider = root.Q<Slider>("slider-master-volume");
        _musicVolumeSlider = root.Q<Slider>("slider-music-volume");
        _sfxVolumeSlider = root.Q<Slider>("slider-sfx-volume");
        _cameraShakeToggle = root.Q<Toggle>("toggle-camera-shake");

        _continueButton.clicked += Resume;
        _teleportButton.clicked += OpenTeleportUI;
        _settingsButton.clicked += ShowSettingsPanel;
        _controlsButton.clicked += OpenInputSettings;
        _quitButton.clicked += QuitToMainMenu;
        _settingsBackButton.clicked += HideSettingsPanel;

        BindSettingsControls();
    }

    private void BindSettingsControls()
    {
        SetSliderValue(_masterVolumeSlider, "pause_master_volume", 1f);
        SetSliderValue(_musicVolumeSlider, Constants.PlayerPrefsKeys.BGM_VOLUME, 1f);
        SetSliderValue(_sfxVolumeSlider, Constants.PlayerPrefsKeys.SFX_VOLUME, 1f);

        bool cameraShake = PlayerPrefs.GetInt(Constants.PlayerPrefsKeys.ACCESSIBILITY_CAMERA_SHAKE, 1) == 1;
        _cameraShakeToggle?.SetValueWithoutNotify(cameraShake);

        _masterVolumeSlider?.RegisterValueChangedCallback(evt => SaveFloatSetting("pause_master_volume", evt.newValue));
        _musicVolumeSlider?.RegisterValueChangedCallback(evt => SaveFloatSetting(Constants.PlayerPrefsKeys.BGM_VOLUME, evt.newValue));
        _sfxVolumeSlider?.RegisterValueChangedCallback(evt => SaveFloatSetting(Constants.PlayerPrefsKeys.SFX_VOLUME, evt.newValue));
        _cameraShakeToggle?.RegisterValueChangedCallback(evt =>
        {
            PlayerPrefs.SetInt(Constants.PlayerPrefsKeys.ACCESSIBILITY_CAMERA_SHAKE, evt.newValue ? 1 : 0);
            PlayerPrefs.Save();
            EventBus.RaiseAccessibilityChanged();
        });
    }

    private static void SetSliderValue(Slider slider, string key, float fallback)
    {
        slider?.SetValueWithoutNotify(PlayerPrefs.GetFloat(key, fallback));
    }

    private static void SaveFloatSetting(string key, float value)
    {
        PlayerPrefs.SetFloat(key, Mathf.Clamp01(value));
        PlayerPrefs.Save();
        EventBus.RaiseSettingsChanged();
    }

    private void ShowSettingsPanel()
    {
        _settingsPanel?.RemoveFromClassList("hidden");
        _masterVolumeSlider?.Focus();
    }

    private void HideSettingsPanel()
    {
        _settingsPanel?.AddToClassList("hidden");
        if (_isOpen)
            _continueButton?.Focus();
    }

    private void OpenInputSettings()
    {
        _inputSettingsPanel ??= FindFirstObjectByType<InputSettingsPanelController>();
        _inputSettingsPanel?.Show();
    }

    private void OpenTeleportUI()
    {
        if (TeleportManager.Instance == null)
        {
            Debug.LogWarning("[PauseMenuController] TeleportManager is not available in this scene.");
            return;
        }

        TeleportManager.Instance.ShowUI();
    }

    private void QuitToMainMenu()
    {
        Hide(false);

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadMainMenu();
        else if (SceneLoader.CanLoadScene(Constants.Scenes.MAIN_MENU))
            SceneManager.LoadScene(Constants.Scenes.MAIN_MENU);
    }

    private bool WasPausePressed()
    {
        bool keyboard = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool gamepad = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
        return keyboard || gamepad;
    }

    private bool IsInputSettingsTakingFocus()
    {
        return _inputSettingsPanel != null && (_inputSettingsPanel.IsVisible || _inputSettingsPanel.IsRebinding);
    }

    private bool IsBlockedByScene()
    {
        return _disableInLobby
            && !string.IsNullOrEmpty(_lobbySceneName)
            && SceneManager.GetActiveScene().name == _lobbySceneName;
    }

    private void TransitionToPauseState()
    {
        _gameStateMachine ??= FindFirstObjectByType<GameStateMachine>();
        if (_gameStateMachine != null && _gameStateMachine.CurrentState == GameState.Playing)
            _gameStateMachine.TransitionTo(GameState.Paused);
        else
            EventBus.RaiseGamePaused();
    }

    private void TransitionToPlayingState()
    {
        _gameStateMachine ??= FindFirstObjectByType<GameStateMachine>();
        if (_gameStateMachine != null && _gameStateMachine.CurrentState == GameState.Paused)
            _gameStateMachine.TransitionTo(GameState.Playing);
        else
            EventBus.RaiseGameResumed();
    }

    private static void LockPlayerInput(bool locked)
    {
        foreach (var handler in FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None))
        {
            if (!handler.IsOwner) continue;

            if (locked)
                handler.LockAllInput();
            else
                handler.UnlockAllInput();
        }
    }
}
