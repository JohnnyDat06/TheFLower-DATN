#if UNITY_EDITOR
using UnityEditor;

/// <summary>Legacy menu alias kept for teammates who used the first prototype generator.</summary>
public static class LobbyRemakeGenerator
{
    [MenuItem("Tools/Auto-Generate Lobby Remake UI")]
    public static void GenerateUI()
    {
        LobbyAutoSceneGenerator.GenerateScene();
    }
}
#endif
