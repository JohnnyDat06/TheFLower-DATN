using UnityEngine;
using UnityEngine.InputSystem;

public class VoiceInputController : MonoBehaviour
{
    private const string MicMutedPreferenceKey = "Vivox.MicMuted";
    private static VoiceInputController _instance;

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
        if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
        {
            IsMutedByUser = !IsMutedByUser;
            PlayerPrefs.SetInt(MicMutedPreferenceKey, IsMutedByUser ? 1 : 0);
            PlayerPrefs.Save();

            if (VivoxManager.Instance != null && VivoxManager.Instance.IsLoggedIn)
                VivoxManager.Instance.SetMicrophoneMute(IsMutedByUser);

            Debug.Log($"[VoiceInputController] Microphone requested: {(IsMutedByUser ? "OFF" : "ON")}");
        }
    }
}
