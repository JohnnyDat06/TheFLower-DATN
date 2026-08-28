using System.Collections;
using TMPro;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Presents the Phase 15 co-op controls at the beginning of each Sand Boat attempt.
/// The panels are local presentation only; authoritative chase state remains in SandBoatBoarding.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandBoatChaseTutorialUI : MonoBehaviour
{
    private const string P1LeftKeyboardControl = "[ D ]";
    private const string P1RightKeyboardControl = "[ A ]";
    private const string P1LeftGamepadControl = "[ LS \u2192 ]";
    private const string P1RightGamepadControl = "[ LS \u2190 ]";
    private const string P2LeftKeyboardControl = "[ S ]";
    private const string P2RightKeyboardControl = "[ W ]";
    private const string P2LeftGamepadControl = "[ LS \u2193 ]";
    private const string P2RightGamepadControl = "[ LS \u2191 ]";

    [Header("Tham chiếu")]
    [SerializeField, Tooltip("Component boarding cung cấp trạng thái bắt đầu và tín hiệu chơi lại của màn rượt đuổi.")]
    private SandBoatBoarding _boarding;
    [SerializeField, Tooltip("Bảng hướng dẫn P1 được đặt ở cạnh trái màn hình.")]
    private CanvasGroup _p1Panel;
    private CanvasGroup _p1RightPanel;
    [SerializeField, Tooltip("Bảng tăng tốc P2 với mũi tên lên được đặt ở cạnh phải màn hình.")]
    private CanvasGroup _p2Panel;
    private CanvasGroup _p2LeftPanel;
    private TextMeshProUGUI _p1Controls;
    private TextMeshProUGUI _p1RightControls;
    private TextMeshProUGUI _p2Controls;
    private TextMeshProUGUI _p2LeftControls;

    [Header("Thời gian hiển thị")]
    [SerializeField, Min(0.5f), Tooltip("Tổng thời gian hai bảng hướng dẫn nhấp nháy trước khi tự ẩn, tính bằng giây thực.")]
    private float _displayDuration = 6f;
    [SerializeField, Range(0.1f, 1f), Tooltip("Khoảng thời gian đổi giữa trạng thái sáng và mờ của hiệu ứng nhấp nháy.")]
    private float _blinkInterval = 0.35f;
    [SerializeField, Range(0f, 1f), Tooltip("Độ trong suốt ở nhịp mờ; 0 là tắt hẳn và 1 là không nhấp nháy.")]
    private float _dimmedAlpha = 0.35f;

    private Coroutine _displayRoutine;
    private bool _wasChaseStarted;
    private InputDeviceDetector _inputDeviceDetector;

    private void Awake()
    {
        ResolveControlLabels();
        RefreshInputPresentation();
        HideAllPanels();
    }

    private void OnEnable()
    {
        if (_boarding != null)
        {
            _boarding.ChaseRestartedLocally += ReplayTutorial;
        }
    }

    private void Start()
    {
        TrySubscribeInputDeviceDetector();
        _wasChaseStarted = _boarding != null && _boarding.ChaseStarted;
        RefreshInputPresentation();
        if (_wasChaseStarted)
        {
            ReplayTutorial();
        }
    }

    private void Update()
    {
        TrySubscribeInputDeviceDetector();

        bool chaseStarted = _boarding != null && _boarding.ChaseStarted;
        if (chaseStarted && !_wasChaseStarted)
        {
            ReplayTutorial();
        }

        _wasChaseStarted = chaseStarted;
    }

    private void OnDisable()
    {
        if (_boarding != null)
        {
            _boarding.ChaseRestartedLocally -= ReplayTutorial;
        }

        if (_displayRoutine != null)
        {
            StopCoroutine(_displayRoutine);
            _displayRoutine = null;
        }

        if (_inputDeviceDetector != null)
        {
            _inputDeviceDetector.DeviceChanged -= OnInputDeviceChanged;
            _inputDeviceDetector = null;
        }

        HideAllPanels();
    }

    /// <summary>Restarts the blink-and-hide sequence for the current local attempt.</summary>
    public void ReplayTutorial()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (_displayRoutine != null)
        {
            StopCoroutine(_displayRoutine);
        }

        _displayRoutine = StartCoroutine(DisplayRoutine());
    }

    private IEnumerator DisplayRoutine()
    {
        float elapsed = 0f;
        bool isBright = true;
        RefreshInputPresentation();
        SetRolePanelAlpha(1f);

        while (elapsed < _displayDuration)
        {
            float waitDuration = Mathf.Min(_blinkInterval, _displayDuration - elapsed);
            yield return new WaitForSecondsRealtime(waitDuration);
            elapsed += waitDuration;
            isBright = !isBright;
            SetRolePanelAlpha(isBright ? 1f : _dimmedAlpha);
        }

        SetRolePanelAlpha(0f);
        _displayRoutine = null;
    }

    private void TrySubscribeInputDeviceDetector()
    {
        if (_inputDeviceDetector != null || InputDeviceDetector.Instance == null)
        {
            return;
        }

        _inputDeviceDetector = InputDeviceDetector.Instance;
        _inputDeviceDetector.DeviceChanged += OnInputDeviceChanged;
        RefreshInputPresentation();
    }

    private void OnInputDeviceChanged(InputDeviceType _)
    {
        RefreshInputPresentation();
    }

    private void RefreshInputPresentation()
    {
        ResolveControlLabels();

        bool isGamepad = _inputDeviceDetector != null
                         && _inputDeviceDetector.CurrentDeviceType == InputDeviceType.Gamepad;

        if (_p1Controls != null)
        {
            _p1Controls.text = isGamepad ? P1LeftGamepadControl : P1LeftKeyboardControl;
        }

        if (_p1RightControls != null)
        {
            _p1RightControls.text = isGamepad ? P1RightGamepadControl : P1RightKeyboardControl;
        }

        if (_p2LeftControls != null)
        {
            _p2LeftControls.text = isGamepad ? P2LeftGamepadControl : P2LeftKeyboardControl;
        }

        if (_p2Controls != null)
        {
            _p2Controls.text = isGamepad ? P2RightGamepadControl : P2RightKeyboardControl;
        }
    }

    private void ResolveControlLabels()
    {
        if (_p1Controls == null && _p1Panel != null)
        {
            _p1Controls = _p1Panel.transform.Find("Controls")?.GetComponent<TextMeshProUGUI>();
        }

        if (_p2Controls == null && _p2Panel != null)
        {
            _p2Controls = _p2Panel.transform.Find("Controls")?.GetComponent<TextMeshProUGUI>();
        }

        if (_p2LeftPanel == null)
        {
            _p2LeftPanel = transform.Find("P2_SpeedTutorial_Left")?.GetComponent<CanvasGroup>();
        }

        EnsureP2LeftPanel();
        ConfigureP2Panel(_p2Panel, true);
        ConfigureP2Panel(_p2LeftPanel, false);

        if (_p2LeftControls == null && _p2LeftPanel != null)
        {
            _p2LeftControls = _p2LeftPanel.transform.Find("Controls")?.GetComponent<TextMeshProUGUI>();
        }

        if (_p1RightPanel == null)
        {
            _p1RightPanel = transform.Find("P1_SteeringTutorial_Right")?.GetComponent<CanvasGroup>();
        }

        if (_p1RightControls == null && _p1RightPanel != null)
        {
            _p1RightControls = _p1RightPanel.transform.Find("Controls")?.GetComponent<TextMeshProUGUI>();
        }
    }

    private void EnsureP2LeftPanel()
    {
        if (_p2LeftPanel != null || !Application.isPlaying || _p2Panel == null)
        {
            return;
        }

        GameObject leftPanelObject = Instantiate(_p2Panel.gameObject, transform);
        leftPanelObject.name = "P2_SpeedTutorial_Left";
        _p2LeftPanel = leftPanelObject.GetComponent<CanvasGroup>();
    }

    private static void ConfigureP2Panel(CanvasGroup panel, bool isRight)
    {
        if (panel == null)
        {
            return;
        }

        RectTransform panelRect = panel.transform as RectTransform;
        if (panelRect != null)
        {
            Vector2 anchor = isRight ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
            panelRect.anchorMin = anchor;
            panelRect.anchorMax = anchor;
            panelRect.pivot = isRight ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
            panelRect.anchoredPosition = new Vector2(isRight ? -42f : 42f, -42f);
        }

        Transform title = panel.transform.Find("Title");
        if (title != null)
        {
            title.gameObject.SetActive(false);
        }

        Transform controls = panel.transform.Find("Controls");
        if (controls is RectTransform controlsRect)
        {
            controlsRect.anchoredPosition = new Vector2(68f, 0f);
        }

        Transform arrowUp = panel.transform.Find("ArrowUp");
        Transform arrowDown = panel.transform.Find("ArrowDown");
        Transform sharedArrow = panel.transform.Find("Arrow");
        if (arrowUp != null)
        {
            arrowUp.gameObject.SetActive(isRight);
            if (arrowUp is RectTransform arrowUpRect)
            {
                arrowUpRect.anchoredPosition = new Vector2(-57f, 0f);
            }
        }

        if (arrowDown != null)
        {
            arrowDown.gameObject.SetActive(!isRight);
            if (arrowDown is RectTransform arrowDownRect)
            {
                arrowDownRect.anchoredPosition = new Vector2(-57f, 0f);
            }
        }

        if (sharedArrow != null)
        {
            sharedArrow.gameObject.SetActive(true);
            sharedArrow.localEulerAngles = new Vector3(0f, 0f, isRight ? 270f : 90f);
            if (sharedArrow is RectTransform sharedArrowRect)
            {
                sharedArrowRect.anchoredPosition = new Vector2(-57f, 0f);
            }
        }
    }

    private void SetRolePanelAlpha(float alpha)
    {
        bool isP1 = IsLocalPlayerOne();
        SetPanelAlpha(_p1Panel, isP1 ? alpha : 0f);
        SetPanelAlpha(_p1RightPanel, isP1 ? alpha : 0f);
        SetPanelAlpha(_p2LeftPanel, isP1 ? 0f : alpha);
        SetPanelAlpha(_p2Panel, isP1 ? 0f : alpha);
    }

    private void HideAllPanels()
    {
        SetPanelAlpha(_p1Panel, 0f);
        SetPanelAlpha(_p1RightPanel, 0f);
        SetPanelAlpha(_p2LeftPanel, 0f);
        SetPanelAlpha(_p2Panel, 0f);
    }

    private static bool IsLocalPlayerOne()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager == null
               || !networkManager.IsListening
               || networkManager.LocalClientId == NetworkManager.ServerClientId;
    }

    private static void SetPanelAlpha(CanvasGroup panel, float alpha)
    {
        if (panel == null)
        {
            return;
        }

        panel.alpha = alpha;
        panel.interactable = false;
        panel.blocksRaycasts = false;
    }
}
