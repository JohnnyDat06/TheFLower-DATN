using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// InputRebindService — Cho phép người chơi đổi phím binding theo device type.
/// Implements IInputRebindService.
/// 
/// SRP: Chỉ xử lý rebind logic. Persistence delegate cho IInputBindingPersistence.
/// DIP: Phụ thuộc IInputBindingPersistence (inject qua [SerializeField]).
/// 
/// Load binding overrides trước khi PlayerInputHandler khởi tạo (Awake vs Start).
/// Fire EventBus.OnInputBindingChanged sau khi save thành công.
/// SRS §14.6
/// </summary>
[DefaultExecutionOrder(-200)]
public class InputRebindService : MonoBehaviour, IInputRebindService
{
    [Header("Dependencies")]
    [SerializeField] private InputActionAsset _inputActions;
    [SerializeField] private PlayerPrefsBindingPersistence _persistence;

    private InputActionRebindingExtensions.RebindingOperation _rebindOp;

    // Actions không được phép rebind:
    // - Move: composite binding (WASD) — rebind sẽ phá layout
    // - CameraLook: analog input (mouse delta / stick) — không có ý nghĩa rebind
    // - SkipCutScene: system action
    private static readonly HashSet<string> NON_REBINDABLE = new()
    {
        "Move", "CameraLook", "SkipCutScene"
    };

    // ─── IInputRebindService Properties ──────────────────────────────────────

