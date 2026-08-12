using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Runtime presentation for the Eris board role lobby and gameplay prompts.
/// It owns only local UI; all choices and actions are validated by the manager/server.
/// </summary>
public sealed class ErisBoardUI : MonoBehaviour
{
    private const int CanvasOrder = 260;
    private static Sprite _roundedSprite;

    private ErisMinigameManager _manager;
    private Canvas _canvas;
    private CanvasGroup _canvasGroup;
    private TMP_Text _title;
    private TMP_Text _status;
    private TMP_Text _countdown;
    private TMP_Text _hint;
    private TMP_Text _startLabel;
    private TMP_Text _controllerLock;
    private TMP_Text _observerLock;
    private Button _controllerButton;
    private Button _observerButton;
    private Button _startButton;
    private Button _swapButton;
    private Button _replayButton;
    private readonly List<Selectable> _disabledSelectables = new();
    private bool _isBuilt;
    private bool _roleCursorActive;

    public void Initialize(ErisMinigameManager manager)
    {
        _manager = manager;
        EnsureEventSystem();
        BuildInterface();
        Refresh();
    }

    private void Update()
    {
        if (!_isBuilt || _manager == null) return;
        Refresh();
    }

    private void OnDestroy()
    {
        if (_roleCursorActive)
            CameraManager.Instance?.SetGameplayCameraLocked(false);
        UICursorLockService.Release(this);
        RestoreGameplayCursor();
        RestoreOtherSelectables();
    }

    private void BuildInterface()
    {
        if (_isBuilt) return;
        _isBuilt = true;

        GameObject canvasObject = new("ErisBoardCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        _canvas = canvasObject.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasOrder;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = CreateRect(canvasObject.transform, "Overlay");
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        _canvasGroup = root.gameObject.AddComponent<CanvasGroup>();
        _canvasGroup.interactable = true;
        _canvasGroup.blocksRaycasts = true;

        Image dim = CreateImage(root.transform, "Dim", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.015f, 0.045f, 0.065f, 0.46f));
        dim.raycastTarget = true;

        RectTransform panel = CreateRect(root.transform, "RolePanel");
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(1000f, 560f);
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = GetRoundedSprite();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color(0.025f, 0.09f, 0.11f, 0.95f);
        panel.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -8f);

        _title = CreateText(panel, "Title", "BÀN CỜ ERIS", 42f, FontStyles.Bold, TextAlignmentOptions.Center);
        _title.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        _title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        _title.rectTransform.pivot = new Vector2(0.5f, 1f);
        _title.rectTransform.anchoredPosition = new Vector2(0f, -30f);
        _title.rectTransform.sizeDelta = new Vector2(900f, 70f);
        _title.color = new Color(1f, 0.88f, 0.42f, 1f);

        _status = CreateText(panel, "Status", "Chọn vai trò của bạn", 22f, FontStyles.Normal, TextAlignmentOptions.Center);
        _status.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        _status.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        _status.rectTransform.pivot = new Vector2(0.5f, 1f);
        _status.rectTransform.anchoredPosition = new Vector2(0f, -92f);
        _status.rectTransform.sizeDelta = new Vector2(820f, 38f);

        _controllerButton = CreateRoleButton(panel, "ControllerCard", new Vector2(-245f, 0f), "NGƯỜI ĐIỀU KHIỂN", "Di chuyển quân cờ theo đường đi", out _controllerLock);
        _observerButton = CreateRoleButton(panel, "ObserverCard", new Vector2(245f, 0f), "NGƯỜI QUAN SÁT", "Ghi nhớ đường đi và bấm E khi sẵn sàng", out _observerLock);
        _controllerButton.onClick.AddListener(() => _manager.RequestRole(ErisRole.Controller));
        _observerButton.onClick.AddListener(() => _manager.RequestRole(ErisRole.Observer));

        _startButton = CreateButton(panel, "Start", new Vector2(0f, -185f), new Vector2(280f, 62f), "BẮT ĐẦU", new Color(0.20f, 0.68f, 0.56f, 1f));
        _startLabel = _startButton.GetComponentInChildren<TMP_Text>();
        _startButton.onClick.AddListener(_manager.RequestStart);

        _countdown = CreateText(root.transform, "Countdown", string.Empty, 128f, FontStyles.Bold, TextAlignmentOptions.Center);
        _countdown.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        _countdown.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _countdown.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        _countdown.rectTransform.anchoredPosition = Vector2.zero;
        _countdown.rectTransform.sizeDelta = new Vector2(500f, 180f);
        _countdown.color = new Color(1f, 0.92f, 0.42f, 1f);

