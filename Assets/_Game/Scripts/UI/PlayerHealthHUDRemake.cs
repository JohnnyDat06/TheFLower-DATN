using Networking.LobbySystem;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the two-player jungle health HUD at runtime and presents replicated PlayerHealth data.
/// Gameplay health remains server-authoritative; this component only reads and animates it locally.
/// </summary>
public sealed class PlayerHealthHUDRemake : MonoBehaviour
{
    public static bool IsGameplayHudVisible { get; private set; } = true;
    public static event System.Action<bool> GameplayHudVisibilityChanged;

    private const float SearchInterval = 0.35f;
    private const float SmoothSpeed = 8f;

    private sealed class PlayerBar
    {
        public GameObject Root;
        public RectTransform Fill;
        public Image Frame;
        public TMP_Text Name;
        public TMP_Text Value;
        public PlayerHealth Health;
        public LobbyPlayerState State;
        public LobbyCharacterAppearance Appearance;
        public float Target = 1f;
        public float Displayed = 1f;
        public bool FillFromRight;
        public bool IsHost;
        public int DisplayedCharacterIndex = -1;
    }

    private static Sprite s_roundedSprite;

    private PlayerBar _host;
    private PlayerBar _client;
    private RectTransform _canvasRect;
    private float _searchTimer;
    private bool _healthBarsVisible = true;

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
        SetGameplayHudVisibility(true);
    }

    private void Update()
    {
        HandleHealthBarVisibilityToggle();

        _searchTimer -= Time.unscaledDeltaTime;
        if (_searchTimer <= 0f)
        {
            _searchTimer = SearchInterval;
            RefreshPlayerBindings();
        }

        AnimateBar(_host);
        AnimateBar(_client);

    }

    /// <summary>
    /// Toggles only the local health-bar presentation. This intentionally bypasses
    /// PlayerInputHandler so movement and gameplay input remain untouched.
    /// </summary>
    private void HandleHealthBarVisibilityToggle()
    {
        if (Keyboard.current == null || !Keyboard.current.f1Key.wasPressedThisFrame) return;

        _healthBarsVisible = !_healthBarsVisible;
        SetHealthBarVisibility(_host);
        SetHealthBarVisibility(_client);
        SetGameplayHudVisibility(_healthBarsVisible);
    }

    private static void SetGameplayHudVisibility(bool visible)
    {
        IsGameplayHudVisible = visible;
        GameplayHudVisibilityChanged?.Invoke(visible);
    }

    private void SetHealthBarVisibility(PlayerBar bar)
    {
        if (bar?.Root == null) return;

        bar.Root.SetActive(_healthBarsVisible && bar.Health != null && bar.Health.IsSpawned);
    }

    private void OnDestroy()
    {
        Unbind(_host);
        Unbind(_client);
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
        _host = CreateBar(_canvasRect, true);
        _client = CreateBar(_canvasRect, false);
    }

    private static PlayerBar CreateBar(RectTransform parent, bool host)
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
        frame.sprite = GetHealthFrameSprite(host, LobbyPlayerState.DefaultCharacterIndex);
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

        return new PlayerBar
        {
            Root = rootObject,
            Fill = fill,
            Frame = frame,
            Name = name,
            Value = value,
            FillFromRight = !host,
            IsHost = host
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
            LobbyPlayerState state = FindLobbyPlayerState(health);
            LobbyCharacterAppearance appearance = FindLobbyCharacterAppearance(health);

            // PlayerHealth can be visible one frame before LobbyPlayerState
            // is attached/synchronised. Rebind when the state reference changes
            // so a temporary default avatar cannot remain on the HP frame.
            if (target.Health == health && target.State == state && target.Appearance == appearance)
            {
                RefreshCharacterFrame(target);
                continue;
            }
            Bind(target, health);
        }

        // CharacterIndex may arrive before the HUD subscribes to its change event.
        // Re-read the authoritative value so a stale frame can never remain visible.
        RefreshCharacterFrame(_host);
        RefreshCharacterFrame(_client);
    }

    private void Bind(PlayerBar bar, PlayerHealth health)
    {
        Unbind(bar);
        bar.Health = health;
        bar.Health.OnHealthChanged += bar == _host ? HandleHostHealthChanged : HandleClientHealthChanged;
        bar.Target = Mathf.Clamp01(health.CurrentHealth / Mathf.Max(1f, health.MaxHealth));
        bar.Displayed = bar.Target;
        LobbyPlayerState state = FindLobbyPlayerState(health);
        bar.Appearance = FindLobbyCharacterAppearance(health);
        if (state != null)
        {
            bar.State = state;
            if (bar == _host) state.PlayerName.OnValueChanged += HandleHostNameChanged;
            else state.PlayerName.OnValueChanged += HandleClientNameChanged;
            if (bar == _host) state.CharacterIndex.OnValueChanged += HandleHostCharacterChanged;
            else state.CharacterIndex.OnValueChanged += HandleClientCharacterChanged;
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

        RefreshCharacterFrame(bar);
        UpdateValueText(bar);
        SetHealthBarVisibility(bar);
    }

    private static LobbyPlayerState FindLobbyPlayerState(PlayerHealth health)
    {
        if (health == null) return null;
        if (health.TryGetComponent(out LobbyPlayerState state)) return state;

        // Keep the HUD compatible with player variants that place network state on a parent
        // or child object instead of the same root as PlayerHealth.
        state = health.GetComponentInParent<LobbyPlayerState>();
        return state != null ? state : health.GetComponentInChildren<LobbyPlayerState>(true);
    }

    private static LobbyCharacterAppearance FindLobbyCharacterAppearance(PlayerHealth health)
    {
        if (health == null) return null;
        if (health.TryGetComponent(out LobbyCharacterAppearance appearance)) return appearance;

        appearance = health.GetComponentInParent<LobbyCharacterAppearance>();
        return appearance != null ? appearance : health.GetComponentInChildren<LobbyCharacterAppearance>(true);
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
            if (bar == _host) bar.State.CharacterIndex.OnValueChanged -= HandleHostCharacterChanged;
            else bar.State.CharacterIndex.OnValueChanged -= HandleClientCharacterChanged;
        }

        bar.Health = null;
        bar.State = null;
        bar.Appearance = null;
        bar.DisplayedCharacterIndex = -1;
        if (bar.Root != null) bar.Root.SetActive(false);
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
    private void HandleHostCharacterChanged(int oldValue, int newValue) => RefreshCharacterFrame(_host);
    private void HandleClientCharacterChanged(int oldValue, int newValue) => RefreshCharacterFrame(_client);

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
    }

    private void RefreshCharacterFrame(PlayerBar bar)
    {
        if (bar == null) return;

        if (bar.Health != null)
            bar.Appearance = FindLobbyCharacterAppearance(bar.Health);

        if (bar.State == null && bar.Appearance == null) return;

        int characterIndex = bar.Appearance != null &&
            bar.Appearance.AppliedCharacterIndex >= 0
                ? bar.Appearance.AppliedCharacterIndex
                : bar.State != null
                    ? bar.State.CharacterIndex.Value
                    : LobbyPlayerState.DefaultCharacterIndex;
        characterIndex = Mathf.Clamp(characterIndex, 0, LobbyPlayerState.AvailableCharacterCount - 1);
        if (bar.DisplayedCharacterIndex == characterIndex && bar.Frame?.sprite != null) return;

        SetCharacterFrame(bar, characterIndex);
    }

    private static void SetCharacterFrame(PlayerBar bar, int characterIndex)
    {
        if (bar?.Frame == null) return;

        int safeIndex = Mathf.Clamp(
            characterIndex,
            0,
            LobbyPlayerState.AvailableCharacterCount - 1);
        Sprite frame = GetHealthFrameSprite(bar.IsHost, safeIndex);
        if (frame != null) bar.Frame.sprite = frame;
        bar.DisplayedCharacterIndex = safeIndex;
    }

    private static Sprite GetHealthFrameSprite(bool host, int characterIndex)
    {
        int safeIndex = Mathf.Clamp(
            characterIndex,
            0,
            LobbyPlayerState.AvailableCharacterCount - 1);
        string side = host ? "Host" : "Client";
        Sprite frame = Resources.Load<Sprite>($"UI/PlayerHUD/PlayerHealthFrame_{side}_{safeIndex:00}");
        return frame != null
            ? frame
            : Resources.Load<Sprite>($"UI/PlayerHUD/PlayerHealthFrame_{side}");
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
