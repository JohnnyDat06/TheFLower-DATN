#if UNITY_EDITOR
using UnityEditor;

/// <summary>Focuses Unity's Game view when Play Mode starts so F11 enlarges the playable view.</summary>
[InitializeOnLoad]
internal static class FocusGameViewOnPlay
{
    static FocusGameViewOnPlay()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        EditorApplication.delayCall += FocusGameView;
    }

    private static void FocusGameView()
    {
        System.Type gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        if (gameViewType == null) return;
        EditorWindow gameView = EditorWindow.GetWindow(gameViewType);
        if (gameView == null) return;
        gameView.ShowTab();
        gameView.Focus();
    }
}
#endif