        RectTransform hintPanel = CreateRect(root.transform, "HintPanel");
        hintPanel.anchorMin = new Vector2(0.5f, 1f);
        hintPanel.anchorMax = new Vector2(0.5f, 1f);
        hintPanel.pivot = new Vector2(0.5f, 1f);
        hintPanel.anchoredPosition = new Vector2(0f, -28f);
        hintPanel.sizeDelta = new Vector2(900f, 56f);
        Image hintBackground = hintPanel.gameObject.AddComponent<Image>();
        hintBackground.sprite = GetRoundedSprite();
        hintBackground.type = Image.Type.Sliced;
        hintBackground.color = new Color(0.02f, 0.12f, 0.14f, 0.93f);
        _hint = CreateText(hintPanel, "Hint", string.Empty, 18f, FontStyles.Normal, TextAlignmentOptions.Center);
        _hint.rectTransform.anchorMin = Vector2.zero;
        _hint.rectTransform.anchorMax = Vector2.one;
        _hint.rectTransform.offsetMin = new Vector2(20f, 8f);
        _hint.rectTransform.offsetMax = new Vector2(-20f, -8f);

        _swapButton = CreateButton(root.transform, "Swap", new Vector2(-155f, -96f), new Vector2(230f, 52f), "HOÁN ĐỔI VỊ TRÍ", new Color(0.24f, 0.40f, 0.65f, 1f));
        SetTopButtonPosition(_swapButton, new Vector2(-155f, -102f));
        _swapButton.onClick.AddListener(_manager.RequestSwapRoles);
        _replayButton = CreateButton(root.transform, "Replay", new Vector2(155f, -96f), new Vector2(230f, 52f), "XEM LẠI ĐƯỜNG ĐI", new Color(0.55f, 0.32f, 0.68f, 1f));
        SetTopButtonPosition(_replayButton, new Vector2(155f, -102f));
        _replayButton.onClick.AddListener(_manager.RequestReplayPath);

        root.gameObject.SetActive(false);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        GameObject eventSystem = new("ErisBoardEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(eventSystem);
    }

    private void Refresh()
    {
        ErisSessionPhase phase = _manager.SessionPhase;
        bool visible = phase != ErisSessionPhase.Idle && phase != ErisSessionPhase.Completed;
        _canvasGroup.alpha = visible ? 1f : 0f;
        _canvasGroup.interactable = visible;
        _canvasGroup.blocksRaycasts = visible;
        _canvas.transform.GetChild(0).gameObject.SetActive(visible);
        if (!visible)
        {
            ApplyRoleSelectionCursor(false);
            RestoreOtherSelectables();
            return;
        }

        bool roleSelection = phase == ErisSessionPhase.RoleSelection;
        bool playing = phase == ErisSessionPhase.Playing;
        bool countdown = phase == ErisSessionPhase.Countdown;
        ApplyRoleSelectionCursor(roleSelection);
        Transform panel = _canvas.transform.GetChild(0).Find("RolePanel");
        Transform dim = _canvas.transform.GetChild(0).Find("Dim");
        panel.gameObject.SetActive(roleSelection);
        dim.gameObject.SetActive(roleSelection || countdown);
        _countdown.gameObject.SetActive(countdown);
        _countdown.text = countdown ? _manager.CountdownValue.ToString() : string.Empty;
        _swapButton.gameObject.SetActive(playing);
        _replayButton.gameObject.SetActive(playing);
        _hint.transform.parent.gameObject.SetActive(playing);

        _controllerLock.text = _manager.IsRoleLocked(ErisRole.Controller) ? "[KHÓA] ĐÃ ĐƯỢC CHỌN" : "TRỐNG";
        _observerLock.text = _manager.IsRoleLocked(ErisRole.Observer) ? "[KHÓA] ĐÃ ĐƯỢC CHỌN" : "TRỐNG";
        _controllerButton.interactable = roleSelection && _manager.CanSelectRole(ErisRole.Controller);
        _observerButton.interactable = roleSelection && _manager.CanSelectRole(ErisRole.Observer);
        _startButton.interactable = roleSelection && _manager.CanStartSession;
        if (_startLabel != null) _startLabel.text = _manager.IsSoloSelection ? "BẮT ĐẦU SOLO" : "BẮT ĐẦU";
        _swapButton.interactable = playing && !_manager.IsSoloSession;

        _status.text = roleSelection
            ? _manager.RoleStatusMessage
            : countdown ? "Hai người đã sẵn sàng" : "BÀN CỜ ĐANG DIỄN RA";
        _hint.text = BuildHint(phase);
        DisableOtherSelectables();
    }

