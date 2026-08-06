using Networking.LobbySystem;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the two-player jungle health HUD at runtime and presents replicated PlayerHealth data.
/// Gameplay health remains server-authoritative; this component only reads and animates it locally.
/// </summary>
public sealed class PlayerHealthHUDRemake : MonoBehaviour
{
    private const float SearchInterval = 0.35f;
    private const float SmoothSpeed = 8f;

    private sealed class PlayerBar
    {
        public GameObject Root;
        public RectTransform Fill;
        public TMP_Text Name;
        public TMP_Text Value;
        public GameObject WorldNameRoot;
        public RectTransform WorldNameRect;
        public TMP_Text WorldName;
        public PlayerHealth Health;
        public LobbyPlayerState State;
        public Vector3 WorldNameOffset = Vector3.up * 1.8f;
        public float Target = 1f;
        public float Displayed = 1f;
        public bool FillFromRight;
    }

    private static Sprite s_roundedSprite;

    private PlayerBar _host;
    private PlayerBar _client;
    private RectTransform _canvasRect;
    private Camera _worldCamera;
    private float _searchTimer;
    private bool _worldNameplatesHiddenByMenu;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForGameplayScenes()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        Install(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => Install(scene);

    private static void Install(Scene scene)
    {
        if (!IsGameplayScene(scene) || Object.FindFirstObjectByType<PlayerHealthHUDRemake>() != null) return;

        GameObject root = new("PlayerHealthHUD_Remake");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<PlayerHealthHUDRemake>();
    }

    private static bool IsGameplayScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return false;
        string name = scene.name;
        return name == Constants.Scenes.LEVEL_01
            || name == Constants.Scenes.LEVEL_02
            || name == Constants.Scenes.LEVEL_03
            || name == Constants.Scenes.LEVEL_04
            || name == Constants.Scenes.BOSS_FINAL;
    }

    private void Awake()
    {
        DisableLegacyHealthController();
        DisableLegacyHealthBars();
        BuildInterface();
    }

    private void OnEnable()
    {
        EventBus.OnGamePaused += HideWorldNameplates;
        EventBus.OnGameResumed += ShowWorldNameplates;
    }

    private void Update()
    {
        _searchTimer -= Time.unscaledDeltaTime;
        if (_searchTimer <= 0f)
        {
            _searchTimer = SearchInterval;
            RefreshPlayerBindings();
        }

        AnimateBar(_host);
        AnimateBar(_client);

        if (_worldNameplatesHiddenByMenu)
        {
            SetWorldNameplateActive(_host, false);
            SetWorldNameplateActive(_client, false);
            return;
        }

        UpdateWorldNameplate(_host);
        UpdateWorldNameplate(_client);
    }

    private void OnDestroy()
    {
        EventBus.OnGamePaused -= HideWorldNameplates;
        EventBus.OnGameResumed -= ShowWorldNameplates;
        Unbind(_host);
        Unbind(_client);
    }

    private void HideWorldNameplates()
    {
        _worldNameplatesHiddenByMenu = true;
        SetWorldNameplateActive(_host, false);
        SetWorldNameplateActive(_client, false);
    }

    private void ShowWorldNameplates()
    {
        _worldNameplatesHiddenByMenu = false;
    }

    private static void SetWorldNameplateActive(PlayerBar bar, bool active)
    {
        if (bar?.WorldNameRoot != null)
            bar.WorldNameRoot.SetActive(active);
    }

