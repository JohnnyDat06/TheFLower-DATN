using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Lightweight persistent NGO chat. It appears only while a network session is listening and
/// uses named messages so no NetworkObject or scene prefab registration is required.
/// </summary>
public sealed class NetworkChatController : MonoBehaviour
{
    private const string SendMessageName = "Game.Chat.Send.v1";
    private const string ReceiveMessageName = "Game.Chat.Receive.v1";
    private const int MaxMessageLength = 180;
    private const int MaxPlayerNameLength = 24;
    private const int MaxVisibleMessages = 50;
    private const float MinimumSendInterval = 0.2f;

    private static readonly Color WindowColor = new(0.025f, 0.12f, 0.14f, 0.92f);
    private static readonly Color HeaderColor = new(0.04f, 0.28f, 0.26f, 0.98f);
    private static readonly Color InputColor = new(0.92f, 0.94f, 0.90f, 0.98f);
    private static readonly Color AccentColor = new(0.12f, 0.76f, 0.58f, 1f);
    private static readonly Color TextColor = new(0.94f, 0.96f, 0.90f, 1f);

    private readonly Queue<GameObject> _messageObjects = new();
    private readonly Dictionary<ulong, float> _lastMessageTimeByClient = new();
    private readonly List<PlayerInputHandler> _typingLockedHandlers = new();

