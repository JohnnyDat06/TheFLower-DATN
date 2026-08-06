using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Creates the boss objective and revive presentation on the shared gameplay Canvas.</summary>
public sealed class BossEncounterHUD : MonoBehaviour
{
    private const ulong NoClient = ulong.MaxValue;

    private CanvasGroup _root;
    private TMP_Text _objective;
    private TMP_Text _status;
    private Image _progress;
    private float _searchTimer;
    private BossEncounterManager _encounter;
    private BossRespawnPolicy _respawn;

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
        panel.anchorMin = new Vector2(0.5f, 1f);
        panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = new Vector2(0f, -28f);
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

        _root.alpha = 1f;
        switch (_encounter.State)
        {
            case BossEncounterManager.EncounterState.WaitingForPlayers:
                _objective.text = "NHIỆM VỤ: Tập hợp tại cổng lõi";
                _status.text = "Chờ cả hai người chơi vào đấu trường";
                break;
            case BossEncounterManager.EncounterState.Intro:
                _objective.text = "NHIỆM VỤ: Phá hủy Lõi Cai Ngục";
                _status.text = "Đấu trường đang bị phong tỏa";
                break;
            case BossEncounterManager.EncounterState.Active:
                _objective.text = "NHIỆM VỤ: Dùng cơ chế đấu trường để hạ boss";
                PresentActiveStatus();
                break;
            case BossEncounterManager.EncounterState.WipeReset:
                _objective.text = "LÕI ĐÃ ÁP ĐẢO CẢ HAI";
                _status.text = "Đang tái tạo đấu trường...";
                break;
            case BossEncounterManager.EncounterState.Victory:
                _objective.text = "NHIỆM VỤ HOÀN THÀNH";
                _status.text = "Lõi Cai Ngục đã bị phá hủy";
                break;
        }
    }

    private void PresentActiveStatus()
    {
        _progress.fillAmount = 0f;
        if (_respawn == null || NetworkManager.Singleton == null)
        {
            _status.text = "Phối hợp, né đòn và kích hoạt cơ chế";
            return;
        }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (_respawn.CountdownTarget == localId)
        {
            _status.text = $"Bạn đã gục — hồi sinh sau {_respawn.CountdownRemaining:0.0}s";
            return;
        }
        if (_respawn.Reviver == localId)
        {
            _progress.fillAmount = _respawn.ReviveProgress;
            _status.text = $"Đang cứu đồng đội... {_respawn.ReviveProgress * 100f:0}%";
            return;
        }
        if (_respawn.ReviveTarget == localId)
        {
            _progress.fillAmount = _respawn.ReviveProgress;
            _status.text = $"Đồng đội đang cứu bạn... {_respawn.ReviveProgress * 100f:0}%";
            return;
        }
        if (_respawn.TryGetLocalReviveCandidate(out ulong targetId) && targetId != NoClient)
        {
            _status.text = "Giữ Interact để cứu đồng đội (5 giây, hồi 60% HP)";
            return;
        }
        _status.text = "Phối hợp, né đòn và kích hoạt cơ chế";
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
