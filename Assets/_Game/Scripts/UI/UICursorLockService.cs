using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Coordinates cursor state for modal UI surfaces so panels do not fight each other.
/// </summary>
public static class UICursorLockService
{
    private static readonly HashSet<object> Owners = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        Owners.Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

    public static bool IsCursorReleased => Owners.Count > 0;

    public static bool HasOtherOwner(object owner)
    {
        RemoveDestroyedOwners();

        foreach (object candidate in Owners)
        {
            if (!ReferenceEquals(candidate, owner)) return true;
        }

        return false;
    }

    public static void Request(object owner)
    {
        if (owner == null) return;

        Owners.Add(owner);
        Apply();
    }

    public static void Release(object owner)
    {
        if (owner == null) return;

        Owners.Remove(owner);
        Apply();
    }

    public static void ReleaseAll()
    {
        Owners.Clear();
        Apply();
    }

    private static void Apply()
    {
        RemoveDestroyedOwners();
        bool showCursor = Owners.Count > 0 || IsMenuScene(SceneManager.GetActiveScene().name);
        Cursor.lockState = showCursor ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = showCursor;
    }

    private static void RemoveDestroyedOwners()
    {
        Owners.RemoveWhere(owner => owner is Object unityObject && unityObject == null);
    }

    private static bool IsMenuScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return false;
        return sceneName.Contains("Lobby", System.StringComparison.OrdinalIgnoreCase) ||
               sceneName.Contains("Menu", System.StringComparison.OrdinalIgnoreCase);
    }
}
