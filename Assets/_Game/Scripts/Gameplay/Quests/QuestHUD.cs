using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// HUD tùy chọn: kéo TMP fields và một RectTransform marker vào Inspector.
/// Marker screen-space vẫn hiển thị khi target bị che bởi geometry.
/// </summary>
public sealed class QuestHUD : MonoBehaviour
{
    [SerializeField] private QuestRouteManager route;
    [SerializeField] private GameObject root;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text distanceText;
    [SerializeField] private RectTransform screenMarker;
    [SerializeField] private TMP_Text screenMarkerDistanceText;
    [SerializeField] private QuestWorldMarker worldMarker;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool clampMarkerToScreen = true;
    [SerializeField] private Vector2 screenPadding = new(36f, 36f);

    private bool _uiVisible = true;

    private void Awake()
    {
        if (root == null) root = gameObject;
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (worldMarker == null) worldMarker = GetComponent<QuestWorldMarker>();
        if (targetCamera == null) targetCamera = Camera.main;
        BuildDefaultUIIfNeeded();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        PlayerHealthHUDRemake.GameplayHudVisibilityChanged += HandleGameplayHudVisibilityChanged;
        ApplyGameplayHudVisibility(PlayerHealthHUDRemake.IsGameplayHudVisible);
        BindRoute();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        PlayerHealthHUDRemake.GameplayHudVisibilityChanged -= HandleGameplayHudVisibilityChanged;
        UnbindRoute();
    }

    private void Update()
    {
        if (route == null)
        {
            BindRoute();
            worldMarker?.Clear();
            return;
        }

        if (route.IsRouteCompleted || route.CurrentStep == null)
        {
            worldMarker?.Clear();
            SetVisible(false);
            return;
        }

        SetVisible(true);
        var step = route.CurrentStep;
        worldMarker?.SetTarget(step);
        float distance = 0f;
        if (TryGetLocalPlayer(out var player)) distance = Vector3.Distance(player.position, step.destination.position);
        if (distanceText != null) distanceText.text = $"{distance:0}m";
        UpdateMarker(step.destination.position, distance);
    }

    private void HandleSceneLoaded(Scene _, LoadSceneMode __) => BindRoute();

    private void BindRoute()
    {
        QuestRouteManager discoveredRoute = FindFirstObjectByType<QuestRouteManager>();
        if (discoveredRoute == route)
        {
            Refresh(route != null ? route.CurrentStepIndex : -1);
            return;
        }

        UnbindRoute();
        route = discoveredRoute;
        if (route != null)
            route.StepChanged += Refresh;

        Refresh(route != null ? route.CurrentStepIndex : -1);
    }

    private void UnbindRoute()
    {
        if (route != null)
            route.StepChanged -= Refresh;

        route = null;
    }

    private void Refresh(int index)
    {
        if (route == null || index < 0 || index >= route.Steps.Count)
        {
            worldMarker?.Clear();
            SetVisible(false);
            return;
        }

        var step = route.Steps[index];
        if (titleText != null) titleText.text = step.displayName;
        if (descriptionText != null) descriptionText.text = step.description;
        if (statusText != null) statusText.text = step.RequiresInteraction ? "Approach and interact" : "Move to the marker";
        SetVisible(true);
    }

    private void UpdateMarker(Vector3 worldPosition, float distance)
    {
        if (screenMarker == null) return;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;
        Vector3 screen = targetCamera.WorldToScreenPoint(worldPosition);
        bool inFront = screen.z > 0f;
        if (inFront && clampMarkerToScreen)
        {
            screen.x = Mathf.Clamp(screen.x, screenPadding.x, Screen.width - screenPadding.x);
            screen.y = Mathf.Clamp(screen.y, screenPadding.y, Screen.height - screenPadding.y);
        }
        screenMarker.position = screen;
        screenMarker.gameObject.SetActive(inFront);
        if (screenMarkerDistanceText != null)
            screenMarkerDistanceText.SetText("{0:0}m", distance);
    }

