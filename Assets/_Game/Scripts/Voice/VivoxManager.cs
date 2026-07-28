using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;

public class VivoxManager : MonoBehaviour
{
    public static VivoxManager Instance { get; private set; }

    [Header("Vivox Settings")]
    [SerializeField] private string _channelName = "MainLobby";
    [SerializeField] private bool _autoLogin = true;

    [Header("3D Audio Settings")]
    [Range(1, 500)]
    [SerializeField] private int _audibleDistance = 100;
    [Range(1, 100)]
    [SerializeField] private int _conversationalDistance = 10;
    [Range(0.1f, 4.0f)]
    [SerializeField] private float _rolloff = 1.0f;
    [SerializeField] private AudioFadeModel _distanceModel =
        AudioFadeModel.LinearByDistance;

    [Header("Volume Settings")]
    [Range(-50, 50)]
    [SerializeField] private int _masterVolume = 0;
    [Range(-50, 50)]
    [SerializeField] private int _micGain = 0;
    [Range(0.0001f, 0.1f)]
    [SerializeField] private float _vadThreshold = 0.001f;

    public int MasterVolume => _masterVolume;
    public int MicGain => _micGain;
    public float VADThreshold => _vadThreshold;

    private bool _isInitialized;
    private bool _isLoggedIn;
    private string _joinedChannelName;
    private Task _initializeTask;
    private Task _loginTask;
    private bool _sessionOperationInProgress;
    private bool _wasNetworkListening;
    private bool _joinedChannelIsPositional;
    private float _nextPositionUpdate;
    private bool _voiceLoginBlocked;
    private float _nextLoginAttemptTime;

    private const float LoginRetryCooldownSeconds = 10f;

