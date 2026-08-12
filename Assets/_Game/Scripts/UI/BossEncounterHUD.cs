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

    private CanvasGroup _root;
    private TMP_Text _objective;
    private TMP_Text _status;
    private Image _progress;
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
        _root = panel.gameObject.AddComponent<CanvasGroup>();

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
    }

    private void PresentState()
    {
        if (_objective == null || _status == null || _encounter == null)
        {
            if (_root != null) _root.alpha = 0f;
            return;
        }

        // Defeat is replicated by BossDefeatController, so this hides the objective panel
        // at the same time for both Host and Client instead of leaving stale combat guidance.
        if (_defeatController != null && _defeatController.IsDefeated)
        {
            _root.alpha = 0f;
            return;
        }

        _root.alpha = 1f;
        if (TryPresentLocalDownedStatus()) return;

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

    private bool TryPresentLocalDownedStatus()
    {
        if (_respawn == null || NetworkManager.Singleton == null ||
            _respawn.CountdownTarget != NetworkManager.Singleton.LocalClientId)
            return false;

        _objective.text = "You have fallen";
        _status.text = $"Spam E to respawn faster. Respawning in {_respawn.CountdownRemaining:0.0} seconds.";
        _progress.fillAmount = 0f;
        return true;
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
}
