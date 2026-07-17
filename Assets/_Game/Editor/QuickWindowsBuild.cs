using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

internal static class QuickWindowsBuild
{
    private const string MenuPath = "Tools/The Flower/Update Windows Build %#&F12";

    [MenuItem(MenuPath, priority = 100)]
    private static void UpdateWindowsBuild()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
        {
            Debug.LogError("[QuickBuild] Cannot resolve the project root.");
            return;
        }

        string outputPath = Path.Combine(projectRoot, "Builds", "Windows", "TheFlower.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("The Flower - Quick Build", "No enabled scenes were found in Build Profiles.", "OK");
            return;
        }

        Debug.Log($"[QuickBuild] Updating Windows build at: {outputPath}");
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });

        BuildSummary summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[QuickBuild] Success in {summary.totalTime}. Size: {summary.totalSize / (1024f * 1024f):0.0} MB");
            EditorUtility.DisplayDialog(
                "The Flower - Quick Build",
                "Windows build updated successfully.\n\nBuilds/Windows/TheFlower.exe",
                "OK");
        }
        else
        {
            Debug.LogError($"[QuickBuild] Failed: {summary.result}, errors: {summary.totalErrors}");
            EditorUtility.DisplayDialog(
                "The Flower - Quick Build",
                $"Build failed: {summary.result}\nErrors: {summary.totalErrors}\nCheck the Console for details.",
                "OK");
        }
    }

    [MenuItem(MenuPath, validate = true)]
    private static bool CanUpdateWindowsBuild()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode
            && !EditorApplication.isCompiling
            && !BuildPipeline.isBuildingPlayer;
    }
}