    public bool IsLoggedIn => _isLoggedIn;
    public bool IsVoiceLoginBlocked => _voiceLoginBlocked;
    public bool IsCurrentChannelPositional => _joinedChannelIsPositional;
    public string JoinedChannelName => _joinedChannelName;
    public string DefaultChannelName => _channelName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<VivoxManager>() != null) return;
        new GameObject("VivoxManager").AddComponent<VivoxManager>();
    }

    public void SetChannelName(string newName)
    {
        if (string.IsNullOrEmpty(newName)) return;
        _channelName = newName;
        Debug.Log($"[VivoxManager] Channel name updated to: {newName}");
    }

    private void OnValidate()
    {
        if (Application.isPlaying && _isLoggedIn)
        {
            ApplyVolumeSettings();
        }
    }

    private void ApplyVolumeSettings()
    {
        VivoxService.Instance.SetInputDeviceVolume(_micGain);
        VivoxService.Instance.SetOutputDeviceVolume(_masterVolume);
    }

    private async void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Ensure app runs in background for voice to stay active
        Application.runInBackground = true;

        // Request microphone permission at startup
        await RequestMicrophonePermission();

        if (_autoLogin)
        {
            await InitializeAsync();
        }
    }

    private void Update()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        bool networkListening = networkManager != null && networkManager.IsListening;

        if (networkListening)
        {
            if (HasRoomChannel() &&
                (_joinedChannelName != _channelName || _joinedChannelIsPositional) &&
                !_sessionOperationInProgress)
                _ = ConnectToRoomVoiceAsync();

            if (_joinedChannelIsPositional && !string.IsNullOrEmpty(_joinedChannelName) && Time.unscaledTime >= _nextPositionUpdate)
            {
                _nextPositionUpdate = Time.unscaledTime + 0.1f;
                UpdateLocal3DPosition(networkManager);
            }
        }
        else if (_wasNetworkListening && !string.IsNullOrEmpty(_joinedChannelName) && !_sessionOperationInProgress)
        {
            _ = DisconnectFromRoomVoiceAsync();
        }

        _wasNetworkListening = networkListening;
    }

    private bool HasRoomChannel()
    {
        return !string.IsNullOrWhiteSpace(_channelName) &&
               !string.Equals(_channelName, "MainLobby", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ConnectToRoomVoiceAsync()
    {
        if (_voiceLoginBlocked || Time.unscaledTime < _nextLoginAttemptTime)
            return;

        _sessionOperationInProgress = true;
        try
        {
            string displayName = PlayerPrefs.GetString(Constants.PlayerPrefsKeys.PLAYER_NAME, "Traveler");
            await LoginAsync(displayName);

            NetworkManager networkManager = NetworkManager.Singleton;
            if (!_isLoggedIn || networkManager == null || !networkManager.IsListening || !HasRoomChannel()) return;

            string requestedChannel = _channelName;
            await JoinChannelAsync(requestedChannel, false);

            networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
                await LeaveChannelAsync();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[VivoxManager] Room voice connection failed: {exception.Message}");
        }
        finally
        {
            _sessionOperationInProgress = false;
        }
    }

    private async Task DisconnectFromRoomVoiceAsync()
    {
        _sessionOperationInProgress = true;
        try
        {
            await LeaveChannelAsync();
        }
        finally
        {
            _sessionOperationInProgress = false;
        }
    }

    private void UpdateLocal3DPosition(NetworkManager networkManager)
    {
        if (!_isLoggedIn || string.IsNullOrEmpty(_joinedChannelName)) return;

        NetworkObject playerObject = networkManager.LocalClient?.PlayerObject;
        Transform playerTransform = playerObject != null ? playerObject.transform : null;
        if (playerTransform == null) return;

        Transform listener = Camera.main != null ? Camera.main.transform : playerTransform;
        Vector3 speakerPosition = playerTransform.position + Vector3.up * 1.5f;

        try
        {
            VivoxService.Instance.Set3DPosition(
                speakerPosition,
                listener.position,
                listener.forward,
                listener.up,
                _joinedChannelName);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[VivoxManager] Could not update voice position: {exception.Message}");
        }
    }

    private async Task RequestMicrophonePermission()
    {
#if UNITY_ANDROID
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
        }
#elif UNITY_IOS
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            await Application.RequestUserAuthorization(UserAuthorization.Microphone);
        }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // On Windows, checking Microphone.devices triggers the OS to acknowledge the app's intent to use the mic.
        string[] devices = Microphone.devices;
        if (devices.Length > 0)
        {
            Debug.Log($"[VivoxManager] Microphone devices found: {string.Join(", ", devices)}");
            
            // "Poke" the microphone to ensure the system recognizes the app is using it.
            // This is similar to what Discord does to 'wake up' the audio recorder.
            try
            {
                string device = devices[0];
                Microphone.GetDeviceCaps(device, out int minFreq, out int maxFreq);
                AudioClip tempClip = Microphone.Start(device, false, 1, 44100);
                Microphone.End(device);
                Debug.Log("[VivoxManager] Microphone 'poke' successful.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VivoxManager] Microphone 'poke' failed: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning("[VivoxManager] No microphone devices found! Please ensure your microphone is plugged in and allowed in Windows Privacy Settings.");
        }

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            var asyncOp = Application.RequestUserAuthorization(UserAuthorization.Microphone);
            while (!asyncOp.isDone) await Task.Yield();
        }
#endif
        await Task.CompletedTask;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        
        if (_initializeTask != null && !_initializeTask.IsFaulted && !_initializeTask.IsCompleted)
        {
            await _initializeTask;
            return;
        }

        _initializeTask = InternalInitializeAsync();
        await _initializeTask;
    }

    private async Task InternalInitializeAsync()
    {
        try
        {
            Debug.Log("[VivoxManager] Initializing Unity Services and Vivox...");
            
            await UgsServiceBootstrap.InitializeAsync();

            // Kiểm tra lại lần nữa trước khi init Vivox
            if (UnityServices.State == ServicesInitializationState.Initialized)
            {
                await VivoxService.Instance.InitializeAsync();
                _isInitialized = true;
                Debug.Log("[VivoxManager] Vivox Service initialized successfully.");
            }
            else
            {
                throw new Exception($"Unity Services failed to initialize. Current state: {UnityServices.State}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[VivoxManager] Initialization failed: {e.Message}");
            _initializeTask = null; // Reset để có thể thử lại
            throw;
        }
    }

    public async Task LoginAsync(string displayName = null)
    {
        if (_isLoggedIn || _voiceLoginBlocked || Time.unscaledTime < _nextLoginAttemptTime) return;

        // Keep the wrapper state in sync when Unity Services survives a Play Mode restart.
        if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
        {
            _isLoggedIn = true;
            return;
        }

        // Đảm bảo đã initialized trước khi login
        try 
        {
            await InitializeAsync();
        }
        catch (Exception e)
        {
            Debug.LogError($"[VivoxManager] Cannot login: Initialization failed. {e.Message}");
            return;
        }

        if (_loginTask != null && !_loginTask.IsFaulted && !_loginTask.IsCompleted)
        {
            await _loginTask;
            return;
        }

        _loginTask = InternalLoginAsync(displayName);
        await _loginTask;
    }

    private async Task InternalLoginAsync(string displayName)
    {
        try
        {
            // Kiểm tra Authentication
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Debug.Log("[VivoxManager] Waiting for Authentication (Anonymous Sign-In)...");
                
                // Nếu chưa sign in, thử sign in luôn nếu AuthManager chưa làm
                if (UnityServices.State == ServicesInitializationState.Initialized)
                {
                    try 
                    {
                        await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    }
                    catch (Exception authEx)
                    {
                        Debug.LogWarning($"[VivoxManager] Manual sign-in attempt failed: {authEx.Message}. Waiting for AuthManager instead...");
                    }
                }

                int timeout = 0;
                while (!AuthenticationService.Instance.IsSignedIn && timeout < 50)
                {
                    await Task.Delay(200);
                    timeout++;
                }
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                throw new Exception("Authentication timeout: User must be signed in to use Vivox.");
            }

            string playerId = AuthenticationService.Instance.PlayerId;
            string finalDisplayName = displayName ?? $"Player_{playerId.Substring(0, Mathf.Min(5, playerId.Length))}";

            Debug.Log($"[VivoxManager] Logging into Vivox. PlayerId: {playerId}, DisplayName: {finalDisplayName}");

            LoginOptions options = new LoginOptions
            {
                DisplayName = finalDisplayName,
                ParticipantUpdateFrequency = ParticipantPropertyUpdateFrequency.TenPerSecond
            };

            await VivoxService.Instance.LoginAsync(options);
            _isLoggedIn = true;
            
            ApplyVolumeSettings();
            SetMicrophoneMute(VoiceInputController.IsMutedByUser);
            
            Debug.Log($"[VivoxManager] Logged in successfully to Vivox as {playerId}.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[VivoxManager] Login failed: {e.Message}");
            _isLoggedIn = false;
            _loginTask = null; // Reset để có thể thử lại
            _nextLoginAttemptTime = Time.unscaledTime + LoginRetryCooldownSeconds;

            string error = e.ToString();
            if (error.Contains("Invalid Access Token Signature", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("Access Token Expired", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("(20122)", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("(20121)", StringComparison.OrdinalIgnoreCase))
            {
                _voiceLoginBlocked = true;
                Debug.LogError("[VivoxManager] Voice login disabled for this run because Vivox credentials are invalid. Update Project Settings and restart the game.");
            }
        }
    }

    public async Task JoinChannelAsync(string channelName, bool isPositional = true)
    {
        if (!_isLoggedIn)
        {
            await LoginAsync();
        }

        if (!_isLoggedIn)
        {
            Debug.LogError("[VivoxManager] Cannot join channel: Login failed.");
            return;
        }

        // Nếu đang ở trong một channel khác, hãy thoát ra trước
        if (!string.IsNullOrEmpty(_joinedChannelName) &&
            (_joinedChannelName != channelName || _joinedChannelIsPositional != isPositional))
        {
            Debug.Log($"[VivoxManager] Leaving current channel {_joinedChannelName} to join {channelName}");
            await LeaveChannelAsync();
        }

        if (_joinedChannelName == channelName && _joinedChannelIsPositional == isPositional)
        {
            Debug.Log($"[VivoxManager] Already in channel: {channelName}");
            return;
        }

        try
        {
            Debug.Log($"[VivoxManager] Joining channel: {channelName} (Positional: {isPositional})...");
            if (isPositional)
            {
                Channel3DProperties properties = new Channel3DProperties(_audibleDistance, _conversationalDistance, _rolloff, _distanceModel);
                await VivoxService.Instance.JoinPositionalChannelAsync(channelName, ChatCapability.AudioOnly, properties);
            }
            else
            {
                await VivoxService.Instance.JoinGroupChannelAsync(channelName, ChatCapability.AudioOnly);
            }

            _joinedChannelName = channelName;
            _joinedChannelIsPositional = isPositional;
            Debug.Log($"[VivoxManager] Successfully joined channel: {channelName}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[VivoxManager] Join channel failed: {e.Message}");
            _joinedChannelName = null;
            _joinedChannelIsPositional = false;
        }
    }

    public async Task LeaveChannelAsync()
    {
        if (string.IsNullOrEmpty(_joinedChannelName)) return;

        try
        {
            await VivoxService.Instance.LeaveChannelAsync(_joinedChannelName);
            Debug.Log($"[VivoxManager] Left channel: {_joinedChannelName}");
            _joinedChannelName = null;
            _joinedChannelIsPositional = false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[VivoxManager] Leave channel failed: {e.Message}");
        }
    }

    public void SetMicrophoneMute(bool mute)
    {
        if (!_isLoggedIn) return;
        
        if (mute)
            VivoxService.Instance.MuteInputDevice();
        else
            VivoxService.Instance.UnmuteInputDevice();
        
        Debug.Log($"[VivoxManager] Microphone {(mute ? "muted" : "unmuted")}");
    }

    public bool IsMicrophoneMuted()
    {
        return _isLoggedIn && VivoxService.Instance.IsInputDeviceMuted;
    }
}
