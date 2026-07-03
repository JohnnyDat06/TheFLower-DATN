using TMPro;
using UnityEngine;

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
    [Tooltip("Offset thêm (tính theo đơn vị World) để Prompt nổi lên phía trên Interactable.")]
    [SerializeField] private Vector3 _worldOffset = new Vector3(0f, 1.5f, 0f);

    [Tooltip("Offset pixel bổ sung sau khi đã convert sang Screen Space.")]
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
    private Transform     _currentTargetTransform;
    private Camera        _mainCamera;
    private Canvas        _parentCanvas;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        _mainCamera   = Camera.main;
        _parentCanvas = GetComponentInParent<Canvas>();

        // Ẩn ngay lúc khởi động
        SetVisible(false);
    }

    private void OnEnable()
    {
        PlayerInteractor.OnInteractableFound += HandleFound;
        PlayerInteractor.OnInteractableLost  += HandleLost;
        EventBus.OnInputBindingChanged       += RefreshKeyLabel;
        EventBus.OnInputDeviceChanged        += OnDeviceChanged;
        EventBus.OnAccessibilityChanged      += RefreshFontSize;

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

        SetVisible(false);
    }

    private void LateUpdate()
    {
        if (_currentTarget == null || _currentTargetTransform == null) return;
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            return;
        }

        TrackWorldPosition();
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

    // ─── World → Screen Tracking ──────────────────────────────────────────────

    private void TrackWorldPosition()
    {
        if (_currentTargetTransform == null) return;
        
        // Chuyển tọa độ World 3D (trên đầu Interactable) → tọa độ Viewport → RectTransform
        Vector3 worldPos   = _currentTargetTransform.position + _worldOffset;
        Vector3 screenPos  = _mainCamera.WorldToScreenPoint(worldPos);

        // Nếu vật sau lưng camera thì ẩn đi
        if (screenPos.z < 0f)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        // Chuyển Screen Position sang Canvas Local Position
        if (_parentCanvas != null && _parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            _promptPanel.position = new Vector3(
                screenPos.x + _screenOffset.x,
                screenPos.y + _screenOffset.y,
                0f
            );
        }
        else if (_parentCanvas != null)
        {
            // Camera Space / World Space canvas — dùng RectTransformUtility
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentCanvas.GetComponent<RectTransform>(),
                screenPos,
                _parentCanvas.worldCamera,
                out Vector2 localPoint
            );
            _promptPanel.localPosition = new Vector3(
                localPoint.x + _screenOffset.x,
                localPoint.y + _screenOffset.y,
                0f
            );
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SetVisible(bool visible)
    {
        if (_promptPanel != null)
        {

            _promptPanel.localScale = visible ? Vector3.one : Vector3.zero;
        }
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
