using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presents a prominent screen-space quest marker and its distance label.
/// It owns only marker visuals; quest state and world projection stay in <see cref="QuestHUD"/>.
/// </summary>
public sealed class QuestScreenMarkerView : MonoBehaviour
{
    private static Sprite fallbackCircleSprite;
    private static readonly Color GlowColor = new(0.15f, 0.9f, 1f, 0.36f);
    private static readonly Color RingColor = new(1f, 0.76f, 0.12f, 1f);
    private static readonly Color InnerColor = new(0.025f, 0.07f, 0.12f, 0.98f);
    private static readonly Color CoreColor = new(1f, 0.96f, 0.72f, 1f);
    private static readonly Color BadgeColor = new(0.015f, 0.04f, 0.075f, 0.94f);

    [SerializeField] private RectTransform markerRoot;
    [SerializeField] private RectTransform pulseRing;
    [SerializeField] private Image pulseRingImage;
    [SerializeField] private RectTransform distanceBadge;
    [SerializeField] private TMP_Text distanceText;
    [SerializeField, Min(0.01f)] private float pulseFrequency = 1.25f;
    [SerializeField, Range(1f, 2f)] private float pulseMaximumScale = 1.55f;

    /// <summary>Updates marker position and distance without allocating formatted strings.</summary>
    public void Present(Vector3 screenPosition, float distance)
    {
        if (markerRoot != null)
            markerRoot.position = screenPosition;

        UpdateDistanceBadgeSide(screenPosition.x);

        if (distanceText != null)
            distanceText.SetText("{0:0} m", distance);
    }

    /// <summary>Controls the complete marker without disabling the quest HUD panel.</summary>
    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }

    private void Update()
    {
        if (pulseRing == null || pulseRingImage == null)
            return;

        float pulse = (Mathf.Sin(Time.unscaledTime * pulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f;
        pulseRing.localScale = Vector3.one * Mathf.Lerp(1f, pulseMaximumScale, pulse);
        Color color = GlowColor;
        color.a = Mathf.Lerp(GlowColor.a, 0.04f, pulse);
        pulseRingImage.color = color;
    }

    /// <summary>Creates the runtime fallback used only when an older prefab has no serialized marker view.</summary>
    public static QuestScreenMarkerView Create(Transform parent)
    {
        Sprite circleSprite = CreateFallbackCircleSprite();
        GameObject rootObject = CreateUiObject("QuestScreenMarker", parent);
        RectTransform rootRect = rootObject.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(68f, 68f);

        Image glow = CreateCircle("PulseGlow", rootRect, circleSprite, GlowColor, 78f);
        Image ring = CreateCircle("OuterRing", rootRect, circleSprite, RingColor, 58f);
        CreateCircle("InnerPlate", ring.rectTransform, circleSprite, InnerColor, 43f);
        CreateCircle("MarkerCore", ring.rectTransform, circleSprite, CoreColor, 20f);

        GameObject badgeObject = CreateUiObject("DistanceBadge", rootRect);
        RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(1f, 0.5f);
        badgeRect.anchorMax = new Vector2(1f, 0.5f);
        badgeRect.pivot = new Vector2(0f, 0.5f);
        badgeRect.anchoredPosition = new Vector2(10f, 0f);
        badgeRect.sizeDelta = new Vector2(96f, 38f);
        Image badge = badgeObject.AddComponent<Image>();
        badge.sprite = circleSprite;
        badge.color = BadgeColor;
        badge.raycastTarget = false;
        Shadow badgeShadow = badgeObject.AddComponent<Shadow>();
        badgeShadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
        badgeShadow.effectDistance = new Vector2(2f, -2f);

        GameObject textObject = CreateUiObject("DistanceText", badgeRect);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 2f);
        textRect.offsetMax = new Vector2(-10f, -2f);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.SetText("0 m");
        text.fontSize = 21f;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        UnityEngine.UI.Outline textOutline = textObject.AddComponent<UnityEngine.UI.Outline>();
        textOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        textOutline.effectDistance = new Vector2(1f, -1f);

        QuestScreenMarkerView view = rootObject.AddComponent<QuestScreenMarkerView>();
        view.markerRoot = rootRect;
        view.pulseRing = glow.rectTransform;
        view.pulseRingImage = glow;
        view.distanceBadge = badgeRect;
        view.distanceText = text;
        return view;
    }

    private void UpdateDistanceBadgeSide(float screenX)
    {
        if (distanceBadge == null)
            return;

        bool placeOnLeft = screenX > Screen.width - 150f;
        distanceBadge.anchorMin = new Vector2(placeOnLeft ? 0f : 1f, 0.5f);
        distanceBadge.anchorMax = distanceBadge.anchorMin;
        distanceBadge.pivot = new Vector2(placeOnLeft ? 1f : 0f, 0.5f);
        distanceBadge.anchoredPosition = new Vector2(placeOnLeft ? -10f : 10f, 0f);
    }

    private static Sprite CreateFallbackCircleSprite()
    {
        if (fallbackCircleSprite != null)
            return fallbackCircleSprite;

        const int textureSize = 64;
        Texture2D texture = new(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            name = "QuestMarkerFallbackCircle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[textureSize * textureSize];
        Vector2 center = Vector2.one * ((textureSize - 1f) * 0.5f);
        float radius = textureSize * 0.5f - 1f;
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float alpha = Mathf.Clamp01(radius - Vector2.Distance(new Vector2(x, y), center));
                pixels[y * textureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        fallbackCircleSprite = Sprite.Create(texture, new Rect(0f, 0f, textureSize, textureSize), Vector2.one * 0.5f, 100f);
        fallbackCircleSprite.name = "QuestMarkerFallbackCircle";
        fallbackCircleSprite.hideFlags = HideFlags.HideAndDontSave;
        return fallbackCircleSprite;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        GameObject child = new(objectName, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child;
    }

    private static Image CreateCircle(string objectName, RectTransform parent, Sprite sprite, Color color, float size)
    {
        GameObject circleObject = CreateUiObject(objectName, parent);
        RectTransform rect = circleObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.one * size;
        Image image = circleObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }
}