    public bool IsRebinding => _rebindOp != null;

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_inputActions == null)
        {
            Debug.LogError("[InputRebindService] InputActionAsset chưa được gán trong Inspector!");
            return;
        }

        if (_persistence == null)
        {
            _persistence = GetComponent<PlayerPrefsBindingPersistence>();
            if (_persistence == null)
            {
                _persistence = gameObject.AddComponent<PlayerPrefsBindingPersistence>();
            }
        }

        // Load bindings cho cả 2 device types — phải chạy trước PlayerInputHandler.Awake()
        LoadAllBindings();
    }

    private void OnDestroy()
    {
        _rebindOp?.Dispose();
    }

    // ─── IInputRebindService Implementation ──────────────────────────────────

    /// <inheritdoc/>
    public void StartRebind(string actionName, InputDeviceType deviceType,
                            Action<bool, string> onComplete, Action<string> onConflict = null)
    {
        if (NON_REBINDABLE.Contains(actionName))
        {
#if UNITY_EDITOR || DEBUG_BUILD
            Debug.LogWarning($"[InputRebindService] '{actionName}' không thể rebind.");
#endif
            onComplete?.Invoke(false, string.Empty);
            return;
        }

        var action = _inputActions.FindAction(actionName);
        if (action == null)
        {
            Debug.LogError($"[InputRebindService] Action '{actionName}' không tìm thấy.");
            onComplete?.Invoke(false, string.Empty);
            return;
        }

        int bindingIndex = FindBindingIndexForDevice(action, deviceType);
        if (bindingIndex < 0)
        {
            Debug.LogError($"[InputRebindService] Không tìm thấy binding cho '{actionName}' trên {deviceType}.");
            onComplete?.Invoke(false, string.Empty);
            return;
        }

        // Disable action trước khi rebind (yêu cầu bắt buộc của InputSystem)
        action.Disable();

        var rebindBuilder = action.PerformInteractiveRebinding(bindingIndex)
            .OnMatchWaitForAnother(0.1f);

        // Exclude controls based on device type
        if (deviceType == InputDeviceType.KeyboardMouse)
        {
            // Keyboard rebind: exclude mouse position/delta (không rebindable)
            rebindBuilder
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Gamepad>") // Chỉ lắng nghe KB+Mouse
                .WithCancelingThrough("<Keyboard>/escape");
        }
        else
        {
            // Gamepad rebind: chỉ lắng nghe Gamepad
            rebindBuilder
                .WithControlsExcluding("<Keyboard>")
                .WithControlsExcluding("<Mouse>")
                .WithCancelingThrough("<Gamepad>/buttonEast"); // B = cancel
        }

        rebindBuilder
            .OnComplete(op =>
            {
                var newKey = op.selectedControl?.displayName ?? string.Empty;

                // Kiểm tra conflict
                var conflict = FindConflict(action, bindingIndex, deviceType);
                if (conflict != null)
                {
                    // Revert binding — để caller xử lý Overwrite/Cancel dialog
                    action.RemoveBindingOverride(bindingIndex);
                    op.Dispose();
                    _rebindOp = null;
                    action.Enable();
                    onConflict?.Invoke(conflict);
                    return;
                }

                op.Dispose();
                _rebindOp = null;
                action.Enable();
                SaveCurrentBindings();
                EventBus.RaiseInputBindingChanged();
                onComplete?.Invoke(true, newKey);

#if UNITY_EDITOR || DEBUG_BUILD
                Debug.Log($"[InputRebindService] '{actionName}' [{deviceType}] → '{newKey}'");
#endif
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

    /// <inheritdoc/>
    public void CancelRebind()
    {
        _rebindOp?.Cancel();
    }

    /// <inheritdoc/>
    public void ResetAllBindings()
    {
        _inputActions.RemoveAllBindingOverrides();
        _persistence.ClearAllBindings();
        EventBus.RaiseInputBindingChanged();

#if UNITY_EDITOR || DEBUG_BUILD
        Debug.Log("[InputRebindService] All bindings reset to default.");
#endif
    }

    /// <inheritdoc/>
    public void ResetBindingsForDevice(InputDeviceType deviceType)
    {
        string targetGroup = GetGroupName(deviceType);
        var playerMap = _inputActions.FindActionMap("Player");
        if (playerMap == null) return;

        foreach (var action in playerMap)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (!string.IsNullOrEmpty(binding.groups) && binding.groups.Contains(targetGroup))
                {
                    action.RemoveBindingOverride(i);
                }
            }
        }

        _persistence.ClearBindings(deviceType);
        EventBus.RaiseInputBindingChanged();

#if UNITY_EDITOR || DEBUG_BUILD
        Debug.Log($"[InputRebindService] Bindings reset to default for {deviceType}.");
#endif
    }

    /// <inheritdoc/>
    public string GetBindingDisplayName(string actionName, InputDeviceType deviceType)
    {
        var action = _inputActions.FindAction(actionName);
        if (action == null) return string.Empty;

        int index = FindBindingIndexForDevice(action, deviceType);
        if (index < 0) return string.Empty;

        return action.GetBindingDisplayString(index);
    }

    /// <inheritdoc/>
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

    // ─── Private Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Tìm binding index trong action thuộc group device tương ứng.
    /// Skip composite parents — chỉ tìm single bindings.
    /// </summary>
    private int FindBindingIndexForDevice(InputAction action, InputDeviceType deviceType)
    {
        string targetGroup = GetGroupName(deviceType);

        for (int i = 0; i < action.bindings.Count; i++)
        {
            var binding = action.bindings[i];
            // Skip composite parents (Move) — chúng không có path
            if (binding.isComposite) continue;
            // Skip composite parts (WASD parts) — chúng thuộc composite
            if (binding.isPartOfComposite) continue;

            if (!string.IsNullOrEmpty(binding.groups) && binding.groups.Contains(targetGroup))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Tìm action khác đang dùng cùng binding path trong cùng device group.
    /// Trả null nếu không conflict.
    /// </summary>
    private string FindConflict(InputAction rebindingAction, int bindingIndex, InputDeviceType deviceType)
    {
        var newPath = rebindingAction.bindings[bindingIndex].effectivePath;
        string targetGroup = GetGroupName(deviceType);
        var playerMap = _inputActions.FindActionMap("Player");
        if (playerMap == null) return null;

        foreach (var action in playerMap)
        {
            if (action == rebindingAction) continue;

            foreach (var binding in action.bindings)
            {
                // Chỉ check conflict trong cùng device group
                if (string.IsNullOrEmpty(binding.groups) || !binding.groups.Contains(targetGroup))
                    continue;

                if (binding.effectivePath == newPath)
                    return action.name;
            }
        }

        return null;
    }

    /// <summary>Load binding overrides cho tất cả device types.</summary>
    private void LoadAllBindings()
    {
        // Load theo thứ tự: Keyboard trước, Gamepad sau
        // InputSystem sẽ merge overrides — mỗi binding chỉ bị override 1 lần
        LoadBindingsForDevice(InputDeviceType.KeyboardMouse);
        LoadBindingsForDevice(InputDeviceType.Gamepad);
    }

    private void LoadBindingsForDevice(InputDeviceType deviceType)
    {
        string json = _persistence.LoadBindings(deviceType);
        if (!string.IsNullOrEmpty(json))
        {
            _inputActions.LoadBindingOverridesFromJson(json);

#if UNITY_EDITOR || DEBUG_BUILD
            Debug.Log($"[InputRebindService] Bindings loaded for {deviceType}.");
#endif
        }
    }

    /// <summary>Save toàn bộ binding overrides hiện tại.</summary>
    private void SaveCurrentBindings()
    {
        // InputSystem lưu tất cả overrides dưới 1 JSON — save cho cả 2 device types
        var json = _inputActions.SaveBindingOverridesAsJson();
        _persistence.SaveBindings(json, InputDeviceType.KeyboardMouse);
        _persistence.SaveBindings(json, InputDeviceType.Gamepad);
    }

    private static string GetGroupName(InputDeviceType deviceType) =>
        deviceType == InputDeviceType.Gamepad ? "Gamepad" : "KeyboardMouse";
}
