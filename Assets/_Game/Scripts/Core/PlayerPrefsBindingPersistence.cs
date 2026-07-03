using UnityEngine;

/// <summary>
/// PlayerPrefsBindingPersistence — Lưu/load binding overrides qua PlayerPrefs.
/// SRP: Chỉ chịu trách nhiệm persistence, KHÔNG biết rebind logic.
/// DIP: InputRebindService phụ thuộc IInputBindingPersistence, không PlayerPrefs trực tiếp.
/// OCP: Swap sang file-based hoặc cloud save = tạo class mới implement IInputBindingPersistence.
/// </summary>
[DefaultExecutionOrder(-250)]
public class PlayerPrefsBindingPersistence : MonoBehaviour, IInputBindingPersistence
{
    // ─── IInputBindingPersistence Implementation ──────────────────────────────

    public void SaveBindings(string json, InputDeviceType deviceType)
    {
        if (string.IsNullOrEmpty(json)) return;

        PlayerPrefs.SetString(GetKey(deviceType), json);
        PlayerPrefs.Save();

#if UNITY_EDITOR || DEBUG_BUILD
        Debug.Log($"[BindingPersistence] Saved bindings for {deviceType}.");
#endif
    }

    public string LoadBindings(InputDeviceType deviceType)
    {
        return PlayerPrefs.GetString(GetKey(deviceType), string.Empty);
    }

    public void ClearBindings(InputDeviceType deviceType)
    {
        PlayerPrefs.DeleteKey(GetKey(deviceType));
        PlayerPrefs.Save();

#if UNITY_EDITOR || DEBUG_BUILD
        Debug.Log($"[BindingPersistence] Cleared bindings for {deviceType}.");
#endif
    }

    public void ClearAllBindings()
    {
        ClearBindings(InputDeviceType.KeyboardMouse);
        ClearBindings(InputDeviceType.Gamepad);

        // Migration: xóa cả key legacy
        PlayerPrefs.DeleteKey(Constants.PlayerPrefsKeys.INPUT_BINDINGS);
        PlayerPrefs.Save();

#if UNITY_EDITOR || DEBUG_BUILD
        Debug.Log("[BindingPersistence] All bindings cleared (including legacy key).");
#endif
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        MigrateOldBindings();
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Migration: nếu key cũ "inputBindings" tồn tại → copy sang 2 keys mới → xóa key cũ.
    /// Đảm bảo người chơi không mất bindings khi update game.
    /// Chỉ chạy 1 lần (key cũ bị xóa sau migration).
    /// </summary>
    private void MigrateOldBindings()
    {
        string oldJson = PlayerPrefs.GetString(Constants.PlayerPrefsKeys.INPUT_BINDINGS, string.Empty);
        if (string.IsNullOrEmpty(oldJson)) return;

        // Apply cả 2 device types (old format không phân biệt device)
        if (string.IsNullOrEmpty(LoadBindings(InputDeviceType.KeyboardMouse)))
            SaveBindings(oldJson, InputDeviceType.KeyboardMouse);

        if (string.IsNullOrEmpty(LoadBindings(InputDeviceType.Gamepad)))
            SaveBindings(oldJson, InputDeviceType.Gamepad);

        // Xóa key legacy
        PlayerPrefs.DeleteKey(Constants.PlayerPrefsKeys.INPUT_BINDINGS);
        PlayerPrefs.Save();

#if UNITY_EDITOR || DEBUG_BUILD
        Debug.Log("[BindingPersistence] Migrated legacy bindings to per-device keys.");
#endif
    }

    private string GetKey(InputDeviceType deviceType) => deviceType switch
    {
        InputDeviceType.Gamepad => Constants.PlayerPrefsKeys.INPUT_BINDINGS_GAMEPAD,
        _                      => Constants.PlayerPrefsKeys.INPUT_BINDINGS_KEYBOARD
    };
}
