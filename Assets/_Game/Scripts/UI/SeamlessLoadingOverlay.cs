using System;
using System.Collections;
using Game.UI.LobbyAuto;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Persistent, network-compatible loading presentation used by SceneLoader and LoadingSyncManager.
/// </summary>
public class SeamlessLoadingOverlay : MonoBehaviour
{
    private const float EndCreditsScrollSpeed = 68f;
    private const float EndCreditsStartPadding = 90f;
    private const float EndCreditsEndPadding = 120f;
    private const float EndTitleDuration = 2.5f;
    private const float EndThankYouDuration = 2.5f;
    private const float EndCreditsHoldDuration = 1.2f;
    private const float EndFadeDuration = 0.24f;
    private const float EndCreditsMusicFadeDuration = 1.2f;
    private const float EndCreditsLogoSpacing = 48f;
    private static readonly Vector2 EndCreditsLogoSize = new(360f, 360f);
    private const string EndCreditsContent =
        "<size=120%><b>THE FLOWER</b></size>\n\n\n" +
        "<b>GAME DIRECTOR</b>\nHồ Tấn Đạt\n\n" +
        "<b>GAME PRODUCER</b>\nHồ Tấn Đạt\n\n" +
        "<b>LEAD DESIGNER</b>\nHồ Tấn Đạt\n\n" +
        "<b>MAP & LEVEL DESIGN</b>\nHuỳnh Trần Anh Thư\nLê Phan Hòa Thuận\nHồ Tấn Đạt\n\n" +
        "<b>NARRATIVE DESIGN</b>\nTeam Doro\n\n" +
        "<b>UI/UX DESIGN</b>\nPhạm Hồng Đăng\n\n" +
        "<b>PROGRAMMING</b>\nHồ Tấn Đạt\nLê Phan Hòa Thuận\nPhạm Hồng Đăng\n\n" +
        "<b>GAMEPLAY SYSTEMS</b>\nLê Phan Hòa Thuận\n\n" +
        "<b>ART DIRECTOR</b>\nHuỳnh Trần Anh Thư\n\n" +
        "<b>ENVIRONMENT & TECHNICAL ART</b>\nHuỳnh Trần Anh Thư\n\n" +
        "<b>ART & VFX</b>\nHuỳnh Trần Anh Thư\nHồ Tấn Đạt\n\n" +
        "<b>ANIMATION</b>\nTeam Doro\n\n" +
        "<b>AUDIO</b>\nHồ Tấn Đạt\nLê Phan Hòa Thuận\nPhạm Hồng Đăng\n\n" +
        "<b>QA / TESTING</b>\nLê Phan Hòa Thuận (Lead)\nHồ Tấn Đạt · Phạm Hồng Đăng · Huỳnh Trần Anh Thư\n\n" +
        "<b>GAME MARKETING</b>\nHồ Tấn Đạt\n\n\n" +
        "<b>BUILT WITH</b>\nUnity 6 LTS\nNetcode for GameObjects\nUnity Gaming Services (Lobby & Relay)\nVivox Voice Chat\nCinemachine · Unity Splines · URP\n\n\n" +
        "<b>SPECIAL THANKS</b>\n" +
        "Thầy Nguyễn Thế Trung - Giảng viên hướng dẫn\n" +
        "Thầy Nguyễn Thế Duy - Giảng viên Lập trình Game\n" +
        "Trường Cao đẳng FPT Polytechnic Cần Thơ";

    public static SeamlessLoadingOverlay Instance { get; private set; }

    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private Slider _progressSlider;
    [SerializeField] private TextMeshProUGUI _toBeContinuedText;
    [SerializeField] private float _fadeDuration = 0.5f;

    [Header("End Credits")]
    [Tooltip("Assign the game logo here to have it appear above the end credits.")]
    [SerializeField] private Sprite _endCreditsLogo;

    private GameObject _loadingPanel;
    private TextMeshProUGUI _progressText;
    private TextMeshProUGUI _loadingStatusText;
    private RectTransform _tipLeaf;
    private RectTransform _creditsViewport;
    private RectTransform _creditsContent;
    private RectTransform _creditsLogoRect;
    private TextMeshProUGUI _creditsText;
    private GameObject _creditsBackdrop;
    private CanvasGroup _endTitleGroup;
    private CanvasGroup _creditsStageGroup;
    private AudioClip _endCreditsMusic;
    private AudioSource _endCreditsMusicSource;
    private float _targetProgress;
    private bool _fadeInRequested;
    private bool _lobbyInteractive;
    private bool _isShowingEndCredits;
    private bool _endCreditsFinished;
    private bool _returnToLobbyAfterTest;
    private float _endCreditsEndPosition;
    private static Sprite s_roundedSprite;
    private static TMP_FontAsset s_vietnameseCreditsFont;

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
        _endCreditsMusic = Resources.Load<AudioClip>("Audio/Music/Where_The_River_Widens");
        BuildRemadeInterface();
        _lobbyInteractive = IsLobbyScene();

        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
        _progressSlider.value = 0f;
        _toBeContinuedText.gameObject.SetActive(false);
        _creditsViewport.gameObject.SetActive(false);

        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        StopEndCreditsMusic();
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
        HandleEndCreditsShortcut();

