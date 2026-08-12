using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// InteractPromptHUD — Prompt UI duy nhất nằm trên Canvas HUD (Screen Space - Overlay).
/// Lắng nghe PlayerInteractor.OnInteractableFound / OnInteractableLost.
/// Chuyển đổi tọa độ 3D của Interactable sang 2D màn hình mỗi frame (WorldToScreenPoint).
/// Chỉ hiển thị trên máy của chính người chơi đó (IsOwner) — Host và Client tự nhiên tách biệt.
/// SRS §9.2
/// </summary>
public class InteractPromptHUD : MonoBehaviour
{
    // ─── Inspector Fields ─────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Panel cha chứa toàn bộ Prompt (bật/tắt cái này để show/hide).")]
    [SerializeField] private RectTransform _promptPanel;

    [Tooltip("Text hiển thị tên phím bấm, ví dụ: [E]")]
    [SerializeField] private TextMeshProUGUI _keyLabel;

    [Tooltip("Text hiển thị tên hành động, ví dụ: Mở cửa")]
    [SerializeField] private TextMeshProUGUI _actionLabel;

    [Header("Positioning")]
    [Tooltip("Offset in world space above the selected interactable.")]
    [SerializeField] private Vector3 _worldOffset = new Vector3(0f, 1.5f, 0f);

    [Tooltip("Additional screen-space offset after projecting the interactable to the HUD.")]
    [SerializeField] private Vector2 _screenOffset = new Vector2(0f, 20f);

    [Header("Font Sizes")]
    [SerializeField] private float _fontSizeSmall  = 14f;
    [SerializeField] private float _fontSizeNormal = 18f;
    [SerializeField] private float _fontSizeLarge  = 24f;

    [Header("Colors")]
    [SerializeField] private Color _hostColor = Color.cyan;
    [SerializeField] private Color _clientColor = Color.red;

    // ─── Runtime ─────────────────────────────────────────────────────────────

    [Header("Input Icon Provider")]
    [Tooltip("ScriptableObject chứa mapping action → icon/text theo device.")]
    [SerializeField] private InputIconMap _iconProvider;

    private IInteractable _currentTarget;
    private Transform _currentTargetTransform;
    private Camera _mainCamera;
    private Canvas        _parentCanvas;
    private float _lastCanvasScale = -1f;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        _mainCamera = Camera.main;
        _parentCanvas = GetComponentInParent<Canvas>();

        RectTransform panel = _promptPanel;
        if (panel != null)
        {
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(190f, 72f);
            panel.localScale = GetPromptScale();
            Image background = panel.GetComponent<Image>();
            if (background != null)
            {
                background.color = new Color(0.015f, 0.08f, 0.09f, 0.88f);
                background.raycastTarget = false;
            }
            UnityEngine.UI.Outline outline = panel.GetComponent<UnityEngine.UI.Outline>() ?? panel.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new Color(0.1f, 0.9f, 0.85f, 0.8f);
            outline.effectDistance = new Vector2(2f, 2f);
        }
        if (_keyLabel != null)
        {
            _keyLabel.fontSize = 24f;
            _keyLabel.alignment = TextAlignmentOptions.Center;
            _keyLabel.rectTransform.localScale = Vector3.one;
            _keyLabel.rectTransform.anchoredPosition = new Vector2(0f, 13f);
            _keyLabel.rectTransform.sizeDelta = new Vector2(170f, 32f);
        }
        if (_actionLabel != null)
        {
            _actionLabel.fontSize = 15f;
            _actionLabel.alignment = TextAlignmentOptions.Center;
            _actionLabel.rectTransform.localScale = Vector3.one;
            _actionLabel.rectTransform.anchoredPosition = new Vector2(0f, -17f);
            _actionLabel.rectTransform.sizeDelta = new Vector2(170f, 24f);
        }

