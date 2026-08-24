using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Runtime virtual keyboard for text fields that must be usable without a physical keyboard.
/// The owner supplies the target field and receives submit/cancel callbacks.
/// </summary>
public sealed class GamepadVirtualKeyboard : MonoBehaviour
{
    private static readonly Color OverlayColor = new(0.01f, 0.035f, 0.045f, 0.98f);
    private static readonly Color CardColor = new(0.035f, 0.22f, 0.23f, 1f);
    private static readonly Color KeyColor = new(0.08f, 0.34f, 0.35f, 1f);
    private static readonly Color ActionColor = new(0.12f, 0.68f, 0.54f, 1f);
    private static readonly Color CancelColor = new(0.48f, 0.25f, 0.32f, 1f);

    private readonly List<List<Button>> _rows = new();
    private readonly List<(Button button, char character)> _characterButtons = new();

    private RectTransform _overlay;
    private TMP_Text _title;
    private TMP_Text _preview;
    private TMP_InputField _target;
    private Action _onSubmit;
    private Action _onCancel;
    private InputAction _cancelAction;
    private bool _uppercase = true;

    public bool IsVisible => _overlay != null && _overlay.gameObject.activeSelf;

    /// <summary>Builds the keyboard under the supplied canvas transform.</summary>
    public void Initialize(Transform parent)
    {
        if (_overlay != null || parent == null) return;

        EnsureEventSystem();
        _overlay = CreateRect(parent, "GamepadVirtualKeyboard", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero);
        _overlay.gameObject.AddComponent<Image>().color = OverlayColor;

        RectTransform card = CreateCard(_overlay, "KeyboardCard", new Vector2(0.5f, 0.5f), new Vector2(1180f, 760f));
        _title = CreateText(card, "Title", string.Empty, 30f, Color.white, TextAlignmentOptions.Center);
        Place(_title.rectTransform, new Vector2(0f, -34f), new Vector2(1040f, 50f), new Vector2(0.5f, 1f));

        RectTransform previewRoot = CreateRect(card, "Preview", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -102f), new Vector2(1020f, 70f), new Vector2(0.5f, 1f));
        previewRoot.gameObject.AddComponent<Image>().color = new Color(0.92f, 0.95f, 0.91f, 1f);
        _preview = CreateText(previewRoot, "Text", string.Empty, 23f, new Color(0.02f, 0.07f, 0.08f, 1f), TextAlignmentOptions.Center);
        Stretch(_preview.rectTransform, Vector2.zero, Vector2.zero);

        AddCharacterRow(card, "ABCDEFGHIJ", -215f);
        AddCharacterRow(card, "KLMNOPQRS", -310f);
        AddCharacterRow(card, "TUVWXYZ123", -405f);
        AddCharacterRow(card, "456789-_.@", -500f);

        Button shift = CreateButton(card, "Aa", KeyColor, new Vector2(-475f, -620f), new Vector2(150f, 76f));
        shift.onClick.AddListener(ToggleCase);
        Button space = CreateButton(card, "SPACE", KeyColor, new Vector2(-230f, -620f), new Vector2(300f, 76f));
        space.onClick.AddListener(() => AppendCharacter(' '));
        Button backspace = CreateButton(card, "BACKSPACE", KeyColor, new Vector2(65f, -620f), new Vector2(220f, 76f));
        backspace.onClick.AddListener(RemoveLastCharacter);
        Button submit = CreateButton(card, "SEND", ActionColor, new Vector2(285f, -620f), new Vector2(170f, 76f));
        submit.onClick.AddListener(Submit);
        Button cancel = CreateButton(card, "CANCEL", CancelColor, new Vector2(465f, -620f), new Vector2(150f, 76f));
        cancel.onClick.AddListener(Cancel);
        _rows.Add(new List<Button> { shift, space, backspace, submit, cancel });

