using System;
using System.Collections;
using Game.UI.LobbyAuto;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Persistent, network-compatible loading presentation used by SceneLoader and LoadingSyncManager.
/// </summary>
public class SeamlessLoadingOverlay : MonoBehaviour
{
    public static SeamlessLoadingOverlay Instance { get; private set; }

    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private Slider _progressSlider;
    [SerializeField] private TextMeshProUGUI _toBeContinuedText;
    [SerializeField] private float _fadeDuration = 0.5f;

    private GameObject _loadingPanel;
    private TextMeshProUGUI _progressText;
    private TextMeshProUGUI _loadingStatusText;
    private RectTransform _tipLeaf;
    private float _targetProgress;
    private bool _fadeInRequested;
    private bool _lobbyInteractive;
    private static Sprite s_roundedSprite;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        BuildRemadeInterface();
        _lobbyInteractive = IsLobbyScene();

        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
        _progressSlider.value = 0f;
        _toBeContinuedText.gameObject.SetActive(false);

        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        _lobbyInteractive = scene.name.Contains("Lobby", StringComparison.OrdinalIgnoreCase);
        if (!_lobbyInteractive) return;

        Debug.Log("[SeamlessLoadingOverlay] Lobby detected. Auto-cleaning overlay.");
        HideForLobby();
    }

    private void Update()
    {
        if (_tipLeaf != null && _canvasGroup.alpha > 0.01f)
            _tipLeaf.Rotate(0f, 0f, -95f * Time.unscaledDeltaTime);

        if (_progressSlider == null || _canvasGroup.alpha <= 0.01f) return;
        _progressSlider.value = Mathf.MoveTowards(
            _progressSlider.value,
            _targetProgress,
            Time.unscaledDeltaTime * 1.2f);
        UpdateProgressPresentation(_progressSlider.value);
    }

    public void ShowToBeContinued(bool show, string text = "The End!")
    {
        if (_toBeContinuedText == null) return;
        _toBeContinuedText.text = text;
        _toBeContinuedText.gameObject.SetActive(show);
    }

    public void ShowProgressBar(bool show)
    {
        if (_loadingPanel != null) _loadingPanel.SetActive(show);
        else if (_progressSlider != null) _progressSlider.gameObject.SetActive(show);
    }

    /// <summary>
    /// Marks the beginning of a scene transition, including a transition that
    /// starts while the current scene is the interactive Lobby.
    /// </summary>
    public void BeginLoadingTransition()
    {
        _lobbyInteractive = false;
    }

    /// <summary>
    /// Makes the loading presentation visible without restarting an in-progress fade.
    /// Multiple network callbacks can legitimately request the same presentation.
    /// </summary>
    public void EnsureLoadingVisible(bool resetProgress = false)
    {
        if (_lobbyInteractive)
        {
            HideForLobby();
            return;
        }

        ShowProgressBar(true);
        if (resetProgress)
        {
            _targetProgress = 0f;
            if (_progressSlider != null)
            {
                _progressSlider.value = 0f;
                UpdateProgressPresentation(0f);
            }
        }
        if (_canvasGroup == null || (_canvasGroup.alpha > 0.01f && !_fadeInRequested)) return;
        if (_fadeInRequested) return;
        FadeIn();
    }

    public void FadeIn(System.Action onComplete = null)
    {
        if (_lobbyInteractive)
        {
            HideForLobby();
            onComplete?.Invoke();
            return;
        }

        Debug.Log("[SeamlessLoadingOverlay] FadeIn called");
        StopAllCoroutines();
        _fadeInRequested = true;
        _targetProgress = 0f;
        if (_progressSlider != null)
        {
            _progressSlider.value = 0f;
            UpdateProgressPresentation(0f);
        }
        StartCoroutine(FadeRoutine(1f, onComplete));
    }

    /// <summary>
    /// Clears the persistent loading canvas before the Lobby UI is presented.
    /// A late network fade callback must never leave an invisible full-screen
    /// canvas intercepting Lobby pointer events.
    /// </summary>
    private void HideForLobby()
    {
        StopAllCoroutines();
        _fadeInRequested = false;
        _targetProgress = 1f;

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }

        ShowToBeContinued(false);
        ShowProgressBar(false);
    }

    private static bool IsLobbyScene()
    {
        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
            .Contains("Lobby", StringComparison.OrdinalIgnoreCase);
    }

    public void FadeOut(System.Action onComplete = null)
    {
        if (_lobbyInteractive)
        {
            HideForLobby();
            onComplete?.Invoke();
            return;
        }

        Debug.Log("[SeamlessLoadingOverlay] FadeOut called");
        StopAllCoroutines();
        _fadeInRequested = false;
        _targetProgress = 1f;
        StartCoroutine(FadeRoutine(0f, onComplete));
    }

    public void SetProgress(float value)
    {
        _targetProgress = Mathf.Max(_targetProgress, Mathf.Clamp01(value));
    }

    private IEnumerator FadeRoutine(float targetAlpha, System.Action onComplete)
    {
        _canvasGroup.blocksRaycasts = targetAlpha > 0.5f;
        float startAlpha = _canvasGroup.alpha;
        float elapsed = 0f;
        while (elapsed < _fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / _fadeDuration);
            yield return null;
        }

        _canvasGroup.alpha = targetAlpha;
        _fadeInRequested = targetAlpha > 0.5f;
        onComplete?.Invoke();
    }

    private void BuildRemadeInterface()
    {
        foreach (Transform child in transform)
            child.gameObject.SetActive(false);

        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

        LobbyRuntimeConfig config = Resources.Load<LobbyRuntimeConfig>("UI/LobbyRuntimeConfig");
        Sprite backgroundSprite = Resources.Load<Sprite>("UI/LobbyAutoBackground");

        Image background = CreateImage(transform, "ForestBackground", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.white);
        background.sprite = backgroundSprite;
        background.preserveAspect = false;
        CreateImage(transform, "ForestTint", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0.10f, 0.075f, 0.14f));

        if (config != null && config.LobbyLogo != null)
        {
            Image logo = CreateImage(transform, "TheFlowerLogo", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero, Color.white);
            logo.sprite = config.LobbyLogo;
            logo.preserveAspect = true;
            logo.rectTransform.pivot = new Vector2(0f, 1f);
            logo.rectTransform.anchoredPosition = new Vector2(72f, -52f);
            logo.rectTransform.sizeDelta = new Vector2(600f, 260f);
        }

        RectTransform module = CreateRect(transform, "LoadingModule");
        module.anchorMin = new Vector2(0.055f, 0.045f);
        module.anchorMax = new Vector2(0.74f, 0.36f);
        module.offsetMin = Vector2.zero;
        module.offsetMax = Vector2.zero;
        Image moduleBorder = module.gameObject.AddComponent<Image>();
        moduleBorder.sprite = GetRoundedSprite();
        moduleBorder.type = Image.Type.Sliced;
        moduleBorder.color = new Color(1f, 0.69f, 0.16f, 0.98f);

        Image panel = CreateImage(module, "Panel", Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f), new Color(0.015f, 0.18f, 0.135f, 0.94f));
        panel.sprite = GetRoundedSprite();
        panel.type = Image.Type.Sliced;
        _loadingPanel = module.gameObject;

        TMP_FontAsset headingFont = config != null ? config.HeadingFont : null;
        _loadingStatusText = CreateText(panel.transform, "Gathering petals...", 31f, new Color(1f, 0.96f, 0.80f, 1f), FontStyles.Bold, TextAlignmentOptions.Left, headingFont);
        Place(_loadingStatusText.rectTransform, new Vector2(42f, -27f), new Vector2(720f, 48f), new Vector2(0f, 1f));

        _progressText = CreateText(panel.transform, "0%", 36f, new Color(1f, 0.86f, 0.28f, 1f), FontStyles.Bold, TextAlignmentOptions.Right, headingFont);
        Place(_progressText.rectTransform, new Vector2(-42f, -24f), new Vector2(180f, 52f), new Vector2(1f, 1f));

        _progressSlider = CreateProgressSlider(panel.rectTransform);

        TextMeshProUGUI tipHeading = CreateText(panel.transform, "ADVENTURE TIP", 21f, new Color(1f, 0.72f, 0.20f, 1f), FontStyles.Bold, TextAlignmentOptions.Left, headingFont);
        Place(tipHeading.rectTransform, new Vector2(108f, -214f), new Vector2(420f, 34f), new Vector2(0f, 1f));

        TextMeshProUGUI tip = CreateText(panel.transform, "Stay close — some paths need both players.", 20f, new Color(1f, 0.97f, 0.88f, 1f), FontStyles.Normal, TextAlignmentOptions.Left, null);
        Place(tip.rectTransform, new Vector2(108f, -250f), new Vector2(850f, 36f), new Vector2(0f, 1f));

        RectTransform leaf = CreateRect(panel.transform, "TipLeaf");
        _tipLeaf = leaf;
        leaf.anchorMin = leaf.anchorMax = new Vector2(0f, 1f);
        leaf.pivot = new Vector2(0.5f, 0.5f);
        leaf.anchoredPosition = new Vector2(68f, -241f);
        leaf.sizeDelta = new Vector2(54f, 34f);
        leaf.localRotation = Quaternion.Euler(0f, 0f, 24f);
        Image leafImage = leaf.gameObject.AddComponent<Image>();
        leafImage.sprite = GetRoundedSprite();
        leafImage.type = Image.Type.Sliced;
        leafImage.color = new Color(0.40f, 0.82f, 0.18f, 1f);

        _toBeContinuedText = CreateText(transform, "The End!", 72f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center, headingFont);
        _toBeContinuedText.rectTransform.anchorMin = new Vector2(0.16f, 0.30f);
        _toBeContinuedText.rectTransform.anchorMax = new Vector2(0.84f, 0.70f);
        _toBeContinuedText.rectTransform.offsetMin = Vector2.zero;
        _toBeContinuedText.rectTransform.offsetMax = Vector2.zero;
        _toBeContinuedText.gameObject.SetActive(false);
    }

    private Slider CreateProgressSlider(RectTransform parent)
    {
        RectTransform track = CreateRect(parent, "FlowerProgress");
        track.anchorMin = new Vector2(0f, 1f);
        track.anchorMax = new Vector2(1f, 1f);
        track.pivot = new Vector2(0.5f, 1f);
        track.anchoredPosition = new Vector2(0f, -100f);
        track.sizeDelta = new Vector2(-84f, 58f);
        Image trackImage = track.gameObject.AddComponent<Image>();
        trackImage.sprite = GetRoundedSprite();
        trackImage.type = Image.Type.Sliced;
        trackImage.color = new Color(0.33f, 0.14f, 0.055f, 1f);

        RectTransform fillArea = CreateRect(track, "FillArea");
        fillArea.anchorMin = Vector2.zero;
        fillArea.anchorMax = Vector2.one;
        fillArea.offsetMin = new Vector2(7f, 7f);
        fillArea.offsetMax = new Vector2(-7f, -7f);

        RectTransform fill = CreateRect(fillArea, "GoldenFill");
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.sprite = GetRoundedSprite();
        fillImage.type = Image.Type.Sliced;
        fillImage.color = new Color(1f, 0.66f, 0.08f, 1f);

        Image shine = CreateImage(fill, "Shine", new Vector2(0f, 0.58f), Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, -4f), new Color(1f, 0.93f, 0.45f, 0.65f));
        shine.sprite = GetRoundedSprite();
        shine.type = Image.Type.Sliced;

        RectTransform handleArea = CreateRect(track, "HandleArea");
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(15f, 0f);
        handleArea.offsetMax = new Vector2(-15f, 0f);
        RectTransform flower = CreateFlowerMarker(handleArea);

        Slider slider = track.gameObject.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.interactable = false;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0f;
        slider.direction = Slider.Direction.LeftToRight;
        slider.fillRect = fill;
        slider.handleRect = flower;
        return slider;
    }

    private static RectTransform CreateFlowerMarker(RectTransform parent)
    {
        RectTransform flower = CreateRect(parent, "FlowerMarker");
        flower.sizeDelta = new Vector2(58f, 58f);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * 60f;
            RectTransform petal = CreateRect(flower, $"Petal{i + 1}");
            petal.anchorMin = petal.anchorMax = new Vector2(0.5f, 0.5f);
            petal.pivot = new Vector2(0.5f, 0.5f);
            petal.anchoredPosition = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * 15f;
            petal.sizeDelta = new Vector2(24f, 17f);
            petal.localRotation = Quaternion.Euler(0f, 0f, angle);
            Image petalImage = petal.gameObject.AddComponent<Image>();
            petalImage.sprite = GetRoundedSprite();
            petalImage.type = Image.Type.Sliced;
            petalImage.color = new Color(1f, 0.96f, 0.74f, 1f);
        }

        Image center = CreateImage(flower, "Center", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(1f, 0.65f, 0.08f, 1f));
        center.rectTransform.sizeDelta = new Vector2(20f, 20f);
        center.sprite = GetRoundedSprite();
        center.type = Image.Type.Sliced;
        return flower;
    }

    private void UpdateProgressPresentation(float value)
    {
        int percent = Mathf.RoundToInt(Mathf.Clamp01(value) * 100f);
        if (_progressText != null) _progressText.text = $"{percent}%";
        if (_loadingStatusText == null) return;
        _loadingStatusText.text = value < 0.30f
            ? "Gathering petals..."
            : value < 0.68f
                ? "Following the forest trail..."
                : value < 0.98f
                    ? "Preparing your adventure..."
                    : "Adventure ready!";
    }

    private static RectTransform CreateRect(Transform parent, string name)
    {
        GameObject child = new(name, typeof(RectTransform));
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image CreateImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        RectTransform rect = CreateRect(parent, name);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string value, float size, Color color, FontStyles style, TextAlignmentOptions alignment, TMP_FontAsset font)
    {
        RectTransform rect = CreateRect(parent, "Text");
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = alignment;
        text.raycastTarget = false;
        if (font != null) text.font = font;
        Shadow shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0.04f, 0.02f, 0.78f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size, Vector2 anchor)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static Sprite GetRoundedSprite()
    {
        if (s_roundedSprite != null) return s_roundedSprite;

        const int width = 64;
        const int height = 32;
        const float radius = 15f;
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false)
        {
            name = "LoadingRoundedSprite",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color[] pixels = new Color[width * height];
        Vector2 center = new((width - 1f) * 0.5f, (height - 1f) * 0.5f);
        Vector2 inner = new(center.x - radius, center.y - radius);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2 distance = new(
                    Mathf.Max(Mathf.Abs(x - center.x) - inner.x, 0f),
                    Mathf.Max(Mathf.Abs(y - center.y) - inner.y, 0f));
                float signedDistance = distance.magnitude - radius;
                float alpha = 1f - Mathf.SmoothStep(-0.75f, 0.75f, signedDistance);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        s_roundedSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
        s_roundedSprite.name = "LoadingRoundedSprite";
        s_roundedSprite.hideFlags = HideFlags.HideAndDontSave;
        return s_roundedSprite;
    }
}