    private NetworkManager _boundNetworkManager;
    private Canvas _canvas;
    private RectTransform _canvasRect;
    private RectTransform _window;
    private GameObject _body;
    private RectTransform _messageContent;
    private ScrollRect _scrollRect;
    private TMP_InputField _inputField;
    private TMP_Text _collapseArrow;
    private bool _collapsed;
    private bool _announcedConnection;
    private bool _isTyping;
    private PlayerInputHandler _inputHandler;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<NetworkChatController>() != null) return;
        new GameObject("NetworkChat").AddComponent<NetworkChatController>();
    }

    private void Awake()
    {
        PersistentSceneRoot.MarkDontDestroyOnLoad(transform);
        BuildInterface();
        SetCollapsed(true);
        SetChatVisible(false);
    }

    private void Update()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        bool connected = networkManager != null && networkManager.IsListening;

        if (connected)
        {
            BindNetwork(networkManager);
            SetChatVisible(true);
            if (!_announcedConnection)
            {
                _announcedConnection = true;
                AddSystemMessage("Chat connected.");
            }

            ResolveInputHandler();
            HandleInputShortcuts();
        }
        else
        {
            SetChatVisible(false);
            _announcedConnection = false;
            if (_boundNetworkManager != null) UnbindNetwork();
        }
    }

    private void OnDestroy()
    {
        UnlockGameplayInput();
        UICursorLockService.Release(this);
        UnbindNetwork();
    }

    private void BindNetwork(NetworkManager networkManager)
    {
        if (_boundNetworkManager == networkManager) return;
        UnbindNetwork();
        _boundNetworkManager = networkManager;
        _boundNetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(SendMessageName, HandleSendRequest);
        _boundNetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(ReceiveMessageName, HandleReceivedMessage);
    }

    private void UnbindNetwork()
    {
        if (_boundNetworkManager == null) return;
        if (_boundNetworkManager.CustomMessagingManager != null)
        {
            _boundNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(SendMessageName);
            _boundNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ReceiveMessageName);
        }
        _boundNetworkManager = null;
        _lastMessageTimeByClient.Clear();
    }

    private void SendCurrentMessage()
    {
        if (_boundNetworkManager == null || !_boundNetworkManager.IsListening || _inputField == null) return;
        string message = Sanitize(_inputField.text, MaxMessageLength);
        if (string.IsNullOrEmpty(message))
        {
            _inputField.ActivateInputField();
            return;
        }

        string playerName = Sanitize(
            PlayerPrefs.GetString(Constants.PlayerPrefsKeys.PLAYER_NAME, "Traveler"),
            MaxPlayerNameLength);
        if (string.IsNullOrEmpty(playerName)) playerName = "Traveler";

        using FastBufferWriter writer = new(1024, Allocator.Temp);
        writer.WriteValueSafe(playerName);
        writer.WriteValueSafe(message);
        _boundNetworkManager.CustomMessagingManager.SendNamedMessage(
            SendMessageName,
            NetworkManager.ServerClientId,
            writer,
            NetworkDelivery.ReliableSequenced);

        _inputField.SetTextWithoutNotify(string.Empty);
        _inputField.ActivateInputField();
    }

    private void HandleInputShortcuts()
    {
        ResolveInputHandler();

        if (_isTyping)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _inputField.SetTextWithoutNotify(string.Empty);
                SetCollapsed(true);
            }
            return;
        }

        if (IsAnotherTextFieldSelected()) return;
        bool chatPressed = _inputHandler != null && _inputHandler.IsOwner
            ? _inputHandler.ChatPressed
            : Keyboard.current != null &&
              (Keyboard.current.tKey.wasPressedThisFrame || Keyboard.current.slashKey.wasPressedThisFrame);
        if (chatPressed)
        {
            if (_collapsed) StartTyping();
            else SetCollapsed(true);
        }
    }

    private void ResolveInputHandler()
    {
        if (_inputHandler != null && _inputHandler.IsSpawned && _inputHandler.IsOwner) return;

        foreach (PlayerInputHandler handler in FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None))
        {
            if (!handler.IsOwner) continue;
            _inputHandler = handler;
            return;
        }

        _inputHandler = null;
    }

    private bool IsAnotherTextFieldSelected()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null && selected != _inputField.gameObject && selected.GetComponentInParent<TMP_InputField>() != null;
    }

    private void StartTyping()
    {
        if (_inputField == null) return;
        if (_isTyping) return;
        if (_collapsed) SetCollapsed(false);

        _isTyping = true;
        LockGameplayInput();
        UICursorLockService.Request(this);
        EventSystem.current?.SetSelectedGameObject(_inputField.gameObject);
        _inputField.ActivateInputField();
    }

    private void StopTyping()
    {
        if (_inputField != null) _inputField.DeactivateInputField();
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _inputField?.gameObject)
            EventSystem.current.SetSelectedGameObject(null);

        _isTyping = false;
        UnlockGameplayInput();
        UICursorLockService.Release(this);
    }

    private void HandleSendRequest(ulong senderClientId, FastBufferReader reader)
    {
        if (_boundNetworkManager == null || !_boundNetworkManager.IsServer) return;
        if (!_boundNetworkManager.ConnectedClientsIds.Contains(senderClientId)) return;

        reader.ReadValueSafe(out string rawName);
        reader.ReadValueSafe(out string rawMessage);
        string playerName = Sanitize(rawName, MaxPlayerNameLength);
        string message = Sanitize(rawMessage, MaxMessageLength);
        if (string.IsNullOrEmpty(playerName)) playerName = $"Player {senderClientId}";
        if (string.IsNullOrEmpty(message)) return;

        float now = Time.unscaledTime;
        if (_lastMessageTimeByClient.TryGetValue(senderClientId, out float lastTime) &&
            now - lastTime < MinimumSendInterval)
            return;
        _lastMessageTimeByClient[senderClientId] = now;

        using FastBufferWriter writer = new(1200, Allocator.Temp);
        writer.WriteValueSafe(senderClientId);
        writer.WriteValueSafe(playerName);
        writer.WriteValueSafe(message);
        _boundNetworkManager.CustomMessagingManager.SendNamedMessage(
            ReceiveMessageName,
            _boundNetworkManager.ConnectedClientsIds,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    private void HandleReceivedMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong authorClientId);
        reader.ReadValueSafe(out string playerName);
        reader.ReadValueSafe(out string message);
        AddChatMessage(authorClientId, playerName, message);
    }

    private void AddChatMessage(ulong authorClientId, string playerName, string message)
    {
        bool local = _boundNetworkManager != null && authorClientId == _boundNetworkManager.LocalClientId;
        Color nameColor = local ? new Color(0.36f, 1f, 0.70f, 1f) : new Color(1f, 0.78f, 0.30f, 1f);
        AddMessage($"<color=#{ColorUtility.ToHtmlStringRGB(nameColor)}><b>{EscapeRichText(playerName)}</b></color>: {EscapeRichText(message)}");
    }

    private void AddSystemMessage(string message)
    {
        AddMessage($"<color=#8FA9AD><i>{EscapeRichText(message)}</i></color>");
    }

    private void AddMessage(string richText)
    {
        if (_messageContent == null) return;
        TMP_Text label = CreateText(_messageContent, richText, 16f, TextColor, TextAlignmentOptions.TopLeft);
        label.textWrappingMode = TextWrappingModes.Normal;
        label.richText = true;
        label.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _messageObjects.Enqueue(label.gameObject);
        while (_messageObjects.Count > MaxVisibleMessages)
        {
            GameObject oldest = _messageObjects.Dequeue();
            if (oldest != null) Destroy(oldest);
        }
        StartCoroutine(ScrollToBottomNextFrame());
    }

    private IEnumerator ScrollToBottomNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 0f;
    }

    private void ToggleCollapsed()
    {
        SetCollapsed(!_collapsed);
        if (!_collapsed) StartTyping();
    }

    private void SetCollapsed(bool collapsed)
    {
        _collapsed = collapsed;
        if (_collapsed) StopTyping();
        _body.SetActive(!_collapsed);
        _window.sizeDelta = _collapsed ? new Vector2(46f, 46f) : new Vector2(480f, 300f);
        _collapseArrow.text = _collapsed ? ">" : "<";
        ClampWindowToCanvas();
    }

    private void SetChatVisible(bool visible)
    {
        if (_canvas != null && _canvas.gameObject.activeSelf != visible)
            _canvas.gameObject.SetActive(visible);
        if (!visible && !_collapsed) SetCollapsed(true);
    }

    private void LockGameplayInput()
    {
        UnlockGameplayInput();
        foreach (PlayerInputHandler handler in FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None))
        {
            if (!handler.IsOwner) continue;
            handler.LockAllInput();
            _typingLockedHandlers.Add(handler);
        }
    }

    private void UnlockGameplayInput()
    {
        foreach (PlayerInputHandler handler in _typingLockedHandlers)
            if (handler != null) handler.UnlockAllInput();
        _typingLockedHandlers.Clear();
    }

    private void ClampWindowToCanvas()
    {
        if (_window == null || _canvasRect == null) return;
        Vector2 position = _window.anchoredPosition;
        position.x = Mathf.Clamp(position.x, 0f, Mathf.Max(0f, _canvasRect.rect.width - _window.rect.width));
        position.y = Mathf.Clamp(position.y, 0f, Mathf.Max(0f, _canvasRect.rect.height - _window.rect.height));
        _window.anchoredPosition = position;
    }

    private void BuildInterface()
    {
        _canvas = new GameObject("NetworkChatCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
        _canvas.transform.SetParent(transform, false);
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1500;
        CanvasScaler scaler = _canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRect = _canvas.GetComponent<RectTransform>();

        _window = CreateRect("ChatWindow", _canvas.transform, Vector2.zero, Vector2.zero, new Vector2(24f, 24f), new Vector2(480f, 300f), Vector2.zero);
        _window.gameObject.AddComponent<Image>().color = WindowColor;
        UnityEngine.UI.Outline outline = _window.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(0.25f, 0.82f, 0.68f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);

        RectTransform header = CreateRect("Header", _window, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 46f), new Vector2(0.5f, 1f));
        header.gameObject.AddComponent<Image>().color = HeaderColor;
        ChatDragHandle dragHandle = header.gameObject.AddComponent<ChatDragHandle>();
        dragHandle.Configure(_window, _canvasRect, _canvas);

        Button collapse = CreateButton(header, "CollapseButton", HeaderColor);
        RectTransform collapseRect = collapse.GetComponent<RectTransform>();
        collapseRect.anchorMin = collapseRect.anchorMax = new Vector2(1f, 0.5f);
        collapseRect.pivot = new Vector2(1f, 0.5f);
        collapseRect.anchoredPosition = new Vector2(-6f, 0f);
        collapseRect.sizeDelta = new Vector2(42f, 34f);
        _collapseArrow = CreateText(collapse.transform, "<", 23f, TextColor, TextAlignmentOptions.Center);
        SetStretch(_collapseArrow.rectTransform, Vector2.zero, Vector2.zero);
        collapse.onClick.AddListener(ToggleCollapsed);

        _body = new GameObject("Body", typeof(RectTransform));
        RectTransform bodyRect = _body.GetComponent<RectTransform>();
        bodyRect.SetParent(_window, false);
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(10f, 10f);
        bodyRect.offsetMax = new Vector2(-10f, -52f);

        RectTransform scrollRoot = CreateRect("Messages", bodyRect, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, -52f), new Vector2(0.5f, 0.5f));
        scrollRoot.offsetMin = new Vector2(0f, 52f);
        scrollRoot.offsetMax = Vector2.zero;
        _scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
        _scrollRect.horizontal = false;
        _scrollRect.scrollSensitivity = 24f;

        RectTransform viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D)).GetComponent<RectTransform>();
        viewport.SetParent(scrollRoot, false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = viewport.offsetMax = Vector2.zero;
        viewport.GetComponent<Image>().color = new Color(0.01f, 0.05f, 0.06f, 0.55f);

        _messageContent = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
        _messageContent.SetParent(viewport, false);
        _messageContent.anchorMin = new Vector2(0f, 1f);
        _messageContent.anchorMax = new Vector2(1f, 1f);
        _messageContent.pivot = new Vector2(0.5f, 1f);
        _messageContent.anchoredPosition = Vector2.zero;
        _messageContent.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = _messageContent.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 5f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        _messageContent.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _scrollRect.viewport = viewport;
        _scrollRect.content = _messageContent;

        RectTransform inputRoot = CreateRect("Input", bodyRect, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(-92f, 42f), new Vector2(0.5f, 0f));
        inputRoot.offsetMin = Vector2.zero;
        inputRoot.offsetMax = new Vector2(-92f, 42f);
        inputRoot.gameObject.AddComponent<Image>().color = InputColor;
        _inputField = inputRoot.gameObject.AddComponent<TMP_InputField>();
        RectTransform textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        textArea.SetParent(inputRoot, false);
        textArea.anchorMin = Vector2.zero;
        textArea.anchorMax = Vector2.one;
        textArea.offsetMin = new Vector2(12f, 5f);
        textArea.offsetMax = new Vector2(-12f, -5f);
        TMP_Text placeholder = CreateText(textArea, "Press T or / to chat...", 15f, new Color(0.20f, 0.28f, 0.29f, 0.65f), TextAlignmentOptions.MidlineLeft);
        placeholder.fontStyle = FontStyles.Italic;
        SetStretch(placeholder.rectTransform, Vector2.zero, Vector2.zero);
        TMP_Text inputText = CreateText(textArea, string.Empty, 15f, new Color(0.02f, 0.07f, 0.08f, 1f), TextAlignmentOptions.MidlineLeft);
        SetStretch(inputText.rectTransform, Vector2.zero, Vector2.zero);
        _inputField.textViewport = textArea;
        _inputField.textComponent = inputText;
        _inputField.placeholder = placeholder;
        _inputField.characterLimit = MaxMessageLength;
        _inputField.lineType = TMP_InputField.LineType.SingleLine;
        _inputField.onSubmit.AddListener(_ => SendCurrentMessage());
        _inputField.onSelect.AddListener(_ => StartTyping());

        Button send = CreateButton(bodyRect, "SendButton", AccentColor);
        RectTransform sendRect = send.GetComponent<RectTransform>();
        sendRect.anchorMin = sendRect.anchorMax = new Vector2(1f, 0f);
        sendRect.pivot = new Vector2(1f, 0f);
        sendRect.anchoredPosition = Vector2.zero;
        sendRect.sizeDelta = new Vector2(84f, 42f);
        TMP_Text sendLabel = CreateText(send.transform, "SEND", 14f, Color.white, TextAlignmentOptions.Center);
        sendLabel.fontStyle = FontStyles.Bold;
        SetStretch(sendLabel.rectTransform, Vector2.zero, Vector2.zero);
        send.onClick.AddListener(SendCurrentMessage);
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2 pivot)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static Button CreateButton(Transform parent, string name, Color color)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Image image = rect.GetComponent<Image>();
        image.color = color;
        Button button = rect.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.72f, 0.78f, 0.75f, 1f);
        button.colors = colors;
        return button;
    }

    private static TMP_Text CreateText(Transform parent, string value, float size, Color color, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(parent, false);
        text.text = value;
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static void SetStretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static string Sanitize(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= maxLength ? sanitized : sanitized.Substring(0, maxLength);
    }

    private static string EscapeRichText(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}

public sealed class ChatDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private RectTransform _window;
    private RectTransform _canvasRect;
    private Canvas _canvas;

    public void Configure(RectTransform window, RectTransform canvasRect, Canvas canvas)
    {
        _window = window;
        _canvasRect = canvasRect;
        _canvas = canvas;
    }

    public void OnBeginDrag(PointerEventData eventData) => Drag(eventData);
    public void OnDrag(PointerEventData eventData) => Drag(eventData);

    private void Drag(PointerEventData eventData)
    {
        if (_window == null || _canvasRect == null || _canvas == null) return;
        _window.anchoredPosition += eventData.delta / Mathf.Max(0.01f, _canvas.scaleFactor);
        Vector2 position = _window.anchoredPosition;
        position.x = Mathf.Clamp(position.x, 0f, Mathf.Max(0f, _canvasRect.rect.width - _window.rect.width));
        position.y = Mathf.Clamp(position.y, 0f, Mathf.Max(0f, _canvasRect.rect.height - _window.rect.height));
        _window.anchoredPosition = position;
    }
}