        SetVisible(false);
    }

    private void OnEnable()
    {
        PlayerInteractor.OnInteractableFound += HandleFound;
        PlayerInteractor.OnInteractableLost  += HandleLost;
        EventBus.OnInputBindingChanged       += RefreshKeyLabel;
        EventBus.OnInputDeviceChanged        += OnDeviceChanged;
        EventBus.OnAccessibilityChanged      += RefreshFontSize;
        Canvas.willRenderCanvases             += TrackPromptForCanvasRender;

        RefreshKeyLabel();
        RefreshFontSize();
    }

    private void OnDisable()
    {
        PlayerInteractor.OnInteractableFound -= HandleFound;
        PlayerInteractor.OnInteractableLost  -= HandleLost;
        EventBus.OnInputBindingChanged       -= RefreshKeyLabel;
        EventBus.OnInputDeviceChanged        -= OnDeviceChanged;
        EventBus.OnAccessibilityChanged      -= RefreshFontSize;
        Canvas.willRenderCanvases             -= TrackPromptForCanvasRender;

        SetVisible(false);
    }

    private void LateUpdate()
    {
        RefreshPromptScale();
    }

    // ─── Event Handlers ───────────────────────────────────────────────────────

    private void HandleFound(IInteractable target)
    {
        _currentTarget          = target;
        _currentTargetTransform = target.GetPromptTransform();

        if (_actionLabel != null)
            _actionLabel.text = target.InteractionPrompt;

        RefreshColor();
        SetVisible(true);
    }

    private void HandleLost()
    {
        _currentTarget          = null;
        _currentTargetTransform = null;
        SetVisible(false);
    }

    private void TrackPromptForCanvasRender()
    {
        if (_currentTarget == null || _currentTargetTransform == null) return;
        if (_mainCamera == null || !_mainCamera.isActiveAndEnabled)
            _mainCamera = Camera.main;
        if (_mainCamera == null) return;

        TrackWorldPosition();
    }

    private void TrackWorldPosition()
    {
        Vector3 screenPoint = _mainCamera.WorldToScreenPoint(_currentTargetTransform.position + _worldOffset);
        if (screenPoint.z <= 0f)
        {
            SetVisible(false);
            return;
        }

        if (_parentCanvas == null || _promptPanel == null) return;

        Camera canvasCamera = _parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : _parentCanvas.worldCamera != null ? _parentCanvas.worldCamera : _mainCamera;
        RectTransform canvasRect = _parentCanvas.GetComponent<RectTransform>();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, canvasCamera, out Vector2 targetPosition))
            return;

        targetPosition += _screenOffset;
        _promptPanel.anchoredPosition = targetPosition;
        SetVisible(true);
    }

    // ─── World → Screen Tracking ──────────────────────────────────────────────

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SetVisible(bool visible)
    {
        if (_promptPanel != null)
            _promptPanel.localScale = visible ? GetPromptScale() : Vector3.zero;
    }

    private void RefreshPromptScale()
    {
        if (_promptPanel == null || _parentCanvas == null) return;
        float scale = Mathf.Max(0.01f, _parentCanvas.scaleFactor);
        if (Mathf.Abs(scale - _lastCanvasScale) < 0.001f) return;
        _lastCanvasScale = scale;
        if (_promptPanel.localScale != Vector3.zero)
            _promptPanel.localScale = GetPromptScale();
    }

    private Vector3 GetPromptScale()
    {
        float scale = _parentCanvas == null ? 1f : Mathf.Max(0.01f, _parentCanvas.scaleFactor);
        return Vector3.one / scale;
    }

    /// <summary>
    /// Callback khi device type thay đổi (KB ↔ Gamepad).
    /// </summary>
    private void OnDeviceChanged(InputDeviceType _) => RefreshKeyLabel();

    private void RefreshKeyLabel()
    {
        if (_keyLabel == null) return;

        if (_iconProvider != null)
        {
            var deviceType = InputDeviceDetector.Instance != null
                ? InputDeviceDetector.Instance.CurrentDeviceType
                : InputDeviceType.KeyboardMouse;
            _keyLabel.text = $"[{_iconProvider.GetDisplayText("Interact", deviceType)}]";
        }
        else
        {
            _keyLabel.text = "[E]"; // graceful fallback
        }
    }

    private void RefreshFontSize()
    {
        int sizeKey = PlayerPrefs.GetInt(Constants.PlayerPrefsKeys.ACCESSIBILITY_PROMPT_SIZE, 1);
        float size  = sizeKey switch
        {
            0 => _fontSizeSmall,
            2 => _fontSizeLarge,
            _ => _fontSizeNormal
        };

        if (_keyLabel    != null) _keyLabel.fontSize    = size;
        if (_actionLabel != null) _actionLabel.fontSize = size;
    }

    private void RefreshColor()
    {
        bool isHost = Unity.Netcode.NetworkManager.Singleton != null && 
                     Unity.Netcode.NetworkManager.Singleton.IsHost;
        Color targetColor = isHost ? _hostColor : _clientColor;

        if (_keyLabel != null) _keyLabel.color = targetColor;
        if (_actionLabel != null) _actionLabel.color = targetColor;
    }
}
