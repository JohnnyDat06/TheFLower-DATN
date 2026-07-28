#if UNITY_EDITOR
using System;
using System.Linq;
using Unity.Multiplayer.Playmode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Multiplayer Play Mode clones use a read-only Asset Database. When their local Build Profile
/// cache becomes stale, Unity can enter Play Mode with zero build scenes and NGO cannot resolve
/// server scene hashes. Re-apply the authoritative enabled scene list in-memory before Play Mode.
/// This runs only in MPPM clone editors; the main editor and player builds are untouched.
/// </summary>
[InitializeOnLoad]
internal static class MppmCloneBuildSceneBootstrap
{
    private static readonly string[] RequiredScenePaths =
    {
        "Assets/_Game/Scenes/_MainMenu/Lobby.unity",
        "Assets/_Game/Scenes/DatScense/Map4_Flying.unity",
        "Assets/_Game/Scenes/_Core/Map1_Main.unity",
        "Assets/_Game/Scenes/_Core/Map2_Main.unity",
        "Assets/_Game/Scenes/_MainMenu/LobbyRemake.unity"
    };

    static MppmCloneBuildSceneBootstrap()
    {
        if (CurrentPlayer.IsMainEditor)
            return;

        ApplyRequiredScenes();
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode ||
            state == PlayModeStateChange.ExitingEditMode)
        {
            ApplyRequiredScenes();
        }
    }

    private static void ApplyRequiredScenes()
    {
        EditorBuildSettingsScene[] currentScenes = EditorBuildSettings.scenes;
        string[] currentEnabledPaths = currentScenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (currentEnabledPaths.SequenceEqual(RequiredScenePaths, StringComparer.Ordinal))
            return;

        string[] missingScenes = RequiredScenePaths
            .Where(path => AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            .ToArray();

        if (missingScenes.Length > 0)
        {
            Debug.LogError($"[MPPM] Cannot prepare clone build scenes. Missing: {string.Join(", ", missingScenes)}");
            return;
        }

        EditorBuildSettings.scenes = RequiredScenePaths
            .Select(path => new EditorBuildSettingsScene(path, true))
            .ToArray();

        Debug.Log($"[MPPM] Restored {RequiredScenePaths.Length} build scenes for the clone editor.");
    }
}
#endif
