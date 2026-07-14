using System;
using System.Collections.Generic;
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
    private VisualElement _actionsPanel;
    private VisualElement _settingsPanel;
    private Button _continueButton;
    private Button _teleportButton;
    private Button _settingsButton;
    private Button _controlsButton;
    private Button _quitButton;
    private Button _settingsBackButton;
    private Label _pauseHint;
    private Slider _masterVolumeSlider;
    private Slider _musicVolumeSlider;
    private Slider _sfxVolumeSlider;
    private Toggle _cameraShakeToggle;

    private bool _isOpen;
    private bool _childMenuOpen;
    private Button _lastFocusedButton;
    private static readonly Dictionary<PlayerInputHandler, bool> CameraLookStates = new();

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
        EventBus.OnInputDeviceChanged -= OnInputDeviceChanged;

        if (_continueButton != null) _continueButton.clicked -= Resume;
        if (_teleportButton != null) _teleportButton.clicked -= OpenTeleportUI;
        if (_settingsButton != null) _settingsButton.clicked -= ShowSettingsPanel;
        if (_controlsButton != null) _controlsButton.clicked -= OpenInputSettings;
        if (_quitButton != null) _quitButton.clicked -= QuitToMainMenu;
        if (_settingsBackButton != null) _settingsBackButton.clicked -= HideSettingsPanel;
    }

    private void Update()
    {
        if (IsBlockedByScene() || IsInputSettingsTakingFocus() || _childMenuOpen) return;

        if (_isOpen && WasBackPressed())
        {
            if (IsSettingsPanelVisible())
            {
                HideSettingsPanel();
                return;
            }

            Resume();
            return;
        }

        if (WasPausePressed())
        {
            if (_isOpen && IsSettingsPanelVisible())
            {
                HideSettingsPanel();
                return;
            }

            Toggle();
        }
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
        _childMenuOpen = false;
        _overlay?.RemoveFromClassList("hidden");
        HideSettingsPanel();
        RefreshInputHints();
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
        bool wasOpen = _isOpen;
        if (!wasOpen && resumeGame) return;

        _isOpen = false;
        _childMenuOpen = false;
        _overlay?.AddToClassList("hidden");
        HideSettingsPanel();

        if (!wasOpen) return;

        UICursorLockService.Release(this);
        LockPlayerInput(false);

        if (resumeGame)
            TransitionToPlayingState();
    }

    private void BindUI()
    {
        var root = _uiDocument.rootVisualElement;
        _overlay = root.Q<VisualElement>("pause-overlay");
        _actionsPanel = root.Q<VisualElement>("pause-actions");
        _settingsPanel = root.Q<VisualElement>("settings-panel");
        _continueButton = root.Q<Button>("btn-continue");
        _teleportButton = root.Q<Button>("btn-teleport");
        _settingsButton = root.Q<Button>("btn-settings");
        _controlsButton = root.Q<Button>("btn-controls");
        _quitButton = root.Q<Button>("btn-quit");
        _settingsBackButton = root.Q<Button>("btn-settings-back");
        _pauseHint = root.Q<Label>("pause-hint");
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
        EventBus.OnInputDeviceChanged += OnInputDeviceChanged;

        BindSettingsControls();
        RefreshInputHints();
    }

    private void BindSettingsControls()
    {
        SetSliderValue(_masterVolumeSlider, Constants.PlayerPrefsKeys.MASTER_VOLUME, 1f);
        SetSliderValue(_musicVolumeSlider, Constants.PlayerPrefsKeys.BGM_VOLUME, 1f);
        SetSliderValue(_sfxVolumeSlider, Constants.PlayerPrefsKeys.SFX_VOLUME, 1f);

        bool cameraShake = PlayerPrefs.GetInt(Constants.PlayerPrefsKeys.ACCESSIBILITY_CAMERA_SHAKE, 1) == 1;
        _cameraShakeToggle?.SetValueWithoutNotify(cameraShake);

        _masterVolumeSlider?.RegisterValueChangedCallback(evt => SaveFloatSetting(Constants.PlayerPrefsKeys.MASTER_VOLUME, evt.newValue));
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
        _actionsPanel?.AddToClassList("hidden");
        _settingsPanel?.RemoveFromClassList("hidden");
        _masterVolumeSlider?.Focus();
    }

    private void HideSettingsPanel()
    {
        _settingsPanel?.AddToClassList("hidden");
        _actionsPanel?.RemoveFromClassList("hidden");
        if (_isOpen)
            _continueButton?.Focus();
    }

    private bool IsSettingsPanelVisible()
    {
        return _settingsPanel != null && !_settingsPanel.ClassListContains("hidden");
    }

    private void OpenInputSettings()
    {
        _inputSettingsPanel ??= FindFirstObjectByType<InputSettingsPanelController>();
        if (_inputSettingsPanel == null) return;

        OpenChildMenu(_controlsButton, () => _inputSettingsPanel.Show(ReturnFromChildMenu, managePlayerInput: false));
    }

    private void OpenTeleportUI()
    {
        if (TeleportManager.Instance == null)
        {
            Debug.LogWarning("[PauseMenuController] TeleportManager is not available in this scene.");
            return;
        }

        OpenChildMenu(_teleportButton, () => TeleportManager.Instance.ShowUI(ReturnFromChildMenu, managePlayerInput: false));
    }

    private void OpenChildMenu(Button sourceButton, Action openChild)
    {
        if (!_isOpen || openChild == null) return;

        _lastFocusedButton = sourceButton;
        _childMenuOpen = true;
        HideSettingsPanel();
        _overlay?.AddToClassList("hidden");
        openChild.Invoke();
    }

    private void ReturnFromChildMenu()
    {
        if (!_isOpen) return;

        _childMenuOpen = false;
        RefreshInputHints();
        _overlay?.RemoveFromClassList("hidden");
        (_lastFocusedButton ?? _continueButton)?.Focus();
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

    private bool WasBackPressed()
    {
        return Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
    }

    private bool IsInputSettingsTakingFocus()
    {
        return _inputSettingsPanel != null && (_inputSettingsPanel.IsVisible || _inputSettingsPanel.IsRebinding);
    }

    private void OnInputDeviceChanged(InputDeviceType _)
    {
        RefreshInputHints();
    }

    private void RefreshInputHints()
    {
        if (_pauseHint == null) return;

        bool gamepad = InputDeviceDetector.Instance != null
            && InputDeviceDetector.Instance.CurrentDeviceType == InputDeviceType.Gamepad;
        _pauseHint.text = gamepad ? "Menu / B" : "Esc";
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
            {
                if (!CameraLookStates.ContainsKey(handler))
                    CameraLookStates.Add(handler, handler.CameraLookEnabled);
                handler.LockAllInput();
                handler.DisableCameraLook();
            }
            else
            {
                handler.UnlockAllInput();
                if (CameraLookStates.TryGetValue(handler, out bool wasEnabled) && wasEnabled)
                    handler.EnableCameraLook();
                else
                    handler.DisableCameraLook();

                CameraLookStates.Remove(handler);
            }
        }
    }
}