    private void BuildInterface()
    {
        GameObject canvasObject = new("PlayerHealthHUD_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasRect = canvasObject.GetComponent<RectTransform>();
        _host = CreateBar(_canvasRect, true, Resources.Load<Sprite>("UI/PlayerHUD/PlayerHealthFrame_Host"));
        _client = CreateBar(_canvasRect, false, Resources.Load<Sprite>("UI/PlayerHUD/PlayerHealthFrame_Client"));
    }

    private static PlayerBar CreateBar(RectTransform parent, bool host, Sprite frameSprite)
    {
        Vector2 anchor = host ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        GameObject rootObject = new(host ? "HostHealthBarRemake" : "ClientHealthBarRemake", typeof(RectTransform), typeof(CanvasGroup));
        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.SetParent(parent, false);
        root.anchorMin = anchor;
        root.anchorMax = anchor;
        root.pivot = anchor;
        root.anchoredPosition = host ? new Vector2(16f, -12f) : new Vector2(-16f, -12f);
        root.sizeDelta = new Vector2(500f, 108f);
        rootObject.SetActive(false);

        Image frame = CreateImage(root, "Frame", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.white);
        frame.sprite = frameSprite;
        frame.preserveAspect = true;
        frame.raycastTarget = false;

        RectTransform slotArea = CreateRect(root, "HealthFillArea");
        slotArea.anchorMin = Vector2.zero;
        slotArea.anchorMax = Vector2.one;
        slotArea.offsetMin = host ? new Vector2(114f, 37f) : new Vector2(18f, 37f);
        slotArea.offsetMax = host ? new Vector2(-17f, -42f) : new Vector2(-106f, -42f);
        slotArea.gameObject.AddComponent<RectMask2D>();

        RectTransform fill = CreateRect(slotArea, "HealthFill");
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.sprite = GetRoundedSprite();
        fillImage.type = Image.Type.Sliced;
        fillImage.color = new Color(1f, 0.20f, 0.19f, 0.96f);
        fillImage.raycastTarget = false;
        Mask roundedMask = fill.gameObject.AddComponent<Mask>();
        roundedMask.showMaskGraphic = true;

        Image shine = CreateImage(fill, "HealthShine", new Vector2(0f, 0.56f), Vector2.one, new Vector2(3f, 0f), new Vector2(-3f, -2f), new Color(1f, 0.64f, 0.35f, 0.68f));
        shine.sprite = GetRoundedSprite();
        shine.type = Image.Type.Sliced;
        shine.raycastTarget = false;

        TMP_Text name = CreateHudTextBadge(root, "PlayerNameBadge", host, true, host ? "HOST" : "CLIENT");
        TMP_Text value = CreateHudTextBadge(root, "HealthValueBadge", host, false, "100 / 100");

        CreateWorldNameplate(parent, host, out GameObject worldNameRoot, out RectTransform worldNameRect, out TMP_Text worldName);

        return new PlayerBar
        {
            Root = rootObject,
            Fill = fill,
            Name = name,
            Value = value,
            WorldNameRoot = worldNameRoot,
            WorldNameRect = worldNameRect,
            WorldName = worldName,
            FillFromRight = !host
        };
    }

    private void RefreshPlayerBindings()
    {
        if (_host == null || _client == null) return;
        if (_host.Health == null || !_host.Health.IsSpawned) Unbind(_host);
        if (_client.Health == null || !_client.Health.IsSpawned) Unbind(_client);

        ulong serverId = NetworkManager.ServerClientId;
        foreach (PlayerHealth health in Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (!health.IsSpawned) continue;
            PlayerBar target = health.OwnerClientId == serverId ? _host : _client;
            if (target.Health == health) continue;
            Bind(target, health);
        }
    }

    private void Bind(PlayerBar bar, PlayerHealth health)
    {
        Unbind(bar);
        bar.Health = health;
        bar.Health.OnHealthChanged += bar == _host ? HandleHostHealthChanged : HandleClientHealthChanged;
        bar.Target = Mathf.Clamp01(health.CurrentHealth / Mathf.Max(1f, health.MaxHealth));
        bar.Displayed = bar.Target;
        bar.WorldNameOffset = CalculateWorldNameOffset(health.transform);

        if (health.TryGetComponent(out LobbyPlayerState state))
        {
            bar.State = state;
            if (bar == _host) state.PlayerName.OnValueChanged += HandleHostNameChanged;
            else state.PlayerName.OnValueChanged += HandleClientNameChanged;
            string playerName = state.PlayerName.Value.ToString();
            if (string.IsNullOrWhiteSpace(playerName) && health.IsOwner)
                playerName = PlayerPrefs.GetString(Constants.PlayerPrefsKeys.PLAYER_NAME, string.Empty);
            SetPlayerName(bar, playerName);
        }
        else
        {
            string playerName = health.IsOwner
                ? PlayerPrefs.GetString(Constants.PlayerPrefsKeys.PLAYER_NAME, string.Empty)
                : string.Empty;
            SetPlayerName(bar, playerName);
        }

        UpdateValueText(bar);
        bar.Root.SetActive(true);
        bar.WorldNameRoot.SetActive(true);
    }

    private void Unbind(PlayerBar bar)
    {
        if (bar == null) return;
        if (bar.Health != null)
            bar.Health.OnHealthChanged -= bar == _host ? HandleHostHealthChanged : HandleClientHealthChanged;
        if (bar.State != null)
        {
            if (bar == _host) bar.State.PlayerName.OnValueChanged -= HandleHostNameChanged;
            else bar.State.PlayerName.OnValueChanged -= HandleClientNameChanged;
        }

        bar.Health = null;
        bar.State = null;
        if (bar.Root != null) bar.Root.SetActive(false);
        if (bar.WorldNameRoot != null) bar.WorldNameRoot.SetActive(false);
    }

    private static void AnimateBar(PlayerBar bar)
    {
        if (bar?.Root == null || !bar.Root.activeSelf) return;
        bar.Displayed = Mathf.MoveTowards(bar.Displayed, bar.Target, Time.unscaledDeltaTime * SmoothSpeed);
        if (bar.FillFromRight)
        {
            bar.Fill.anchorMin = new Vector2(1f - bar.Displayed, 0f);
            bar.Fill.anchorMax = Vector2.one;
        }
        else
        {
            bar.Fill.anchorMin = Vector2.zero;
            bar.Fill.anchorMax = new Vector2(bar.Displayed, 1f);
        }
        bar.Fill.offsetMin = Vector2.zero;
        bar.Fill.offsetMax = Vector2.zero;
    }

    private void HandleHostHealthChanged(float current, float max) => SetHealth(_host, current, max);
    private void HandleClientHealthChanged(float current, float max) => SetHealth(_client, current, max);
    private void HandleHostNameChanged(FixedString32Bytes oldValue, FixedString32Bytes newValue) => SetPlayerName(_host, newValue.ToString());
    private void HandleClientNameChanged(FixedString32Bytes oldValue, FixedString32Bytes newValue) => SetPlayerName(_client, newValue.ToString());

    private static void SetHealth(PlayerBar bar, float current, float max)
    {
        bar.Target = Mathf.Clamp01(current / Mathf.Max(1f, max));
        UpdateValueText(bar);
    }

    private static void UpdateValueText(PlayerBar bar)
    {
        if (bar?.Value == null || bar.Health == null) return;
        bar.Value.text = $"{Mathf.CeilToInt(bar.Health.CurrentHealth)} / {Mathf.CeilToInt(bar.Health.MaxHealth)}";
    }

    private static void SetPlayerName(PlayerBar bar, string value)
    {
        if (bar == null) return;
        string playerName = string.IsNullOrWhiteSpace(value) ? "PLAYER" : value.Trim();
        if (bar.Name != null) bar.Name.text = playerName;
        if (bar.WorldName != null) bar.WorldName.text = playerName;
        if (bar.WorldNameRect != null)
            bar.WorldNameRect.sizeDelta = new Vector2(Mathf.Clamp(92f + playerName.Length * 9f, 132f, 260f), 42f);
    }

    private void UpdateWorldNameplate(PlayerBar bar)
    {
        if (bar?.WorldNameRoot == null || bar.Health == null || !bar.Health.IsSpawned)
        {
            if (bar?.WorldNameRoot != null) bar.WorldNameRoot.SetActive(false);
            return;
        }

        if (_worldCamera == null || !_worldCamera.isActiveAndEnabled)
            _worldCamera = Camera.main;
        if (_worldCamera == null || _canvasRect == null)
        {
            bar.WorldNameRoot.SetActive(false);
            return;
        }

        Vector3 screenPoint = _worldCamera.WorldToScreenPoint(bar.Health.transform.position + bar.WorldNameOffset);
        bool isVisible = screenPoint.z > 0f;
        bar.WorldNameRoot.SetActive(isVisible);
        if (!isVisible) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPoint, null, out Vector2 localPoint))
            bar.WorldNameRect.anchoredPosition = localPoint;
    }

    private static Vector3 CalculateWorldNameOffset(Transform player)
    {
        float highestPoint = player.position.y + 1.4f;
        foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>())
        {
            if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                highestPoint = Mathf.Max(highestPoint, renderer.bounds.max.y);
        }

        float height = Mathf.Clamp(highestPoint - player.position.y + 0.28f, 1.3f, 3.2f);
        return Vector3.up * height;
    }

    private static void CreateWorldNameplate(RectTransform parent, bool host, out GameObject root, out RectTransform rootRect, out TMP_Text label)
    {
        rootRect = CreateRect(parent, host ? "HostWorldName" : "ClientWorldName");
        root = rootRect.gameObject;
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.sizeDelta = new Vector2(150f, 42f);

        Image outline = root.AddComponent<Image>();
        outline.sprite = GetRoundedSprite();
        outline.type = Image.Type.Sliced;
        outline.color = host ? new Color(1f, 0.72f, 0.18f, 0.96f) : new Color(0.48f, 0.93f, 0.77f, 0.96f);
        outline.raycastTarget = false;
        Shadow shadow = root.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0.08f, 0.06f, 0.72f);
        shadow.effectDistance = new Vector2(0f, -3f);

        Image background = CreateImage(rootRect, "Background", Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f), new Color(0.02f, 0.20f, 0.16f, 0.94f));
        background.sprite = GetRoundedSprite();
        background.type = Image.Type.Sliced;
        background.raycastTarget = false;

        label = CreateLabel(rootRect, host ? "HOST" : "CLIENT", 19f, FontStyles.Bold);
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.enableAutoSizing = true;
        label.fontSizeMin = 13f;
        label.fontSizeMax = 19f;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(12f, 5f);
        label.rectTransform.offsetMax = new Vector2(-12f, -5f);
        root.SetActive(false);
    }

    private static TMP_Text CreateHudTextBadge(RectTransform parent, string objectName, bool host, bool isName, string text)
    {
        RectTransform badge = CreateRect(parent, objectName);
        Vector2 anchor = host ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        if (!isName) anchor = host ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        badge.anchorMin = anchor;
        badge.anchorMax = anchor;
        badge.pivot = anchor;
        badge.anchoredPosition = isName
            ? (host ? new Vector2(112f, -2f) : new Vector2(-112f, -2f))
            : (host ? new Vector2(-12f, -2f) : new Vector2(12f, -2f));
        badge.sizeDelta = isName ? new Vector2(230f, 32f) : new Vector2(132f, 32f);

        Image border = badge.gameObject.AddComponent<Image>();
        border.sprite = GetRoundedSprite();
        border.type = Image.Type.Sliced;
        border.color = new Color(1f, 0.69f, 0.16f, 0.98f);
        border.raycastTarget = false;

        Image background = CreateImage(badge, "Background", Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f), new Color(0.025f, 0.13f, 0.11f, 0.96f));
        background.sprite = GetRoundedSprite();
        background.type = Image.Type.Sliced;
        background.raycastTarget = false;

        TMP_Text label = CreateLabel(badge, text, isName ? 18f : 17f, FontStyles.Bold);
        label.alignment = TextAlignmentOptions.Center;
        label.color = isName ? new Color(1f, 0.97f, 0.82f, 1f) : new Color(1f, 0.86f, 0.26f, 1f);
        label.enableAutoSizing = true;
        label.fontSizeMin = isName ? 13f : 14f;
        label.fontSizeMax = isName ? 18f : 17f;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(8f, 3f);
        label.rectTransform.offsetMax = new Vector2(-8f, -3f);

        Shadow textShadow = label.gameObject.AddComponent<Shadow>();
        textShadow.effectColor = new Color(0f, 0f, 0f, 0.88f);
        textShadow.effectDistance = new Vector2(1.5f, -1.5f);
        textShadow.useGraphicAlpha = true;
        return label;
    }

    private static Sprite GetRoundedSprite()
    {
        if (s_roundedSprite != null) return s_roundedSprite;

        const int width = 64;
        const int height = 32;
        const float radius = 15f;
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false)
        {
            name = "RuntimeRoundedUISprite",
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
        s_roundedSprite.name = "RuntimeRoundedUISprite";
        s_roundedSprite.hideFlags = HideFlags.HideAndDontSave;
        return s_roundedSprite;
    }

    private void DisableLegacyHealthBars()
    {
        foreach (Transform candidate in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.gameObject.scene != gameObject.scene) continue;
            if (candidate.name is "HealthHost" or "HealthClient") candidate.gameObject.SetActive(false);
        }
    }

    private void DisableLegacyHealthController()
    {
        foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour == null || behaviour.gameObject.scene != gameObject.scene) continue;
            if (behaviour.GetType().Name == "PlayerHUDController") behaviour.enabled = false;
        }
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
        return image;
    }

    private static TMP_Text CreateLabel(Transform parent, string text, float size, FontStyles style)
    {
        RectTransform rect = CreateRect(parent, "Label");
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = new Color(1f, 0.95f, 0.69f, 1f);
        label.raycastTarget = false;
        return label;
    }
}
