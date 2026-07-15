using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.UI.LobbyAuto
{
    /// <summary>
    /// Installs the redesigned presentation on both the production Lobby scene and the standalone
    /// LobbyRemake preview scene without changing their serialized hierarchy.
    /// </summary>
    public static class LobbyRemakeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            InstallForScene(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            InstallForScene(scene);
        }

        private static void InstallForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || !scene.name.Contains("Lobby")) return;

            // The production scene still contains the original LobbyUI component. Keep the
            // serialized object intact for backwards compatibility, but prevent it from wiring
            // a second set of button listeners or starting a second NGO host behind the remake.
            Networking.LobbySystem.LobbyUI[] legacyControllers =
                Object.FindObjectsByType<Networking.LobbySystem.LobbyUI>(FindObjectsSortMode.None);
            foreach (Networking.LobbySystem.LobbyUI legacyController in legacyControllers)
            {
                legacyController.enabled = false;
            }

            if (Object.FindFirstObjectByType<LobbyAutoController>() != null) return;

            GameObject root = new("LobbyRemake_Interface");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<LobbyAutoController>();
        }
    }
}
