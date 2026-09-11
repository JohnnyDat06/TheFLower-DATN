using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Creates the boss objective and revive presentation on the shared gameplay Canvas.</summary>
public sealed class BossEncounterHUD : MonoBehaviour
{
    private const ulong NoClient = ulong.MaxValue;
    private const float ObjectivePanelBottomOffset = 28f;

    private CanvasGroup _guidanceRoot;
    private CanvasGroup _respawnOverlay;
    private TMP_Text _objective;
    private TMP_Text _status;
    private Image _progress;
    private Image _respawnCountdownRing;
    private TMP_Text _respawnKeyText;
    private TMP_Text _respawnCountdownText;
    private Sprite _respawnRingSprite;
    private float _respawnKeyPulseStartedAt = float.NegativeInfinity;
    private float _searchTimer;
    private BossEncounterManager _encounter;
    private BossRespawnPolicy _respawn;
    private BossPhaseController _phaseController;
    private RuneManager _runeManager;
    private SealManager _sealManager;
    private BossStunController _stunController;
    private BossCoreController _coreController;
    private DualCoreInteractionController _dualCoreController;
    private DualRuneChallengeController _dualRuneChallenge;
    private BossDefeatController _defeatController;
    private bool _uiVisible = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterSceneLoad()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        Install(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode _) => Install(scene);

    private static void Install(Scene scene)
    {
        if (scene.name != Constants.Scenes.BOSS_FINAL || Object.FindFirstObjectByType<BossEncounterHUD>() != null) return;
        GameObject root = new("BossEncounterHUD");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<BossEncounterHUD>();
    }

    private void Awake()
    {
        BuildInterface();
    }

    private void OnEnable()
    {
        PlayerHealthHUDRemake.GameplayHudVisibilityChanged += HandleGameplayHudVisibilityChanged;
        ApplyGameplayHudVisibility(PlayerHealthHUDRemake.IsGameplayHudVisible);
    }

    private void OnDisable()
    {
        PlayerHealthHUDRemake.GameplayHudVisibilityChanged -= HandleGameplayHudVisibilityChanged;
    }

    private void OnDestroy()
    {
        if (_respawnRingSprite == null) return;

        if (_respawnRingSprite.texture != null)
            Destroy(_respawnRingSprite.texture);
        Destroy(_respawnRingSprite);
    }

    private void Update()
    {
        _searchTimer -= Time.unscaledDeltaTime;
        if (_searchTimer <= 0f)
        {
            _searchTimer = 0.25f;
            _encounter = BossEncounterManager.Instance;
            if (_respawn == null) _respawn = Object.FindFirstObjectByType<BossRespawnPolicy>();
            CacheCombatControllers();
        }

        PresentState();
    }

    private void BuildInterface()
    {
        GameObject canvasObject = new("BossEncounterCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 125;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        RectTransform panel = CreateRect(canvasObject.transform, "BossObjectivePanel");
        panel.anchorMin = new Vector2(0.5f, 0f);
        panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, ObjectivePanelBottomOffset);
        panel.sizeDelta = new Vector2(620f, 118f);
        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.04f, 0.025f, 0.09f, 0.88f);
        _guidanceRoot = panel.gameObject.AddComponent<CanvasGroup>();

        _objective = CreateText(panel, "Objective", 25f, FontStyles.Bold, new Vector2(0f, -12f), new Vector2(580f, 36f));
        _status = CreateText(panel, "Status", 18f, FontStyles.Normal, new Vector2(0f, -53f), new Vector2(580f, 32f));
        _status.color = new Color(1f, 0.86f, 0.35f, 1f);

        RectTransform bar = CreateRect(panel, "ReviveProgress");
        bar.anchorMin = new Vector2(0.5f, 0f);
        bar.anchorMax = new Vector2(0.5f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, 12f);
        bar.sizeDelta = new Vector2(500f, 12f);
        Image barBackground = bar.gameObject.AddComponent<Image>();
        barBackground.color = new Color(0f, 0f, 0f, 0.7f);
        RectTransform fill = CreateRect(bar, "Fill");
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = new Vector2(2f, 2f);
        fill.offsetMax = new Vector2(-2f, -2f);
        _progress = fill.gameObject.AddComponent<Image>();
        _progress.color = new Color(0.22f, 0.94f, 0.62f, 1f);
        _progress.type = Image.Type.Filled;
        _progress.fillMethod = Image.FillMethod.Horizontal;
        _progress.fillAmount = 0f;