        if (_tipLeaf != null && _canvasGroup.alpha > 0.01f)
            _tipLeaf.Rotate(0f, 0f, -95f * Time.unscaledDeltaTime);

        UpdateEndCreditsScroll();

        if (_progressSlider == null || _canvasGroup.alpha <= 0.01f) return;
        _progressSlider.value = Mathf.MoveTowards(
            _progressSlider.value,
            _targetProgress,
            Time.unscaledDeltaTime * 1.2f);
        UpdateProgressPresentation(_progressSlider.value);
    }

    public void ShowToBeContinued(bool show, string text = "The End!")
    {
        if (show)
        {
            HideEndCredits();
            if (_endTitleGroup != null) _endTitleGroup.alpha = 1f;
        }
        if (_toBeContinuedText == null) return;
        _toBeContinuedText.text = text;
        _toBeContinuedText.gameObject.SetActive(show);
    }

    /// <summary>
    /// Shows the final end credits and starts their vertical film-style scroll.
    /// This is also used by the hidden test shortcut so the ending can be reviewed
    /// without completing the full game flow.
    /// </summary>
    public void ShowEndCredits()
    {
        if (_creditsViewport == null || _creditsContent == null) return;

        _returnToLobbyAfterTest = _lobbyInteractive;
        BeginLoadingTransition();
        StopAllCoroutines();
        StartEndCreditsMusic();
        HideForLoadingPresentation();
        if (_creditsBackdrop != null) _creditsBackdrop.SetActive(true);
        _creditsViewport.gameObject.SetActive(false);
        if (_endTitleGroup != null) _endTitleGroup.alpha = 0f;
        if (_creditsStageGroup != null) _creditsStageGroup.alpha = 0f;
        _endCreditsFinished = false;
        _isShowingEndCredits = false;
        StartCoroutine(PlayEndCreditsSequence());
    }

    /// <summary>Hides the ending credits when a new loading transition begins.</summary>
    public void HideEndCredits()
    {
        StopAllCoroutines();
        StopEndCreditsMusic();
        _isShowingEndCredits = false;
        _endCreditsFinished = false;
        if (_creditsBackdrop != null) _creditsBackdrop.SetActive(false);
        if (_creditsViewport != null) _creditsViewport.gameObject.SetActive(false);
        if (_endTitleGroup != null) _endTitleGroup.alpha = 0f;
        if (_creditsStageGroup != null) _creditsStageGroup.alpha = 0f;
    }

    /// <summary>True after the full end-credit scroll has reached its final position.</summary>
    public bool IsEndCreditsComplete => _endCreditsFinished;

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
        HideEndCredits();

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }

        ShowToBeContinued(false);
        ShowProgressBar(false);
    }

    private void HideForLoadingPresentation()
    {
        _toBeContinuedText.gameObject.SetActive(false);
        ShowProgressBar(false);
        if (_loadingPanel != null) _loadingPanel.SetActive(false);
    }

    private IEnumerator PlayEndCreditsSequence()
    {
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = true;
        if (_canvasGroup.alpha <= 0.01f)
            yield return StartCoroutine(FadeRoutine(1f, null));

        _toBeContinuedText.text = "THE END";
        _toBeContinuedText.gameObject.SetActive(true);
        _endTitleGroup.alpha = 1f;
        yield return new WaitForSecondsRealtime(EndTitleDuration);

        yield return StartCoroutine(FadeToStage(() =>
        {
            _toBeContinuedText.gameObject.SetActive(false);
            _creditsViewport.gameObject.SetActive(true);
            ResetEndCreditsScroll();
        }));

        while (_isShowingEndCredits)
            yield return null;

        yield return new WaitForSecondsRealtime(EndCreditsHoldDuration);

        yield return StartCoroutine(FadeToStage(() =>
        {
            _creditsViewport.gameObject.SetActive(false);
            _toBeContinuedText.text = "THANK YOU FOR PLAYING";
            _toBeContinuedText.gameObject.SetActive(true);
        }));

        yield return new WaitForSecondsRealtime(EndThankYouDuration);
        yield return StartCoroutine(FadeOutEndCreditsMusic());
        _endCreditsFinished = true;

        if (_returnToLobbyAfterTest)
        {
            _returnToLobbyAfterTest = false;
            _lobbyInteractive = true;
            HideForLobby();
            LobbyAutoController.ResumeLobbyMusicAfterEndCredits();
        }
    }

    private void StartEndCreditsMusic()
    {
        StopEndCreditsMusic();

        if (AudioManager.Instance == null)
            return;

        LobbyAutoController.StopLobbyMusicForEndCredits();
        StopActiveSceneAudioForEndCredits();
        AudioManager.Instance.StopAllMusic();
        if (_endCreditsMusic == null)
        {
            Debug.LogWarning("[SeamlessLoadingOverlay] End credits music was not found at Resources/Audio/Music/Where_The_River_Widens.");
            return;
        }

        _endCreditsMusicSource = gameObject.AddComponent<AudioSource>();
        _endCreditsMusicSource.clip = _endCreditsMusic;
        _endCreditsMusicSource.playOnAwake = false;
        _endCreditsMusicSource.loop = false;
        _endCreditsMusicSource.spatialBlend = 0f;
        _endCreditsMusicSource.outputAudioMixerGroup = null;
        _endCreditsMusicSource.ignoreListenerPause = true;
        _endCreditsMusicSource.volume = 1f;
        _endCreditsMusicSource.Play();
    }

    private void StopActiveSceneAudioForEndCredits()
    {
        UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        AudioSource[] sources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);

        foreach (AudioSource source in sources)
        {
            // Boss ambience is authored as a scene-owned AudioSource, outside AudioManager's music pool.
            if (source == null || !source.isPlaying || source.gameObject.scene.handle != activeScene.handle)
                continue;

            source.Stop();
        }
    }

    private IEnumerator FadeOutEndCreditsMusic()
    {
        if (_endCreditsMusicSource == null)
            yield break;

        float startVolume = _endCreditsMusicSource.volume;
        float elapsed = 0f;
        while (_endCreditsMusicSource != null && elapsed < EndCreditsMusicFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / EndCreditsMusicFadeDuration);
            _endCreditsMusicSource.volume = Mathf.Lerp(startVolume, 0f, progress);
            yield return null;
        }

        StopEndCreditsMusic();
    }

    private void StopEndCreditsMusic()
    {
        if (_endCreditsMusicSource == null)
            return;

        _endCreditsMusicSource.Stop();
        Destroy(_endCreditsMusicSource);
        _endCreditsMusicSource = null;
    }

    private IEnumerator FadeToStage(Action setupStage)
    {
        yield return StartCoroutine(FadeStageGroups(0f));

        setupStage?.Invoke();
        _endTitleGroup.alpha = 0f;
        _creditsStageGroup.alpha = 0f;
        yield return StartCoroutine(FadeStageGroups(1f));
    }

    private IEnumerator FadeStageGroups(float targetAlpha)
    {
        float titleStartAlpha = _endTitleGroup.alpha;
        float creditsStartAlpha = _creditsStageGroup.alpha;
        float elapsed = 0f;
        while (elapsed < EndFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / EndFadeDuration);
            _endTitleGroup.alpha = Mathf.Lerp(titleStartAlpha, targetAlpha, progress);
            _creditsStageGroup.alpha = Mathf.Lerp(creditsStartAlpha, targetAlpha, progress);
            yield return null;
        }

        _endTitleGroup.alpha = targetAlpha;
        _creditsStageGroup.alpha = targetAlpha;
    }

    private void HandleEndCreditsShortcut()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        bool controlHeld = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        bool shiftHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        if (controlHeld && shiftHeld && keyboard.enterKey.wasPressedThisFrame)
        {
            ShowEndCredits();
            Debug.Log("[SeamlessLoadingOverlay] End credits opened with Ctrl+Shift+Enter.");
        }
    }

    private void ResetEndCreditsScroll()
    {
        _creditsContent.ForceUpdateRectTransforms();
        _creditsText.ForceMeshUpdate();

        float logoBlockHeight = EndCreditsLogoSize.y + EndCreditsLogoSpacing;
        float viewportHeight = Mathf.Max(1f, _creditsViewport.rect.height);
        float contentHeight = Mathf.Max(
            _creditsContent.rect.height,
            logoBlockHeight + _creditsText.preferredHeight + EndCreditsStartPadding);

        _creditsContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
        _creditsContent.anchoredPosition = new Vector2(0f, -contentHeight - EndCreditsStartPadding);
        _endCreditsEndPosition = viewportHeight + EndCreditsEndPadding;
        _isShowingEndCredits = true;
        _endCreditsFinished = false;
    }

    private void UpdateEndCreditsScroll()
    {
        if (!_isShowingEndCredits || _creditsContent == null) return;

        Vector2 position = _creditsContent.anchoredPosition;
        position.y += EndCreditsScrollSpeed * Time.unscaledDeltaTime;
        if (position.y >= _endCreditsEndPosition)
        {
            position.y = _endCreditsEndPosition;
            _isShowingEndCredits = false;
        }

        _creditsContent.anchoredPosition = position;
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
        _endTitleGroup = _toBeContinuedText.gameObject.AddComponent<CanvasGroup>();
        _endTitleGroup.alpha = 0f;

        Image creditsBackdrop = CreateImage(
            transform,
            "EndCreditsBackdrop",
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero,
            Color.black);
        _creditsBackdrop = creditsBackdrop.gameObject;
        _creditsBackdrop.SetActive(false);
        creditsBackdrop.transform.SetSiblingIndex(_toBeContinuedText.transform.GetSiblingIndex());

        _creditsViewport = CreateRect(transform, "EndCreditsViewport");
        _creditsViewport.anchorMin = new Vector2(0.07f, 0.04f);
        _creditsViewport.anchorMax = new Vector2(0.93f, 0.96f);
        _creditsViewport.offsetMin = Vector2.zero;
        _creditsViewport.offsetMax = Vector2.zero;
        _creditsViewport.gameObject.AddComponent<RectMask2D>();
        _creditsStageGroup = _creditsViewport.gameObject.AddComponent<CanvasGroup>();
        _creditsStageGroup.alpha = 0f;

        // Scrolling container: holds the game logo (top) and the credits text (below it).
        _creditsContent = CreateRect(_creditsViewport, "EndCreditsContent");
        _creditsContent.anchorMin = new Vector2(0.5f, 0f);
        _creditsContent.anchorMax = new Vector2(0.5f, 0f);
        _creditsContent.pivot = new Vector2(0.5f, 0f);
        _creditsContent.anchoredPosition = Vector2.zero;
        _creditsContent.sizeDelta = new Vector2(1650f, 1f);

        // Logo slot — assign a Sprite to "_endCreditsLogo" in the Inspector to show it here.
        Image creditsLogo = CreateImage(
            _creditsContent,
            "EndCreditsLogo",
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            Vector2.zero,
            Vector2.zero,
            Color.white);
        creditsLogo.sprite = _endCreditsLogo;
        creditsLogo.preserveAspect = true;
        creditsLogo.raycastTarget = false;
        _creditsLogoRect = creditsLogo.rectTransform;
        _creditsLogoRect.pivot = new Vector2(0.5f, 1f);
        _creditsLogoRect.anchoredPosition = Vector2.zero;
        _creditsLogoRect.sizeDelta = EndCreditsLogoSize;

        TMP_FontAsset creditsFont = GetVietnameseCreditsFont(headingFont);
        _creditsText = CreateText(
            _creditsContent,
            EndCreditsContent,
            46f,
            new Color(1f, 0.96f, 0.82f, 1f),
            FontStyles.Normal,
            TextAlignmentOptions.Center,
            creditsFont);
        RectTransform textRect = _creditsText.rectTransform;
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = new Vector2(0f, -(EndCreditsLogoSize.y + EndCreditsLogoSpacing));
        textRect.sizeDelta = new Vector2(0f, 1f);
        _creditsText.enableWordWrapping = true;
        _creditsText.overflowMode = TextOverflowModes.Overflow;
        _creditsText.verticalAlignment = VerticalAlignmentOptions.Top;
        _creditsViewport.gameObject.SetActive(false);
    }

    private static TMP_FontAsset GetVietnameseCreditsFont(TMP_FontAsset fallbackFont)
    {
        if (s_vietnameseCreditsFont != null) return s_vietnameseCreditsFont;

        TMP_FontAsset systemFont = TMP_FontAsset.CreateFontAsset("Arial", "Regular", 90);
        if (systemFont == null)
        {
            Debug.LogWarning("[SeamlessLoadingOverlay] Arial was not available; using the default credits font.");
            return fallbackFont;
        }

        systemFont.hideFlags = HideFlags.HideAndDontSave;
        if (!systemFont.TryAddCharacters(EndCreditsContent, out string missingCharacters))
        {
            Debug.LogWarning($"[SeamlessLoadingOverlay] Credits font is missing characters: {missingCharacters}");
            UnityEngine.Object.Destroy(systemFont);
            return fallbackFont;
        }

        s_vietnameseCreditsFont = systemFont;
        return s_vietnameseCreditsFont;
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
