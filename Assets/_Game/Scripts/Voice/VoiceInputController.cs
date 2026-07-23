using UnityEngine;
using UnityEngine.InputSystem;

public class VoiceInputController : MonoBehaviour
{
    private const string MicMutedPreferenceKey = "Vivox.MicMuted";
    private static VoiceInputController _instance;
    private PlayerInputHandler _inputHandler;

    public static bool IsMutedByUser { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<VoiceInputController>() != null) return;
        new GameObject("VoiceInputController").AddComponent<VoiceInputController>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }

        _instance = this;
        IsMutedByUser = PlayerPrefs.GetInt(MicMutedPreferenceKey, 0) != 0;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        ResolveInputHandler();
        bool pressed = Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame;
        if (_inputHandler != null && _inputHandler.IsOwner)
            pressed |= _inputHandler.VoiceMutePressed;

        if (pressed)
        {
            IsMutedByUser = !IsMutedByUser;
            PlayerPrefs.SetInt(MicMutedPreferenceKey, IsMutedByUser ? 1 : 0);
            PlayerPrefs.Save();

            if (VivoxManager.Instance != null && VivoxManager.Instance.IsLoggedIn)
                VivoxManager.Instance.SetMicrophoneMute(IsMutedByUser);

            Debug.Log($"[VoiceInputController] Microphone requested: {(IsMutedByUser ? "OFF" : "ON")}");
        }
    }

    private void ResolveInputHandler()
    {
        if (_inputHandler != null && _inputHandler.IsSpawned && _inputHandler.IsOwner) return;

        foreach (PlayerInputHandler handler in FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None))
        {
            if (!handler.IsOwner) continue;
            _inputHandler = handler;
            return;
        }

        _inputHandler = null;
    }
}