        BuildRespawnOverlay(canvasObject.transform);
    }

    private void PresentState()
    {
        if (!_uiVisible)
        {
            SetGuidanceVisible(false);
            SetRespawnOverlayVisible(false);
            return;
        }

        if (_objective == null || _status == null || _encounter == null)
        {
            SetGuidanceVisible(false);
            SetRespawnOverlayVisible(false);
            return;
        }

        // Defeat is replicated by BossDefeatController, so this hides the objective panel
        // at the same time for both Host and Client instead of leaving stale combat guidance.
        if (_defeatController != null && _defeatController.IsDefeated)
        {
            SetGuidanceVisible(false);
            SetRespawnOverlayVisible(false);
            return;
        }

        bool localEnteredArena = HasLocalPlayerEnteredArena();
        bool localPlayerIsDown = IsLocalPlayerRespawning();
        PresentRespawnOverlay(localPlayerIsDown && _encounter.HasEncounterStarted);

        // Do not show boss instructions while walking through the room approach.
        // The local player's replicated EnterBoss entry is the only authority for
        // this visual decision, so Host and Client never show it for the other player.
        if (!localEnteredArena || localPlayerIsDown)
        {
            SetGuidanceVisible(false);
            return;
        }

        SetGuidanceVisible(true);

        switch (_encounter.State)
        {
            case BossEncounterManager.EncounterState.WaitingForPlayers:
                _objective.text = "Gather at the Core Gate";
                _status.text = "Wait for both players to enter the arena";
                break;
            case BossEncounterManager.EncounterState.Intro:
                _objective.text = "Destroy the Warden Core";
                _status.text = "The arena is sealed";
                break;
            case BossEncounterManager.EncounterState.Active:
                _objective.text = "Use the arena mechanism to defeat the boss";
                PresentActiveStatus();
                if (!IsLocalReviveMessageActive()) PresentCombatGuidance();
                break;
            case BossEncounterManager.EncounterState.WipeReset:
                _objective.text = "Both players have fallen";
                _status.text = "Resetting the arena...";
                break;
            case BossEncounterManager.EncounterState.Victory:
                _objective.text = "Objective complete";
                _status.text = "The Warden Core has been destroyed";
                break;
        }
    }

    private void HandleGameplayHudVisibilityChanged(bool visible)
    {
        ApplyGameplayHudVisibility(visible);
    }

    private void ApplyGameplayHudVisibility(bool visible)
    {
        _uiVisible = visible;
        SetGuidanceVisible(visible);
        SetRespawnOverlayVisible(visible);
    }

    private void SetGuidanceVisible(bool visible)
    {
        if (_guidanceRoot == null) return;
        _guidanceRoot.alpha = visible ? 1f : 0f;
        _guidanceRoot.interactable = false;
        _guidanceRoot.blocksRaycasts = false;
    }

    private void PresentActiveStatus()
    {
        _progress.fillAmount = 0f;
        if (_respawn == null || NetworkManager.Singleton == null)
        {
            _status.text = "Coordinate, dodge attacks, and activate the arena mechanism";
            return;
        }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (_respawn.CountdownTarget == localId)
        {
            _status.text = $"You are down. Respawning in {_respawn.CountdownRemaining:0.0} seconds.";
            return;
        }
        if (_respawn.Reviver == localId)
        {
            _progress.fillAmount = _respawn.ReviveProgress;
            _status.text = $"Reviving teammate... {_respawn.ReviveProgress * 100f:0}%";
            return;
        }
        if (_respawn.ReviveTarget == localId)
        {
            _progress.fillAmount = _respawn.ReviveProgress;
            _status.text = $"Teammate is reviving you... {_respawn.ReviveProgress * 100f:0}%";
            return;
        }
        if (_respawn.TryGetLocalReviveCandidate(out ulong targetId) && targetId != NoClient)
        {
            _status.text = "Hold Interact to revive your teammate (5 seconds, restores 60% HP)";
            return;
        }
        _status.text = "Coordinate, dodge attacks, and activate the arena mechanism";
    }

    private bool HasLocalPlayerEnteredArena()
    {
        return NetworkManager.Singleton != null &&
               _encounter != null &&
               _encounter.HasPlayerEntered(NetworkManager.Singleton.LocalClientId);
    }

    private bool IsLocalPlayerRespawning()
    {
        return _respawn != null &&
               NetworkManager.Singleton != null &&
               _respawn.CountdownTarget == NetworkManager.Singleton.LocalClientId;
    }

    private void BuildRespawnOverlay(Transform canvasTransform)
    {
        RectTransform overlay = CreateRect(canvasTransform, "BossRespawnOverlay");
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = Vector2.zero;
        overlay.offsetMax = Vector2.zero;
        Image dimmer = overlay.gameObject.AddComponent<Image>();
        dimmer.color = new Color(0f, 0f, 0f, 0.6f);
        dimmer.raycastTarget = false;
        _respawnOverlay = overlay.gameObject.AddComponent<CanvasGroup>();
        _respawnOverlay.alpha = 0f;
        _respawnOverlay.interactable = false;
        _respawnOverlay.blocksRaycasts = false;

        RectTransform ring = CreateRect(overlay, "RespawnCountdownRing");
        ring.anchorMin = new Vector2(0.5f, 0.5f);
        ring.anchorMax = new Vector2(0.5f, 0.5f);
        ring.pivot = new Vector2(0.5f, 0.5f);
        ring.sizeDelta = new Vector2(264f, 264f);

        _respawnRingSprite = CreateRingSprite();
        Image ringBackground = ring.gameObject.AddComponent<Image>();
        ringBackground.sprite = _respawnRingSprite;
        ringBackground.color = new Color(0.12f, 0.12f, 0.15f, 0.85f);

        RectTransform fill = CreateRect(ring, "RespawnCountdownFill");
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        _respawnCountdownRing = fill.gameObject.AddComponent<Image>();
        _respawnCountdownRing.sprite = _respawnRingSprite;
        _respawnCountdownRing.color = new Color(1f, 0.78f, 0.24f, 1f);
        _respawnCountdownRing.type = Image.Type.Filled;
        _respawnCountdownRing.fillMethod = Image.FillMethod.Radial360;
        _respawnCountdownRing.fillOrigin = (int)Image.Origin360.Top;
        _respawnCountdownRing.fillClockwise = false;

        _respawnKeyText = CreateCenteredText(ring, "RespawnKey", "E", 94f, FontStyles.Bold,
            Vector2.zero, new Vector2(130f, 118f));
        _respawnKeyText.color = Color.white;

        _respawnCountdownText = CreateCenteredText(overlay, "RespawnCountdownText", "", 28f,
            FontStyles.Bold, new Vector2(0f, -182f), new Vector2(560f, 42f));
        _respawnCountdownText.color = new Color(1f, 0.9f, 0.62f, 1f);

        TMP_Text prompt = CreateCenteredText(overlay, "RespawnPrompt", "Press E to respawn faster", 20f,
            FontStyles.Normal, new Vector2(0f, -222f), new Vector2(560f, 36f));
        prompt.color = new Color(1f, 1f, 1f, 0.9f);
    }

    private void PresentRespawnOverlay(bool visible)
    {
        SetRespawnOverlayVisible(visible);
        if (!visible || _respawn == null || _respawnCountdownRing == null ||
            _respawnKeyText == null || _respawnCountdownText == null)
            return;

        float duration = Mathf.Max(0.01f, _respawn.CountdownDuration);
        _respawnCountdownRing.fillAmount = Mathf.Clamp01(_respawn.CountdownRemaining / duration);
        _respawnCountdownText.text = $"Respawning in {_respawn.CountdownRemaining:0.0}s";

        if (WasLocalRespawnKeyPressed())
            _respawnKeyPulseStartedAt = Time.unscaledTime;

        float elapsed = Time.unscaledTime - _respawnKeyPulseStartedAt;
        float pulse = elapsed >= 0f && elapsed < 0.22f
            ? Mathf.Sin(elapsed / 0.22f * Mathf.PI)
            : 0f;
        _respawnKeyText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.22f, pulse);
    }

    private void SetRespawnOverlayVisible(bool visible)
    {
        if (_respawnOverlay == null) return;
        _respawnOverlay.alpha = visible ? 1f : 0f;
        _respawnOverlay.interactable = false;
        _respawnOverlay.blocksRaycasts = false;
        if (!visible && _respawnKeyText != null)
            _respawnKeyText.rectTransform.localScale = Vector3.one;
    }

    private static Sprite CreateRingSprite()
    {
        const int size = 128;
        const float innerRadius = 0.62f;
        const float outerRadius = 0.94f;
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            name = "BossRespawnRing"
        };

        Color clear = new(1f, 1f, 1f, 0f);
        Color solid = Color.white;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float normalizedX = (x + 0.5f) / size * 2f - 1f;
            float normalizedY = (y + 0.5f) / size * 2f - 1f;
            float radius = Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
            texture.SetPixel(x, y, radius >= innerRadius && radius <= outerRadius ? solid : clear);
        }

        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private bool WasLocalRespawnKeyPressed()
    {
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject == null) return false;
        return NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent<PlayerInputHandler>(out var input) &&
               input.InteractPressed;
    }

    private void CacheCombatControllers()
    {
        _phaseController ??= Object.FindFirstObjectByType<BossPhaseController>();
        _runeManager ??= Object.FindFirstObjectByType<RuneManager>();
        _sealManager ??= Object.FindFirstObjectByType<SealManager>();
        _stunController ??= Object.FindFirstObjectByType<BossStunController>();
        _coreController ??= Object.FindFirstObjectByType<BossCoreController>();
        _dualCoreController ??= Object.FindFirstObjectByType<DualCoreInteractionController>();
        _dualRuneChallenge ??= Object.FindFirstObjectByType<DualRuneChallengeController>();
        _defeatController ??= Object.FindFirstObjectByType<BossDefeatController>();
    }

    private bool IsLocalReviveMessageActive()
    {
        if (_respawn == null || NetworkManager.Singleton == null) return false;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        return _respawn.CountdownTarget == localId ||
               _respawn.Reviver == localId ||
               _respawn.ReviveTarget == localId ||
               (_respawn.TryGetLocalReviveCandidate(out ulong targetId) && targetId != NoClient);
    }

    private void PresentCombatGuidance()
    {
        _objective.text = GetPhaseObjective();
        _status.text = GetCombatInstruction();
    }

    private string GetPhaseObjective()
    {
        if (_defeatController != null && _defeatController.IsDefeated)
            return "Objective complete";

        return _phaseController?.CurrentPhase switch
        {
            BossCombatPhase.PhaseOne => "Phase 1. Break the defense.",
            BossCombatPhase.PhaseTwo => "Phase 2. The Guardian is enraged.",
            BossCombatPhase.PhaseThree => "Phase 3. Complete the Diamond challenge.",
            _ => "Use the arena mechanism to defeat the boss."
        };
    }

    private string GetCombatInstruction()
    {
        if (_defeatController != null && _defeatController.IsDefeated)
            return "The exit is open. Both players can proceed to the Exit Door.";

        if (_coreController != null && _coreController.State == BossCoreState.Exposed)
        {
            if (_dualCoreController != null && _dualCoreController.PendingPointId >= 0)
                return "One Core Point is active. Your teammate should activate the other Core Point now.";

            return "The boss is stunned. Both players should activate the two Core Points together.";
        }

        if (_stunController != null && _stunController.IsStunned)
            return "The boss is stunned. Move to the Core and prepare a coordinated strike.";

        if (_phaseController != null &&
            _phaseController.CurrentPhase == BossCombatPhase.PhaseThree &&
            _dualRuneChallenge != null &&
            !_dualRuneChallenge.IsChallengeComplete)
        {
            int chargedRunes = CountRunes(RuneState.Charged);
            return chargedRunes == 0
                ? "Guide a Shockwave through both Diamonds at nearly the same time."
                : "One Diamond is charged. Guide a Shockwave through the other Diamond now.";
        }

        if (CountSeals(SealState.Ready) > 0)
            return "A Diamond is charged. Go to the matching Seal and press Interact.";

        if (CountSeals(SealState.Active) > 0)
            return "One Seal is active. Your teammate should activate the remaining Seal.";

        if (CountRunes(RuneState.Charged) > 0)
            return "A Diamond is charged. Quickly reach the matching Seal.";

        return "Dodge the boss attacks and guide a Shockwave through a Diamond to charge it.";
    }

    private int CountRunes(RuneState state)
    {
        if (_runeManager == null || _runeManager.Runes == null) return 0;

        int count = 0;
        foreach (RuneController rune in _runeManager.Runes)
            if (rune != null && rune.State == state) count++;
        return count;
    }

    private int CountSeals(SealState state)
    {
        if (_sealManager == null || _sealManager.Seals == null) return 0;

        int count = 0;
        foreach (SealController seal in _sealManager.Seals)
            if (seal != null && seal.State == state) count++;
        return count;
    }

    private static RectTransform CreateRect(Transform parent, string name)
    {
        GameObject item = new(name, typeof(RectTransform));
        item.transform.SetParent(parent, false);
        return item.GetComponent<RectTransform>();
    }

    private static TMP_Text CreateText(RectTransform parent, string name, float size, FontStyles style, Vector2 position, Vector2 dimensions)
    {
        RectTransform rect = CreateRect(parent, name);
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.enableWordWrapping = true;
        return text;
    }

    private static TMP_Text CreateCenteredText(
        RectTransform parent,
        string name,
        string content,
        float size,
        FontStyles style,
        Vector2 position,
        Vector2 dimensions)
    {
        RectTransform rect = CreateRect(parent, name);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }
}
