using Unity.Netcode;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small runtime Vivox HUD: white while quiet, glowing while speaking, crossed out while muted.
/// </summary>
public sealed class VoiceMicIndicator : MonoBehaviour
{
    private static readonly Color QuietColor = Color.white;
    private static readonly Color SpeakingColor = new(0.25f, 1f, 0.68f, 1f);
    private static readonly Color MutedColor = new(0.68f, 0.72f, 0.74f, 1f);

    private Canvas _canvas;
    private Image _glow;
    private Image _microphone;
    private Image _muteSlash;
    private float _smoothedEnergy;
    private bool _uiVisible = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<VoiceMicIndicator>() != null) return;
        new GameObject("VoiceMicIndicator").AddComponent<VoiceMicIndicator>();
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        BuildInterface();
        PlayerHealthHUDRemake.GameplayHudVisibilityChanged += HandleGameplayHudVisibilityChanged;
        SetUiVisible(PlayerHealthHUDRemake.IsGameplayHudVisible);
    }

    private void OnDestroy()
    {
        PlayerHealthHUDRemake.GameplayHudVisibilityChanged -= HandleGameplayHudVisibilityChanged;
    }

    private void Update()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        VivoxManager vivox = VivoxManager.Instance;
        bool inSession = networkManager != null && networkManager.IsListening;
        bool voiceReady = vivox != null && vivox.IsLoggedIn && !string.IsNullOrEmpty(vivox.JoinedChannelName);

        SetVisible(_uiVisible && inSession);

        bool muted = VoiceInputController.IsMutedByUser || (voiceReady && vivox.IsMicrophoneMuted());
        float energy = voiceReady && !muted ? ReadLocalAudioEnergy(vivox.JoinedChannelName) : 0f;
        _smoothedEnergy = Mathf.Lerp(_smoothedEnergy, energy, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));

        float speakingAmount = Mathf.InverseLerp(0.015f, 0.22f, _smoothedEnergy);
        _microphone.color = muted ? MutedColor : Color.Lerp(QuietColor, SpeakingColor, speakingAmount);
        _muteSlash.gameObject.SetActive(muted);

        Color glowColor = SpeakingColor;
        glowColor.a = muted ? 0f : speakingAmount * 0.8f;
        _glow.color = glowColor;
        _glow.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.05f, 1.38f, speakingAmount);
    }

    private static float ReadLocalAudioEnergy(string channelName)
    {
        try
        {
            if (!VivoxService.Instance.ActiveChannels.TryGetValue(channelName, out var participants)) return 0f;
            foreach (var participant in participants)
                if (participant.IsSelf) return Mathf.Clamp01((float)participant.AudioEnergy);
        }
        catch (System.Exception)
        {
            // Vivox can briefly rebuild ActiveChannels while joining or leaving.
        }

        return 0f;
    }

    private void SetVisible(bool visible)
    {
        if (_canvas != null && _canvas.gameObject.activeSelf != visible)
            _canvas.gameObject.SetActive(visible);
    }

    private void SetUiVisible(bool visible)
    {
        _uiVisible = visible;
        NetworkManager networkManager = NetworkManager.Singleton;
        bool inSession = networkManager != null && networkManager.IsListening;
        SetVisible(_uiVisible && inSession);
    }

    private void HandleGameplayHudVisibilityChanged(bool visible)
    {
        SetUiVisible(visible);
    }

    private void BuildInterface()
    {
        _canvas = new GameObject("VoiceMicCanvas", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
        _canvas.transform.SetParent(transform, false);
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1490;

        CanvasScaler scaler = _canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform badge = CreateImage("MicBadge", _canvas.transform, new Color(0.015f, 0.07f, 0.08f, 0.82f));
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1f, 0f);
        badge.anchoredPosition = new Vector2(-24f, 24f);
        badge.sizeDelta = new Vector2(64f, 64f);

        Sprite microphoneSprite = CreateMicrophoneSprite();

        _glow = CreateImage("SpeakingGlow", badge, Color.clear).GetComponent<Image>();
        SetCenteredSize(_glow.rectTransform, 48f, 48f);
        _glow.sprite = microphoneSprite;
        _glow.preserveAspect = true;

        _microphone = CreateImage("Microphone", badge, QuietColor).GetComponent<Image>();
        SetCenteredSize(_microphone.rectTransform, 44f, 44f);
        _microphone.sprite = microphoneSprite;
        _microphone.preserveAspect = true;

        _muteSlash = CreateImage("MutedSlash", badge, new Color(1f, 0.25f, 0.22f, 1f)).GetComponent<Image>();
        SetCenteredSize(_muteSlash.rectTransform, 5f, 48f);
        _muteSlash.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
    }

    private static RectTransform CreateImage(string name, Transform parent, Color color)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.GetComponent<Image>().color = color;
        rect.GetComponent<Image>().raycastTarget = false;
        return rect;
    }

    private static void SetCenteredSize(RectTransform rect, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width, height);
    }

    private static Sprite CreateMicrophoneSprite()
    {
        const int size = 64;
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            name = "RuntimeMicrophoneIcon",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32 clear = new(255, 255, 255, 0);
        Color32 solid = new(255, 255, 255, 255);
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 point = new(x + 0.5f, y + 0.5f);
                bool capsule = DistanceToVerticalSegment(point, new Vector2(32f, 24f), new Vector2(32f, 40f)) <= 8f;
                bool sideBars = (x >= 18 && x <= 22 || x >= 41 && x <= 45) && y >= 24 && y <= 35;
                float lowerArcDistance = Vector2.Distance(point, new Vector2(32f, 25f));
                bool lowerArc = y <= 25 && lowerArcDistance >= 11f && lowerArcDistance <= 15f;
                bool stem = x >= 30 && x <= 34 && y >= 8 && y <= 14;
                bool baseLine = x >= 23 && x <= 41 && y >= 5 && y <= 9;
                pixels[y * size + x] = capsule || sideBars || lowerArc || stem || baseLine ? solid : clear;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 64f);
    }

    private static float DistanceToVerticalSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        float t = Mathf.Clamp01(Vector2.Dot(point - start, end - start) / (end - start).sqrMagnitude);
        return Vector2.Distance(point, Vector2.Lerp(start, end, t));
    }
}
