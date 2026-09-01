using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Che man hinh cua rieng nguoi choi bi ha trong Boss Room va chi mo lai
/// sau khi teleport hoi sinh authoritative da hoan tat.
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
    [SerializeField, Tooltip("Trang thai that bai Sand Boat trong cung scene; man hinh se mo den trong luc checkpoint dang reset.")]
    private SandBoatChaseFailController _sandBoatFailController;
    [SerializeField, Tooltip("Trang thai tran Boss authoritative; ca Host va Client se mo den khi hai nguoi choi cung chet va wipe reset.")]
    private BossEncounterManager _bossEncounter;

    private CanvasGroup _fadeGroup;
    private Coroutine _fadeRoutine;
    private PlayerHealth _localPlayerHealth;
    private bool _localDeathEventActive;
    private bool _fadeRequested;

    private void Awake()
    {
        ResolveSandBoatFailController();
        ResolveBossEncounter();
        BuildFadeCanvas();
        SetFadeAlpha(0f);
    }

    private void OnEnable()
    {
        EventBus.OnPlayerDied += HandlePlayerDied;
        EventBus.OnPlayerRespawned += HandlePlayerRespawned;
    }

    private void OnDisable()
    {
        EventBus.OnPlayerDied -= HandlePlayerDied;
        EventBus.OnPlayerRespawned -= HandlePlayerRespawned;
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = null;
        _localPlayerHealth = null;
        _localDeathEventActive = false;
        _fadeRequested = false;
    }

    private void Update()
    {
        ResolveLocalPlayerHealth();
        ResolveSandBoatFailController();
        ResolveBossEncounter();

        bool localPlayerIsDead = _localPlayerHealth != null && _localPlayerHealth.IsDead;
        bool sandBoatChaseFailed = _sandBoatFailController != null && _sandBoatFailController.IsFailed;
        bool bossWipeReset = _bossEncounter != null
                             && _bossEncounter.State == BossEncounterManager.EncounterState.WipeReset;
        ApplyFadeRequirement(
            _localDeathEventActive
            || localPlayerIsDead
            || sandBoatChaseFailed
            || bossWipeReset);
    }

    private void HandlePlayerDied(ulong playerClientId)
    {
        if (!IsLocalPlayer(playerClientId)) return;
        _localDeathEventActive = true;
        ApplyFadeRequirement(true);
    }

    private void HandlePlayerRespawned(ulong playerClientId, Vector3 _)
    {
        if (!IsLocalPlayer(playerClientId)) return;
        _localDeathEventActive = false;
        bool sandBoatChaseFailed = _sandBoatFailController != null && _sandBoatFailController.IsFailed;
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

    private void ResolveLocalPlayerHealth()
    {
        if (_localPlayerHealth != null
            && _localPlayerHealth.IsSpawned
            && _localPlayerHealth.IsOwner)
        {
            return;
        }

        _localPlayerHealth = null;
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null
            || !networkManager.IsClient
            || networkManager.LocalClient == null
            || networkManager.LocalClient.PlayerObject == null)
        {
            return;
        }

        networkManager.LocalClient.PlayerObject.TryGetComponent(out _localPlayerHealth);
    }

    private void ResolveSandBoatFailController()
    {
        if (_sandBoatFailController != null)
        {
            return;
        }

        _sandBoatFailController = FindFirstObjectByType<SandBoatChaseFailController>(FindObjectsInactive.Include);
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

    private static bool IsLocalPlayer(ulong playerClientId)
    {
        return NetworkManager.Singleton != null
               && NetworkManager.Singleton.IsClient
               && NetworkManager.Singleton.LocalClientId == playerClientId;
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
