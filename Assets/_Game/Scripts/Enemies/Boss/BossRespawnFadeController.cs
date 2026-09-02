using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Che man hinh cua ca hai nguoi choi khi tran Boss vao trang thai full-party wipe
/// va chi mo lai sau khi Host da hoi sinh day du ca hai nguoi choi.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossRespawnFadeController : MonoBehaviour
{
    [Header("Boss Respawn Fade")]
    [SerializeField, Min(0.05f), Tooltip("Thoi gian man hinh mo dan sang mau den sau khi mau nguoi choi ve 0.")]
    private float _fadeToBlackDuration = 0.35f;
    [SerializeField, Min(0.05f), Tooltip("Thoi gian man hinh hien lai sau khi nguoi choi da hoi sinh va teleport xong.")]
    private float _fadeFromBlackDuration = 0.5f;
    [SerializeField, Min(0f), Tooltip("Thoi gian toi thieu giu man hinh den de wipe hoac hoi sinh nhanh van hien thi ro rang.")]
    private float _minimumBlackHoldDuration = 0.35f;
    [SerializeField, Tooltip("Thu tu ve cua lop man den; gia tri cao giup che toan bo HUD Boss.")]
    private int _sortingOrder = 6500;
    [SerializeField, Tooltip("Trang thai that bai Sand Boat truoc khi vao Boss; giu nguyen fade cua checkpoint duong truot.")]
    private SandBoatChaseFailController _sandBoatFailController;
    [SerializeField, Tooltip("Trang thai tran Boss authoritative; ca Host va Client se mo den khi hai nguoi choi cung chet va wipe reset.")]
    private BossEncounterManager _bossEncounter;

    private CanvasGroup _fadeGroup;
    private Coroutine _fadeRoutine;
    private bool _fadeRequested;

    private void Awake()
    {
        ResolveSandBoatFailController();
        ResolveBossEncounter();
        BuildFadeCanvas();
        SetFadeAlpha(0f);
    }

    private void OnDisable()
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = null;
        _fadeRequested = false;
        SetFadeAlpha(0f);
    }

    private void Update()
    {
        ResolveSandBoatFailController();
        ResolveBossEncounter();

        bool bossEncounterStarted = _bossEncounter != null && _bossEncounter.HasEncounterStarted;
        bool sandBoatChaseFailed = !bossEncounterStarted
                                   && _sandBoatFailController != null
                                   && _sandBoatFailController.IsFailed;
        bool bossWipeReset = _bossEncounter != null
                             && _bossEncounter.State == BossEncounterManager.EncounterState.WipeReset;
        ApplyFadeRequirement(sandBoatChaseFailed || bossWipeReset);
    }

    private void ApplyFadeRequirement(bool shouldFadeToBlack)
    {
        _fadeRequested = shouldFadeToBlack;
        if (_fadeRoutine != null || !shouldFadeToBlack)
        {
            return;
        }

        if (_fadeGroup == null)
        {
            BuildFadeCanvas();
        }

        _fadeRoutine = StartCoroutine(FadeLifecycle());
    }

    private void ResolveBossEncounter()
    {
        if (_bossEncounter != null)
        {
            return;
        }

        _bossEncounter = BossEncounterManager.Instance;
        if (_bossEncounter == null)
        {
            _bossEncounter = FindFirstObjectByType<BossEncounterManager>(FindObjectsInactive.Include);
        }
    }

    private void ResolveSandBoatFailController()
    {
        if (_sandBoatFailController != null) return;
        _sandBoatFailController = FindFirstObjectByType<SandBoatChaseFailController>(FindObjectsInactive.Include);
    }

    private IEnumerator FadeLifecycle()
    {
        do
        {
            yield return FadeAlphaRoutine(1f, _fadeToBlackDuration, false);

            float blackHoldElapsed = 0f;
            while (_fadeRequested || blackHoldElapsed < _minimumBlackHoldDuration)
            {
                if (!_fadeRequested)
                {
                    blackHoldElapsed += Time.unscaledDeltaTime;
                }

                yield return null;
            }

            yield return FadeAlphaRoutine(0f, _fadeFromBlackDuration, true);
        }
        while (_fadeRequested);

        SetFadeAlpha(0f);
        _fadeRoutine = null;
    }

    private IEnumerator FadeAlphaRoutine(float targetAlpha, float duration, bool stopWhenFadeRequested)
    {
        float startAlpha = _fadeGroup.alpha;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (stopWhenFadeRequested && _fadeRequested)
            {
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            SetFadeAlpha(Mathf.SmoothStep(startAlpha, targetAlpha, progress));
            yield return null;
        }

        SetFadeAlpha(targetAlpha);
    }

    private void BuildFadeCanvas()
    {
        if (_fadeGroup != null) return;

        GameObject canvasObject = new(
            "Boss Respawn Fade Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = _sortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _fadeGroup = canvasObject.GetComponent<CanvasGroup>();
        _fadeGroup.interactable = false;
        _fadeGroup.blocksRaycasts = false;

        GameObject blackImageObject = new("Black", typeof(RectTransform), typeof(Image));
        blackImageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform blackRect = blackImageObject.GetComponent<RectTransform>();
        blackRect.anchorMin = Vector2.zero;
        blackRect.anchorMax = Vector2.one;
        blackRect.offsetMin = Vector2.zero;
        blackRect.offsetMax = Vector2.zero;

        Image blackImage = blackImageObject.GetComponent<Image>();
        blackImage.color = Color.black;
        blackImage.raycastTarget = false;
    }

    private void SetFadeAlpha(float alpha)
    {
        if (_fadeGroup == null) return;
        _fadeGroup.alpha = Mathf.Clamp01(alpha);
    }
}
