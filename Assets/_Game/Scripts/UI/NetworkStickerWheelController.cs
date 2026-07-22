using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Gameplay-only radial sticker wheel. Hold G, point at an item, then release G.
/// The center X cancels and the arrows switch sticker sets.
/// </summary>
public sealed class NetworkStickerWheelController : MonoBehaviour
{
    private const string SendMessageName = "Game.Sticker.Send.v2";
    private const string ReceiveMessageName = "Game.Sticker.Receive.v2";
    private const float StickerDuration = 5f;
    private const float MinimumSendInterval = 0.5f;
    private const float StickerHeightAbovePlayer = 2.65f;
    private const float StickerWorldSize = 1.3f;
    private const int CancelSelection = -1;
    private const int PreviousSetSelection = -2;
    private const int NextSetSelection = -3;
    private const int NoSelection = -4;

    private static readonly string[][] StickerSets =
    {
        new[] { "wave", "happy", "cheer", "leaf", "drool", "love", "surprised", "celebrate" },
        new[] { "Set2/detective", "Set2/banana", "Set2/facepalm", "Set2/gamer", "Set2/angry", "Set2/crying", "Set2/thumbsup", "Set2/dance" }
    };

    private readonly Dictionary<ulong, float> _lastSendTime = new();
    private readonly Dictionary<ulong, GameObject> _activeStickers = new();
    private readonly List<PlayerInputHandler> _lockedInputs = new();