        ConfigureNavigation();
        _overlay.gameObject.SetActive(false);
        BindCancelAction();
    }

    /// <summary>Shows the keyboard and routes its result to the supplied callbacks.</summary>
    public void Show(TMP_InputField target, string title, Action onSubmit, Action onCancel)
    {
        if (_overlay == null || target == null) return;

        BindCancelAction();
        _target = target;
        _onSubmit = onSubmit;
        _onCancel = onCancel;
        _target.DeactivateInputField();
        _uppercase = true;
        _title.text = title ?? "VIRTUAL KEYBOARD";
        UpdateCharacterLabels();
        RefreshPreview();
        _overlay.gameObject.SetActive(true);
        _overlay.SetAsLastSibling();
        FocusFirstKey();
    }

    /// <summary>Closes the keyboard without invoking submit or cancel.</summary>
    public void HideWithoutCallback()
    {
        HideInternal();
    }

    /// <summary>Refreshes the preview after the owner changes the target text.</summary>
    public void RefreshPreview()
    {
        if (_preview == null || _target == null) return;
        _preview.text = _target.text ?? string.Empty;
    }

    /// <summary>Restores focus to the first virtual key.</summary>
    public void FocusFirstKey()
    {
        if (_rows.Count == 0 || EventSystem.current == null) return;
        EventSystem.current.SetSelectedGameObject(_rows[0][0].gameObject);
    }

    private void BindCancelAction()
    {
        InputSystemUIInputModule module = EventSystem.current?.GetComponent<InputSystemUIInputModule>();
        InputAction action = module?.cancel?.action;
        if (_cancelAction == action) return;
        UnbindCancelAction();
        _cancelAction = action;
        if (_cancelAction != null) _cancelAction.performed += HandleCancelPerformed;
    }

    private void UnbindCancelAction()
    {
        if (_cancelAction != null) _cancelAction.performed -= HandleCancelPerformed;
        _cancelAction = null;
    }

    private void HandleCancelPerformed(InputAction.CallbackContext context)
    {
        if (IsVisible) Cancel();
    }

    private void Submit()
    {
        if (!IsVisible) return;
        Action callback = _onSubmit;
        HideInternal();
        callback?.Invoke();
    }

    private void Cancel()
    {
        if (!IsVisible) return;
        Action callback = _onCancel;
        HideInternal();
        callback?.Invoke();
    }

    private void HideInternal()
    {
        GameObject selected = EventSystem.current?.currentSelectedGameObject;
        if (selected != null && _overlay != null && selected.transform.IsChildOf(_overlay))
            EventSystem.current.SetSelectedGameObject(null);
        if (_overlay != null) _overlay.gameObject.SetActive(false);
        _target = null;
        _onSubmit = null;
        _onCancel = null;
    }

    private void AddCharacterRow(Transform parent, string characters, float y)
    {
        const float keyWidth = 82f;
        const float spacing = 14f;
        float rowWidth = characters.Length * keyWidth + (characters.Length - 1) * spacing;
        float startX = -rowWidth * 0.5f + keyWidth * 0.5f;
        List<Button> row = new(characters.Length);

        for (int i = 0; i < characters.Length; i++)
        {
            char character = characters[i];
            Button key = CreateButton(parent, character.ToString(), KeyColor,
                new Vector2(startX + i * (keyWidth + spacing), y), new Vector2(keyWidth, 70f));
            key.onClick.AddListener(() => AppendCharacter(character));
            row.Add(key);
            _characterButtons.Add((key, character));
        }

        _rows.Add(row);
    }

    private void AppendCharacter(char character)
    {
        if (_target == null) return;
        int limit = _target.characterLimit > 0 ? _target.characterLimit : 180;
        string current = _target.text ?? string.Empty;
        if (current.Length >= limit) return;
        if (char.IsLetter(character)) character = _uppercase ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character);
        _target.SetTextWithoutNotify(current + character);
        RefreshPreview();
    }

    private void RemoveLastCharacter()
    {
        if (_target == null || string.IsNullOrEmpty(_target.text)) return;
        _target.SetTextWithoutNotify(_target.text.Substring(0, _target.text.Length - 1));
        RefreshPreview();
    }

    private void ToggleCase()
    {
        _uppercase = !_uppercase;
        UpdateCharacterLabels();
    }

    private void UpdateCharacterLabels()
    {
        foreach ((Button button, char character) in _characterButtons)
        {
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = (_uppercase ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character)).ToString();
        }
    }

    private void ConfigureNavigation()
    {
        for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            List<Button> row = _rows[rowIndex];
            for (int column = 0; column < row.Count; column++)
            {
                Button key = row[column];
                Selectable up = rowIndex > 0 ? FindNearestHorizontalKey(key, _rows[rowIndex - 1]) : null;
                Selectable down = rowIndex < _rows.Count - 1 ? FindNearestHorizontalKey(key, _rows[rowIndex + 1]) : null;
                Selectable left = column > 0 ? row[column - 1] : null;
                Selectable right = column < row.Count - 1 ? row[column + 1] : null;
                SetExplicitNavigation(key, up, down, left, right);
            }
        }
    }

    private static Button FindNearestHorizontalKey(Button source, IReadOnlyList<Button> candidates)
    {
        if (source == null || candidates == null || candidates.Count == 0) return null;
        float sourceX = ((RectTransform)source.transform).anchoredPosition.x;
        Button nearest = candidates[0];
        float distance = Mathf.Abs(((RectTransform)nearest.transform).anchoredPosition.x - sourceX);
        for (int i = 1; i < candidates.Count; i++)
        {
            float candidateDistance = Mathf.Abs(((RectTransform)candidates[i].transform).anchoredPosition.x - sourceX);
            if (candidateDistance >= distance) continue;
            nearest = candidates[i];
            distance = candidateDistance;
        }
        return nearest;
    }

    private static void SetExplicitNavigation(Selectable selectable, Selectable up, Selectable down, Selectable left, Selectable right)
    {
        Navigation navigation = selectable.navigation;
        navigation.mode = Navigation.Mode.Explicit;
        navigation.selectOnUp = up;
        navigation.selectOnDown = down;
        navigation.selectOnLeft = left;
        navigation.selectOnRight = right;
        selectable.navigation = navigation;
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = EventSystem.current ?? UnityEngine.Object.FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
            eventSystem = new GameObject("ChatEventSystem", typeof(EventSystem)).GetComponent<EventSystem>();

        eventSystem.enabled = true;
        StandaloneInputModule legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
        InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null)
        {
            inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        }
        if (legacyModule != null) legacyModule.enabled = false;
        if (inputModule.actionsAsset == null) inputModule.AssignDefaultActions();
        inputModule.enabled = true;
    }

    private static RectTransform CreateCard(Transform parent, string name, Vector2 anchor, Vector2 size)
    {
        RectTransform card = CreateRect(parent, name, anchor, anchor, Vector2.zero, size, anchor);
        Image image = card.gameObject.AddComponent<Image>();
        image.color = CardColor;
        return card;
    }

    private static Button CreateButton(Transform parent, string label, Color color, Vector2 position, Vector2 size)
    {
        RectTransform rect = CreateRect(parent, $"Key_{label}", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size, new Vector2(0.5f, 0.5f));
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.2f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.2f);
        button.colors = colors;

        TMP_Text text = CreateText(rect, "Label", label, 18f, Color.white, TextAlignmentOptions.Center);
        Stretch(text.rectTransform, new Vector2(4f, 2f), new Vector2(-4f, -2f));
        return button;
    }

    private static TMP_Text CreateText(Transform parent, string name, string value, float size, Color color, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(parent, false);
        text.text = value;
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.fontSizeMin = Mathf.Max(10f, size * 0.6f);
        text.fontSizeMax = size;
        text.enableAutoSizing = true;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2 pivot)
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

    private static void Place(RectTransform rect, Vector2 position, Vector2 size, Vector2 anchor)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private void OnDestroy()
    {
        UnbindCancelAction();
    }
}