    private void ApplyRoleSelectionCursor(bool roleSelection)
    {
        if (roleSelection)
        {
            // Role selection is a local screen-space UI on every machine. Do
            // not leave one client with the gameplay crosshair locked at the
            // centre while the other client can click the role cards.
            if (!_roleCursorActive)
            {
                CameraManager.Instance?.SetGameplayCameraLocked(true);
                _roleCursorActive = true;
            }
            // Request is idempotent; repeat it so another global UI cannot
            // silently release this client's role-selection cursor.
            UICursorLockService.Request(this);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (_roleCursorActive)
        {
            CameraManager.Instance?.SetGameplayCameraLocked(false);
            UICursorLockService.Release(this);
            _roleCursorActive = false;
        }
        RestoreGameplayCursor();
    }

    private static void RestoreGameplayCursor()
    {
        if (UICursorLockService.IsCursorReleased)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private string BuildHint(ErisSessionPhase phase)
    {
        if (phase == ErisSessionPhase.RoleSelection)
        {
            if (_manager.IsSoloSelection) return "NGƯỜI ĐIỀU KHIỂN có thể bấm BẮT ĐẦU SOLO · Hoặc chờ Người Quan Sát tham gia";
            return "Mỗi người chọn một vai trò khác nhau · Người còn lại bấm BẮT ĐẦU";
        }
        if (phase == ErisSessionPhase.Countdown) return string.Empty;
        if (_manager.LocalRole == ErisRole.Observer) return "SẴN SÀNG: BẤM E  ·  ←/→: ĐỔI CAMERA  ·  ↑: CAMERA TRÊN";
        return "WASD: DI CHUYỂN  ·  ←/→: ĐỔI CAMERA  ·  ↑: CAMERA TRÊN";
    }

    private void DisableOtherSelectables()
    {
        foreach (Selectable selectable in FindObjectsByType<Selectable>(FindObjectsSortMode.None))
        {
            if (selectable == _controllerButton || selectable == _observerButton || selectable == _startButton || selectable == _swapButton || selectable == _replayButton) continue;
            if (!selectable.interactable) continue;
            selectable.interactable = false;
            if (!_disabledSelectables.Contains(selectable)) _disabledSelectables.Add(selectable);
        }
    }

    private void RestoreOtherSelectables()
    {
        foreach (Selectable selectable in _disabledSelectables)
            if (selectable != null) selectable.interactable = true;
        _disabledSelectables.Clear();
    }

    private Button CreateRoleButton(RectTransform parent, string objectName, Vector2 position, string label, string description, out TMP_Text lockText)
    {
        Button button = CreateButton(parent, objectName, position, new Vector2(420f, 170f), string.Empty, new Color(0.07f, 0.28f, 0.31f, 1f));
        TMP_Text title = CreateText(button.transform, "Label", label, 26f, FontStyles.Bold, TextAlignmentOptions.Center);
        title.rectTransform.anchorMin = new Vector2(0f, 0.55f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = new Vector2(12f, 0f);
        title.rectTransform.offsetMax = new Vector2(-12f, -12f);
        TMP_Text body = CreateText(button.transform, "Description", description, 18f, FontStyles.Normal, TextAlignmentOptions.Center);
        body.rectTransform.anchorMin = new Vector2(0f, 0.22f);
        body.rectTransform.anchorMax = new Vector2(1f, 0.58f);
        body.rectTransform.offsetMin = new Vector2(18f, 0f);
        body.rectTransform.offsetMax = new Vector2(-18f, 0f);
        lockText = CreateText(button.transform, "Lock", "TRỐNG", 16f, FontStyles.Bold, TextAlignmentOptions.Center);
        lockText.rectTransform.anchorMin = new Vector2(0f, 0f);
        lockText.rectTransform.anchorMax = new Vector2(1f, 0.22f);
        lockText.rectTransform.offsetMin = new Vector2(8f, 8f);
        lockText.rectTransform.offsetMax = new Vector2(-8f, 0f);
        return button;
    }

    private static Button CreateButton(Transform parent, string objectName, Vector2 position, Vector2 size, string text, Color color)
    {
        GameObject buttonObject = CreateRect(parent, objectName).gameObject;
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = buttonObject.AddComponent<Image>();
        image.sprite = GetRoundedSprite();
        image.type = Image.Type.Sliced;
        image.color = color;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.22f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
        colors.disabledColor = new Color(0.12f, 0.15f, 0.17f, 0.62f);
        button.colors = colors;
        if (!string.IsNullOrWhiteSpace(text))
        {
            TMP_Text label = CreateText(buttonObject.transform, "Label", text, 21f, FontStyles.Bold, TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(8f, 4f);
            label.rectTransform.offsetMax = new Vector2(-8f, -4f);
        }
        return button;
    }

    private static void SetTopButtonPosition(Button button, Vector2 position)
    {
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = position;
    }

    private static TMP_Text CreateText(Transform parent, string objectName, string text, float fontSize, FontStyles style, TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(parent, objectName);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = Color.white;
        label.enableAutoSizing = true;
        label.fontSizeMin = Mathf.Max(10f, fontSize * 0.65f);
        label.fontSizeMax = fontSize;
        label.raycastTarget = false;
        label.outlineWidth = 0.18f;
        label.outlineColor = new Color(0f, 0f, 0f, 0.8f);
        return label;
    }

    private static RectTransform CreateRect(Transform parent, string objectName)
    {
        GameObject child = new(objectName, typeof(RectTransform));
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image CreateImage(Transform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        RectTransform rect = CreateRect(parent, objectName);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static Sprite GetRoundedSprite()
    {
        if (_roundedSprite != null) return _roundedSprite;
        Texture2D texture = new(64, 32, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color[] pixels = new Color[64 * 32];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        _roundedSprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 32f), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(12f, 12f, 12f, 12f));
        _roundedSprite.hideFlags = HideFlags.HideAndDontSave;
        return _roundedSprite;
    }
}