    private void SetVisible(bool visible)
    {
        bool shouldShow = visible && _uiVisible;

        // Keep the panel active so a route can appear after network scene-spawn.
        // CanvasGroup avoids the common "disabled HUD never wakes up" race.
        if (canvasGroup != null)
        {
            canvasGroup.alpha = shouldShow ? 1f : 0f;
            canvasGroup.interactable = shouldShow;
            canvasGroup.blocksRaycasts = false;
        }
        else if (root != null && root != gameObject)
        {
            root.SetActive(shouldShow);
        }
    }

    private void ApplyGameplayHudVisibility(bool visible)
    {
        _uiVisible = visible;

        if (!visible)
        {
            SetVisible(false);
            return;
        }

        bool hasActiveStep = route != null && !route.IsRouteCompleted && route.CurrentStep != null;
        SetVisible(hasActiveStep);
    }

    private void HandleGameplayHudVisibilityChanged(bool visible)
    {
        ApplyGameplayHudVisibility(visible);
    }

    private void BuildDefaultUIIfNeeded()
    {
        // Canvas.prefab already contains this component on InteractPromptPanel.
        // Build only missing references so designers can override any part in Inspector.
        RectTransform host = GetComponent<RectTransform>();
        if (host == null) return;

        if (titleText == null) titleText = CreateText("QuestTitle", host, "QUEST", 22, new Vector2(24, -18), new Vector2(360, 34), TextAlignmentOptions.Left);
        if (descriptionText == null) descriptionText = CreateText("QuestDescription", host, "", 15, new Vector2(24, -60), new Vector2(360, 42), TextAlignmentOptions.Left);
        if (statusText == null) statusText = CreateText("QuestStatus", host, "", 13, new Vector2(24, -104), new Vector2(260, 24), TextAlignmentOptions.Left);
        if (distanceText == null) distanceText = CreateText("QuestDistance", host, "0m", 20, new Vector2(318, -82), new Vector2(70, 32), TextAlignmentOptions.Right);

        if (screenMarker == null)
        {
            GameObject marker = new("QuestScreenMarker");
            marker.transform.SetParent(host.root, false);
            screenMarker = marker.AddComponent<RectTransform>();
            screenMarker.sizeDelta = new Vector2(44, 44);
            Image image = marker.AddComponent<Image>();
            image.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            image.color = new Color(1f, 0.82f, 0.15f, 0.95f);
            image.raycastTarget = false;
            marker.SetActive(false);
        }

        if (screenMarkerDistanceText == null)
            screenMarkerDistanceText = CreateMarkerDistanceText(screenMarker);

        if (GetComponent<Image>() != null)
            GetComponent<Image>().color = new Color(0.03f, 0.06f, 0.1f, 0.92f);

    }

    private static TMP_Text CreateText(string objectName, Transform parent, string value, float size, Vector2 anchoredPosition, Vector2 dimensions, TextAlignmentOptions alignment)
    {
        GameObject child = new(objectName);
        child.transform.SetParent(parent, false);
        RectTransform rect = child.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = dimensions;
        TMP_Text text = child.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.enableWordWrapping = true;
        return text;
    }

    private static TMP_Text CreateMarkerDistanceText(RectTransform marker)
    {
        GameObject child = new("QuestMarkerDistance");
        child.transform.SetParent(marker, false);
        RectTransform rect = child.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(10f, 0f);
        rect.sizeDelta = new Vector2(90f, 34f);
        TMP_Text text = child.AddComponent<TextMeshProUGUI>();
        text.SetText("0m");
        text.fontSize = 20f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Left;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static bool TryGetLocalPlayer(out Transform player)
    {
        player = null;
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return false;
        player = NetworkManager.Singleton.LocalClient.PlayerObject?.transform;
        return player != null;
    }
}