    private NetworkManager _boundNetworkManager;
    private Canvas _canvas;
    private RectTransform _wheel;
    private Image[] _optionFrames;
    private Image[] _optionIcons;
    private RectTransform[] _optionRects;
    private Sprite[][] _spriteSets;
    private RectTransform _cancelRect;
    private RectTransform _previousSetRect;
    private RectTransform _nextSetRect;
    private Image _cancelImage;
    private Image _previousSetImage;
    private Image _nextSetImage;
    private TMP_Text _setNumber;
    private bool _isOpen;
    private int _selectedIndex = NoSelection;
    private int _currentSetIndex;
    private PlayerInputHandler _inputHandler;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<NetworkStickerWheelController>() != null) return;
        new GameObject("NetworkStickerWheel").AddComponent<NetworkStickerWheelController>();
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        LoadSprites();
        BuildInterface();
        SetWheelOpen(false);
    }

    private void Update()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        bool connected = networkManager != null && networkManager.IsListening;

        if (connected) BindNetwork(networkManager);
        else if (_boundNetworkManager != null) UnbindNetwork();

        if (_isOpen && !CanUseStickerWheel())
        {
            SetWheelOpen(false);
            return;
        }

        ResolveInputHandler();
        Keyboard keyboard = Keyboard.current;
        bool keyboardHeld = keyboard != null && keyboard.gKey.isPressed;
        bool stickerBindingHeld = _inputHandler != null && _inputHandler.IsOwner
            ? _inputHandler.StickerWheelHeld
            : keyboardHeld;

        if (_isOpen)
        {
            if (WasStickerCancelPressed())
            {
                SetWheelOpen(false);
                return;
            }

            if (WasPreviousSetPressed()) ChangeStickerSet(-1);
            else if (WasNextSetPressed()) ChangeStickerSet(1);

            UpdateSelection();
            if (!stickerBindingHeld)
            {
                ApplySelection();
                SetWheelOpen(false);
            }
            return;
        }

        bool stickerPressed = _inputHandler != null && _inputHandler.IsOwner
            ? _inputHandler.StickerWheelHeld
            : keyboard != null && keyboard.gKey.wasPressedThisFrame;
        if (!stickerPressed || IsAnotherTextFieldSelected()) return;
        if (CanUseStickerWheel()) SetWheelOpen(true);
    }

    private bool WasStickerCancelPressed()
    {
        if (_inputHandler != null && _inputHandler.IsOwner)
            return _inputHandler.StickerCancelPressed;

        return Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
    }

    private bool WasPreviousSetPressed()
    {
        if (_inputHandler != null && _inputHandler.IsOwner)
            return _inputHandler.StickerPreviousSetPressed;

        return Gamepad.current != null && Gamepad.current.dpad.left.wasPressedThisFrame;
    }

    private bool WasNextSetPressed()
    {
        if (_inputHandler != null && _inputHandler.IsOwner)
            return _inputHandler.StickerNextSetPressed;

        return Gamepad.current != null && Gamepad.current.dpad.right.wasPressedThisFrame;
    }

    private void OnDestroy()
    {
        SetWheelOpen(false);
        UnbindNetwork();
    }

    private bool CanUseStickerWheel()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName.Contains("Lobby", System.StringComparison.OrdinalIgnoreCase) ||
            sceneName.Contains("Menu", System.StringComparison.OrdinalIgnoreCase))
            return false;

        if (UICursorLockService.HasOtherOwner(this)) return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening &&
               networkManager.LocalClient?.PlayerObject != null;
    }

    private static bool IsAnotherTextFieldSelected()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null && selected.GetComponentInParent<TMP_InputField>() != null;
    }

    private void SetWheelOpen(bool open)
    {
        _isOpen = open;
        _selectedIndex = NoSelection;
        if (_canvas != null) _canvas.gameObject.SetActive(open);
        UpdateHighlights();

        if (open)
        {
            LockGameplayInput();
            CameraManager.Instance?.SetGameplayCameraLocked(true);
            UICursorLockService.Request(this);
        }
        else
        {
            UnlockGameplayInput();
            CameraManager.Instance?.SetGameplayCameraLocked(false);
            UICursorLockService.Release(this);
        }
    }

    private void UpdateSelection()
    {
        ResolveInputHandler();
        Vector2 stick = _inputHandler != null && _inputHandler.IsOwner
            ? _inputHandler.StickerNavigateInput
            : Gamepad.current != null ? Gamepad.current.rightStick.ReadValue() : Vector2.zero;
        if (stick.sqrMagnitude >= 0.16f)
        {
            float angle = Mathf.Atan2(stick.y, stick.x) * Mathf.Rad2Deg;
            int index = Mathf.RoundToInt((90f - angle) / 45f);
            _selectedIndex = (index + 8) % 8;
            UpdateHighlights();
            return;
        }

        if (Mouse.current == null) return;
        Vector2 pointer = Mouse.current.position.ReadValue();
        int closestIndex = NoSelection;
        float closestDistance = 72f * _canvas.scaleFactor;

        for (int i = 0; i < _optionRects.Length; i++)
        {
            Vector2 optionPosition = RectTransformUtility.WorldToScreenPoint(null, _optionRects[i].position);
            float distance = Vector2.Distance(pointer, optionPosition);
            if (distance >= closestDistance) continue;
            closestDistance = distance;
            closestIndex = i;
        }

        if (closestIndex == NoSelection && IsPointerNear(pointer, _previousSetRect, 40f))
            closestIndex = PreviousSetSelection;
        else if (closestIndex == NoSelection && IsPointerNear(pointer, _nextSetRect, 40f))
            closestIndex = NextSetSelection;
        else if (closestIndex == NoSelection && IsPointerNear(pointer, _cancelRect, 78f))
            closestIndex = CancelSelection;

        if (_selectedIndex == closestIndex) return;
        _selectedIndex = closestIndex;
        UpdateHighlights();
    }

    private void UpdateHighlights()
    {
        if (_optionFrames == null) return;
        for (int i = 0; i < _optionFrames.Length; i++)
        {
            bool selected = i == _selectedIndex;
            _optionFrames[i].color = selected
                ? new Color(1f, 0.82f, 0.22f, 1f)
                : new Color(0.94f, 0.98f, 0.92f, 0.92f);
            _optionRects[i].localScale = Vector3.one * (selected ? 1.14f : 1f);
        }

        SetControlHighlight(_cancelImage, _cancelRect, _selectedIndex == CancelSelection, new Color(0.025f, 0.20f, 0.16f, 0.98f));
        SetControlHighlight(_previousSetImage, _previousSetRect, _selectedIndex == PreviousSetSelection, new Color(0.05f, 0.30f, 0.25f, 0.98f));
        SetControlHighlight(_nextSetImage, _nextSetRect, _selectedIndex == NextSetSelection, new Color(0.05f, 0.30f, 0.25f, 0.98f));
    }

    private static bool IsPointerNear(Vector2 pointer, RectTransform target, float radius)
    {
        if (target == null) return false;
        Vector2 position = RectTransformUtility.WorldToScreenPoint(null, target.position);
        Canvas canvas = target.GetComponentInParent<Canvas>();
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        return Vector2.Distance(pointer, position) <= radius * scale;
    }

    private static void SetControlHighlight(Image image, RectTransform rect, bool selected, Color normalColor)
    {
        if (image == null || rect == null) return;
        image.color = selected ? new Color(1f, 0.72f, 0.18f, 1f) : normalColor;
        rect.localScale = Vector3.one * (selected ? 1.12f : 1f);
    }

    private void ApplySelection()
    {
        if (_selectedIndex >= 0)
        {
            SendSticker(_currentSetIndex, _selectedIndex);
            return;
        }

        if (_selectedIndex == PreviousSetSelection)
            ChangeStickerSet(-1);
        else if (_selectedIndex == NextSetSelection)
            ChangeStickerSet(1);
    }

    private void ChangeStickerSet(int direction)
    {
        _currentSetIndex = (_currentSetIndex + direction + StickerSets.Length) % StickerSets.Length;
        RefreshOptionSprites();
    }

    private void RefreshOptionSprites()
    {
        if (_optionIcons == null) return;
        for (int i = 0; i < _optionIcons.Length; i++)
            _optionIcons[i].sprite = _spriteSets[_currentSetIndex][i];
        if (_setNumber != null) _setNumber.text = $"{_currentSetIndex + 1}/{StickerSets.Length}";
    }

    private void SendSticker(int setIndex, int stickerIndex)
    {
        if (_boundNetworkManager == null || !_boundNetworkManager.IsListening) return;
        using FastBufferWriter writer = new(16, Allocator.Temp);
        writer.WriteValueSafe((byte)setIndex);
        writer.WriteValueSafe((byte)stickerIndex);
        _boundNetworkManager.CustomMessagingManager.SendNamedMessage(
            SendMessageName,
            NetworkManager.ServerClientId,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    private void BindNetwork(NetworkManager networkManager)
    {
        if (_boundNetworkManager == networkManager) return;
        UnbindNetwork();
        _boundNetworkManager = networkManager;
        _boundNetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(SendMessageName, HandleSendRequest);
        _boundNetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(ReceiveMessageName, HandleReceive);
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
        _lastSendTime.Clear();
    }

    private void HandleSendRequest(ulong senderClientId, FastBufferReader reader)
    {
        if (_boundNetworkManager == null || !_boundNetworkManager.IsServer) return;
        if (!_boundNetworkManager.ConnectedClients.ContainsKey(senderClientId)) return;

        reader.ReadValueSafe(out byte setIndex);
        reader.ReadValueSafe(out byte stickerIndex);
        if (setIndex >= StickerSets.Length || stickerIndex >= StickerSets[setIndex].Length) return;

        float now = Time.unscaledTime;
        if (_lastSendTime.TryGetValue(senderClientId, out float lastTime) &&
            now - lastTime < MinimumSendInterval)
            return;
        _lastSendTime[senderClientId] = now;

        using FastBufferWriter writer = new(32, Allocator.Temp);
        writer.WriteValueSafe(senderClientId);
        writer.WriteValueSafe(setIndex);
        writer.WriteValueSafe(stickerIndex);
        _boundNetworkManager.CustomMessagingManager.SendNamedMessage(
            ReceiveMessageName,
            _boundNetworkManager.ConnectedClientsIds,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    private void HandleReceive(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong authorClientId);
        reader.ReadValueSafe(out byte setIndex);
        reader.ReadValueSafe(out byte stickerIndex);
        if (setIndex >= _spriteSets.Length ||
            stickerIndex >= _spriteSets[setIndex].Length ||
            _spriteSets[setIndex][stickerIndex] == null)
            return;
        ShowSticker(authorClientId, setIndex, stickerIndex);
    }

    private void ShowSticker(ulong clientId, int setIndex, int stickerIndex)
    {
        if (_boundNetworkManager == null ||
            !_boundNetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
            client.PlayerObject == null)
            return;

        if (_activeStickers.TryGetValue(clientId, out GameObject previous) && previous != null)
            Destroy(previous);

        GameObject root = new($"StickerOverlay_{clientId}", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1450;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Image sticker = new GameObject("StickerImage", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        sticker.transform.SetParent(root.transform, false);
        sticker.rectTransform.anchorMin = sticker.rectTransform.anchorMax = sticker.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        sticker.rectTransform.anchoredPosition = Vector2.zero;
        sticker.rectTransform.sizeDelta = new Vector2(180f, 180f);
        sticker.sprite = _spriteSets[setIndex][stickerIndex];
        sticker.preserveAspect = true;
        sticker.raycastTarget = false;

        StickerScreenFollower follower = root.AddComponent<StickerScreenFollower>();
        follower.Configure(
            client.PlayerObject.transform,
            root.GetComponent<RectTransform>(),
            sticker.rectTransform,
            canvas,
            StickerHeightAbovePlayer,
            StickerWorldSize);
        _activeStickers[clientId] = root;
        StartCoroutine(RemoveStickerAfterDelay(clientId, root));
    }

    private IEnumerator RemoveStickerAfterDelay(ulong clientId, GameObject sticker)
    {
        yield return new WaitForSecondsRealtime(StickerDuration);
        if (_activeStickers.TryGetValue(clientId, out GameObject current) && current == sticker)
        {
            _activeStickers.Remove(clientId);
            if (sticker != null) Destroy(sticker);
        }
    }

    private void LockGameplayInput()
    {
        UnlockGameplayInput();
        foreach (PlayerInputHandler handler in FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None))
        {
            if (!handler.IsOwner) continue;
            handler.LockAllInput();
            _lockedInputs.Add(handler);
        }
    }

    private void UnlockGameplayInput()
    {
        foreach (PlayerInputHandler handler in _lockedInputs)
            if (handler != null) handler.UnlockAllInput();
        _lockedInputs.Clear();
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

    private void LoadSprites()
    {
        _spriteSets = new Sprite[StickerSets.Length][];
        for (int setIndex = 0; setIndex < StickerSets.Length; setIndex++)
        {
            _spriteSets[setIndex] = new Sprite[StickerSets[setIndex].Length];
            for (int stickerIndex = 0; stickerIndex < StickerSets[setIndex].Length; stickerIndex++)
            {
                Texture2D texture = Resources.Load<Texture2D>($"Stickers/{StickerSets[setIndex][stickerIndex]}");
                if (texture == null) continue;
                _spriteSets[setIndex][stickerIndex] = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
            }
        }
    }

    private void BuildInterface()
    {
        _canvas = new GameObject("StickerWheelCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
        _canvas.transform.SetParent(transform, false);
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1600;

        CanvasScaler scaler = _canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Image dimmer = new GameObject("Dimmer", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        dimmer.transform.SetParent(_canvas.transform, false);
        dimmer.rectTransform.anchorMin = Vector2.zero;
        dimmer.rectTransform.anchorMax = Vector2.one;
        dimmer.rectTransform.offsetMin = dimmer.rectTransform.offsetMax = Vector2.zero;
        dimmer.color = new Color(0.01f, 0.05f, 0.04f, 0.46f);
        dimmer.raycastTarget = false;

        _wheel = new GameObject("StickerWheel", typeof(RectTransform)).GetComponent<RectTransform>();
        _wheel.SetParent(_canvas.transform, false);
        _wheel.anchorMin = _wheel.anchorMax = _wheel.pivot = new Vector2(0.5f, 0.5f);
        _wheel.sizeDelta = new Vector2(620f, 620f);

        int stickerCount = StickerSets[0].Length;
        _optionFrames = new Image[stickerCount];
        _optionIcons = new Image[stickerCount];
        _optionRects = new RectTransform[stickerCount];
        Sprite circleSprite = CreateCircleSprite();
        const float radius = 230f;

        for (int i = 0; i < stickerCount; i++)
        {
            float angle = (90f - i * 45f) * Mathf.Deg2Rad;
            RectTransform frame = new GameObject($"StickerOption_{i + 1}", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            frame.SetParent(_wheel, false);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            frame.sizeDelta = new Vector2(132f, 132f);
            Image frameImage = frame.GetComponent<Image>();
            frameImage.sprite = circleSprite;
            frameImage.color = new Color(0.94f, 0.98f, 0.92f, 0.92f);
            frameImage.raycastTarget = false;

            Image icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(frame, false);
            icon.rectTransform.anchorMin = Vector2.zero;
            icon.rectTransform.anchorMax = Vector2.one;
            icon.rectTransform.offsetMin = new Vector2(7f, 7f);
            icon.rectTransform.offsetMax = new Vector2(-7f, -7f);
            icon.sprite = _spriteSets[_currentSetIndex][i];
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            _optionFrames[i] = frameImage;
            _optionIcons[i] = icon;
            _optionRects[i] = frame;
        }

        _cancelRect = new GameObject("Cancel_G", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _cancelRect.SetParent(_wheel, false);
        _cancelRect.anchorMin = _cancelRect.anchorMax = _cancelRect.pivot = new Vector2(0.5f, 0.5f);
        _cancelRect.sizeDelta = new Vector2(150f, 150f);
        _cancelImage = _cancelRect.GetComponent<Image>();
        _cancelImage.sprite = circleSprite;
        _cancelImage.color = new Color(0.025f, 0.20f, 0.16f, 0.98f);

        TMP_Text key = CreateText(_cancelRect, "G / LB", 34f, Color.white);
        key.fontStyle = FontStyles.Bold;
        key.rectTransform.anchorMin = Vector2.zero;
        key.rectTransform.anchorMax = Vector2.one;
        key.rectTransform.offsetMin = key.rectTransform.offsetMax = Vector2.zero;

        TMP_Text cancel = CreateText(_cancelRect, "×", 42f, new Color(1f, 0.25f, 0.20f, 1f));
        cancel.fontStyle = FontStyles.Bold;
        cancel.rectTransform.anchorMin = cancel.rectTransform.anchorMax = cancel.rectTransform.pivot = new Vector2(1f, 1f);
        cancel.rectTransform.anchoredPosition = new Vector2(-9f, -5f);
        cancel.rectTransform.sizeDelta = new Vector2(52f, 52f);

        _setNumber = CreateText(_cancelRect, string.Empty, 16f, new Color(0.75f, 0.92f, 0.82f, 1f));
        _setNumber.fontStyle = FontStyles.Bold;
        _setNumber.rectTransform.anchorMin = _setNumber.rectTransform.anchorMax = _setNumber.rectTransform.pivot = new Vector2(0.5f, 0f);
        _setNumber.rectTransform.anchoredPosition = new Vector2(0f, 13f);
        _setNumber.rectTransform.sizeDelta = new Vector2(70f, 24f);

        _previousSetRect = CreatePageControl("PreviousSet", _wheel, circleSprite, "<", new Vector2(-125f, 0f), out _previousSetImage);
        _nextSetRect = CreatePageControl("NextSet", _wheel, circleSprite, ">", new Vector2(125f, 0f), out _nextSetImage);
        RefreshOptionSprites();
    }

    private static RectTransform CreatePageControl(
        string name,
        Transform parent,
        Sprite circleSprite,
        string arrow,
        Vector2 position,
        out Image image)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(72f, 72f);
        image = rect.GetComponent<Image>();
        image.sprite = circleSprite;
        image.color = new Color(0.05f, 0.30f, 0.25f, 0.98f);

        TMP_Text label = CreateText(rect, arrow, 42f, Color.white);
        label.fontStyle = FontStyles.Bold;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        return rect;
    }

    private static TMP_Text CreateText(Transform parent, string value, float size, Color color)
    {
        TextMeshProUGUI text = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(parent, false);
        text.text = value;
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private static Sprite CreateCircleSprite()
    {
        const int size = 64;
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            name = "RuntimeStickerWheelCircle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[size * size];
        Vector2 center = Vector2.one * (size * 0.5f);
        float radius = size * 0.48f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
            byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(radius - distance + 0.5f) * 255f);
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }
}

public sealed class StickerScreenFollower : MonoBehaviour
{
    private Transform _target;
    private RectTransform _canvasRect;
    private RectTransform _visual;
    private Canvas _canvas;
    private float _height;
    private float _worldSize;

    public void Configure(
        Transform target,
        RectTransform canvasRect,
        RectTransform visual,
        Canvas canvas,
        float height,
        float worldSize)
    {
        _target = target;
        _canvasRect = canvasRect;
        _visual = visual;
        _canvas = canvas;
        _height = height;
        _worldSize = worldSize;
    }

    private void LateUpdate()
    {
        Camera camera = Camera.main;
        if (_target == null || camera == null || _canvasRect == null || _visual == null) return;

        Vector3 worldCenter = _target.position + Vector3.up * _height;
        Vector3 screenCenter = camera.WorldToScreenPoint(worldCenter);
        bool visible = screenCenter.z > 0f;
        if (_visual.gameObject.activeSelf != visible) _visual.gameObject.SetActive(visible);
        if (!visible) return;

        Vector3 screenEdge = camera.WorldToScreenPoint(worldCenter + camera.transform.up * (_worldSize * 0.5f));
        float scaleFactor = _canvas != null ? Mathf.Max(0.01f, _canvas.scaleFactor) : 1f;
        float size = Mathf.Clamp(Mathf.Abs(screenEdge.y - screenCenter.y) * 2f / scaleFactor, 105f, 220f);
        _visual.sizeDelta = new Vector2(size, size);

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenCenter, null, out Vector2 localPoint))
        {
            Rect bounds = _canvasRect.rect;
            float halfSize = size * 0.5f + 6f;
            if (bounds.width > halfSize * 2f && bounds.height > halfSize * 2f)
            {
                localPoint.x = Mathf.Clamp(localPoint.x, bounds.xMin + halfSize, bounds.xMax - halfSize);
                localPoint.y = Mathf.Clamp(localPoint.y, bounds.yMin + halfSize, bounds.yMax - halfSize);
            }
            _visual.anchoredPosition = localPoint;
        }
    }
}
