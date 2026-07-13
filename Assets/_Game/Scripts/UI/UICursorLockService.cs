using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates cursor state for modal UI surfaces so panels do not fight each other.
/// </summary>
public static class UICursorLockService
{
    private static readonly HashSet<object> Owners = new();

    public static bool IsCursorReleased => Owners.Count > 0;

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
        bool showCursor = Owners.Count > 0;
        Cursor.lockState = showCursor ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = showCursor;
    }
}
