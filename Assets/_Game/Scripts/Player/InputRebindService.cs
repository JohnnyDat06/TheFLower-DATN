using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles interactive input rebinding and persistence.
/// UI asks this service for display data and conflict resolution; the service owns
/// binding indices, overrides, validation, and saving.
/// </summary>
[DefaultExecutionOrder(-200)]
public class InputRebindService : MonoBehaviour, IInputRebindService
{
    [Header("Dependencies")]
    [SerializeField] private InputActionAsset _inputActions;
    [SerializeField] private PlayerPrefsBindingPersistence _persistence;

    private InputActionRebindingExtensions.RebindingOperation _rebindOp;
    private PendingConflict _pendingConflict;

    private static readonly HashSet<string> NON_REBINDABLE = new()
    {
        "Move", "CameraLook", "Pause", "SkipCutScene"
    };

    public bool IsRebinding => _rebindOp != null;
    public bool HasPendingConflict => _pendingConflict != null;
    public InputRebindConflict PendingConflict => _pendingConflict?.Info ?? default;

    private void Awake()
    {
        if (_inputActions == null)
        {
            Debug.LogError("[InputRebindService] InputActionAsset is not assigned.");
            return;
        }

        if (_persistence == null)
        {
            _persistence = GetComponent<PlayerPrefsBindingPersistence>();
            if (_persistence == null)
                _persistence = gameObject.AddComponent<PlayerPrefsBindingPersistence>();
        }

        LoadAllBindings();
    }

    private void OnDestroy()
    {
        _rebindOp?.Dispose();
        _rebindOp = null;
    }

    public void StartRebind(string actionName, InputDeviceType deviceType,
                            Action<bool, string> onComplete, Action<string> onConflict = null)
    {
        var target = deviceType == InputDeviceType.Gamepad ? InputBindingTarget.Gamepad : InputBindingTarget.Keyboard;
        StartRebind(actionName, target, onComplete, conflict => onConflict?.Invoke(conflict.ConflictActionName));
    }

    public void StartRebind(string actionName, InputBindingTarget target,
                            Action<bool, string> onComplete, Action<InputRebindConflict> onConflict = null)
    {
        DiscardPendingConflict();

        if (NON_REBINDABLE.Contains(actionName))
        {
#if UNITY_EDITOR || DEBUG_BUILD
            Debug.LogWarning($"[InputRebindService] '{actionName}' cannot be rebound.");
#endif
            onComplete?.Invoke(false, string.Empty);
            return;
        }

        var action = _inputActions.FindAction(actionName);
        if (action == null)
        {
            Debug.LogError($"[InputRebindService] Action '{actionName}' was not found.");
            onComplete?.Invoke(false, string.Empty);
            return;
        }

        int bindingIndex = FindBindingIndexForTarget(action, target, allowFallbackKeyboardMouse: true);
        if (bindingIndex < 0)
        {
            Debug.LogError($"[InputRebindService] No binding slot found for '{actionName}' on {target}.");
            onComplete?.Invoke(false, string.Empty);
            return;
        }

        string previousOverridePath = action.bindings[bindingIndex].overridePath;
        string previousEffectivePath = action.bindings[bindingIndex].effectivePath;

        action.Disable();

        var rebindBuilder = action.PerformInteractiveRebinding(bindingIndex)
            .OnMatchWaitForAnother(0.1f);

        ConfigureTargetFilters(rebindBuilder, target);

        rebindBuilder
            .OnComplete(op =>
            {
                string newKey = op.selectedControl?.displayName ?? string.Empty;
                string attemptedOverridePath = action.bindings[bindingIndex].overridePath;
                string attemptedEffectivePath = action.bindings[bindingIndex].effectivePath;

                var conflict = FindConflict(action, bindingIndex, target, attemptedEffectivePath);
                if (conflict.HasValue)
                {
                    RestoreBindingOverride(action, bindingIndex, previousOverridePath);
                    _pendingConflict = new PendingConflict(
                        new InputRebindConflict(action.name, conflict.Value.Action.name, target, newKey),
                        action,
                        bindingIndex,
                        conflict.Value.Action,
                        conflict.Value.BindingIndex,
                        attemptedOverridePath,
                        previousEffectivePath);

                    op.Dispose();
                    _rebindOp = null;
                    action.Enable();
                    onConflict?.Invoke(_pendingConflict.Info);
                    return;
                }

                op.Dispose();
                _rebindOp = null;
                action.Enable();
                SaveCurrentBindings();
                EventBus.RaiseInputBindingChanged();
                onComplete?.Invoke(true, newKey);
            })
            .OnCancel(op =>
            {
                op.Dispose();
                _rebindOp = null;
                action.Enable();
                onComplete?.Invoke(false, string.Empty);
            });

        _rebindOp = rebindBuilder.Start();
    }

    public void CancelRebind()
    {
        _rebindOp?.Cancel();
    }

    public bool ApplyPendingConflict()
    {
        if (_pendingConflict == null) return false;

        var pending = _pendingConflict;
        _pendingConflict = null;

        RestoreBindingOverride(pending.SourceAction, pending.SourceBindingIndex, pending.AttemptedOverridePath);
        pending.ConflictAction.ApplyBindingOverride(pending.ConflictBindingIndex, pending.SourcePreviousEffectivePath);

        SaveCurrentBindings();
        EventBus.RaiseInputBindingChanged();
        return true;
    }

    public void DiscardPendingConflict()
    {
        _pendingConflict = null;
    }

    public void ResetAllBindings()
    {
        DiscardPendingConflict();
        _inputActions.RemoveAllBindingOverrides();
        _persistence.ClearAllBindings();
        EventBus.RaiseInputBindingChanged();
    }

    public void ResetBindingsForDevice(InputDeviceType deviceType)
    {
        DiscardPendingConflict();

        string targetGroup = GetGroupName(deviceType);
        var playerMap = _inputActions.FindActionMap("Player");
        if (playerMap == null) return;

        foreach (var action in playerMap)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (!string.IsNullOrEmpty(binding.groups) && binding.groups.Contains(targetGroup))
                    action.RemoveBindingOverride(i);
            }
        }

        _persistence.ClearBindings(deviceType);
        EventBus.RaiseInputBindingChanged();
    }

    public string GetBindingDisplayName(string actionName, InputDeviceType deviceType)
    {
        var target = deviceType == InputDeviceType.Gamepad ? InputBindingTarget.Gamepad : InputBindingTarget.Keyboard;
        return GetBindingDisplayName(actionName, target);
    }

    public string GetBindingDisplayName(string actionName, InputBindingTarget target)
    {
        var action = _inputActions.FindAction(actionName);
        if (action == null) return string.Empty;

        int index = FindBindingIndexForTarget(action, target, allowFallbackKeyboardMouse: false);
        if (index < 0) return string.Empty;

        return NormalizeDisplayName(action.GetBindingDisplayString(index), target);
    }

    public IReadOnlyList<string> GetRebindableActionNames()
    {
        var list = new List<string>();
        var playerMap = _inputActions.FindActionMap("Player");
        if (playerMap == null) return list;

        foreach (var action in playerMap)
        {
            if (!NON_REBINDABLE.Contains(action.name))
                list.Add(action.name);
        }

        return list;
    }

    private static void ConfigureTargetFilters(InputActionRebindingExtensions.RebindingOperation rebindBuilder, InputBindingTarget target)
    {
        rebindBuilder
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/scroll");

        switch (target)
        {
            case InputBindingTarget.Keyboard:
                rebindBuilder
                    .WithControlsExcluding("<Mouse>")
                    .WithControlsExcluding("<Gamepad>")
                    .WithControlsExcluding("<Keyboard>/escape")
                    .WithCancelingThrough("<Keyboard>/escape");
                break;

            case InputBindingTarget.Mouse:
                rebindBuilder
                    .WithControlsExcluding("<Keyboard>")
                    .WithControlsExcluding("<Gamepad>")
                    .WithCancelingThrough("<Keyboard>/escape");
                break;

            case InputBindingTarget.Gamepad:
                rebindBuilder
                    .WithControlsExcluding("<Keyboard>")
                    .WithControlsExcluding("<Mouse>")
                    .WithControlsExcluding("<Gamepad>/start")
                    .WithCancelingThrough("<Gamepad>/start");
                break;
        }
    }

    private int FindBindingIndexForTarget(InputAction action, InputBindingTarget target, bool allowFallbackKeyboardMouse)
    {
        int fallbackKeyboardMouseIndex = -1;
        string targetGroup = GetGroupName(target);

        for (int i = 0; i < action.bindings.Count; i++)
        {
            var binding = action.bindings[i];
            if (binding.isComposite || binding.isPartOfComposite) continue;
            if (string.IsNullOrEmpty(binding.groups) || !binding.groups.Contains(targetGroup)) continue;

            string path = binding.effectivePath;
            if (target == InputBindingTarget.Gamepad)
                return i;

            if (fallbackKeyboardMouseIndex < 0)
                fallbackKeyboardMouseIndex = i;

            if (target == InputBindingTarget.Keyboard && IsKeyboardPath(path))
                return i;

            if (target == InputBindingTarget.Mouse && IsMousePath(path))
                return i;
        }

        return allowFallbackKeyboardMouse && target != InputBindingTarget.Gamepad ? fallbackKeyboardMouseIndex : -1;
    }

    private ConflictBinding? FindConflict(InputAction rebindingAction, int bindingIndex, InputBindingTarget target, string newPath)
    {
        if (string.IsNullOrEmpty(newPath)) return null;

        string targetGroup = GetGroupName(target);
        var playerMap = _inputActions.FindActionMap("Player");
        if (playerMap == null) return null;

        foreach (var action in playerMap)
        {
            if (action == rebindingAction) continue;

            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (binding.isComposite || binding.isPartOfComposite) continue;
                if (string.IsNullOrEmpty(binding.groups) || !binding.groups.Contains(targetGroup)) continue;
                if (!string.Equals(binding.effectivePath, newPath, StringComparison.Ordinal)) continue;

                return new ConflictBinding(action, i);
            }
        }

        return null;
    }

    private static void RestoreBindingOverride(InputAction action, int bindingIndex, string previousOverridePath)
    {
        if (string.IsNullOrEmpty(previousOverridePath))
            action.RemoveBindingOverride(bindingIndex);
        else
            action.ApplyBindingOverride(bindingIndex, previousOverridePath);
    }

    private void LoadAllBindings()
    {
        LoadBindingsForDevice(InputDeviceType.KeyboardMouse);
        LoadBindingsForDevice(InputDeviceType.Gamepad);
    }

    private void LoadBindingsForDevice(InputDeviceType deviceType)
    {
        string json = _persistence.LoadBindings(deviceType);
        if (!string.IsNullOrEmpty(json))
            _inputActions.LoadBindingOverridesFromJson(json);
    }

    private void SaveCurrentBindings()
    {
        var json = _inputActions.SaveBindingOverridesAsJson();
        _persistence.SaveBindings(json, InputDeviceType.KeyboardMouse);
        _persistence.SaveBindings(json, InputDeviceType.Gamepad);
    }

    private static string NormalizeDisplayName(string displayName, InputBindingTarget target)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return string.Empty;

        return displayName
            .Replace("Left Button", "LMB")
            .Replace("Right Button", "RMB")
            .Replace("Middle Button", "MMB")
            .Replace("Left Stick Press", "LS")
            .Replace("Right Stick Press", "RS")
            .Replace("Button South", "A")
            .Replace("Button East", "B")
            .Replace("Button West", "X")
            .Replace("Button North", "Y")
            .Replace("Start", "Menu");
    }

    private static bool IsKeyboardPath(string path) => !string.IsNullOrEmpty(path) && path.StartsWith("<Keyboard>", StringComparison.Ordinal);
    private static bool IsMousePath(string path) => !string.IsNullOrEmpty(path) && path.StartsWith("<Mouse>", StringComparison.Ordinal);

    private static string GetGroupName(InputDeviceType deviceType) =>
        deviceType == InputDeviceType.Gamepad ? "Gamepad" : "KeyboardMouse";

    private static string GetGroupName(InputBindingTarget target) =>
        target == InputBindingTarget.Gamepad ? "Gamepad" : "KeyboardMouse";

    private readonly struct ConflictBinding
    {
        public ConflictBinding(InputAction action, int bindingIndex)
        {
            Action = action;
            BindingIndex = bindingIndex;
        }

        public InputAction Action { get; }
        public int BindingIndex { get; }
    }

    private sealed class PendingConflict
    {
        public PendingConflict(
            InputRebindConflict info,
            InputAction sourceAction,
            int sourceBindingIndex,
            InputAction conflictAction,
            int conflictBindingIndex,
            string attemptedOverridePath,
            string sourcePreviousEffectivePath)
        {
            Info = info;
            SourceAction = sourceAction;
            SourceBindingIndex = sourceBindingIndex;
            ConflictAction = conflictAction;
            ConflictBindingIndex = conflictBindingIndex;
            AttemptedOverridePath = attemptedOverridePath;
            SourcePreviousEffectivePath = sourcePreviousEffectivePath;
        }

        public InputRebindConflict Info { get; }
        public InputAction SourceAction { get; }
        public int SourceBindingIndex { get; }
        public InputAction ConflictAction { get; }
        public int ConflictBindingIndex { get; }
        public string AttemptedOverridePath { get; }
        public string SourcePreviousEffectivePath { get; }
    }
}
