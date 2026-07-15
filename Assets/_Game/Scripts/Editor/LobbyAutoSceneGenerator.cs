#if UNITY_EDITOR
using System.Linq;
using Game.UI.LobbyAuto;
using Networking.LobbySystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LobbyAutoSceneGenerator
{
    private const string ScenePath = "Assets/_Game/Scenes/_MainMenu/LobbyRemake.unity";
    private const string NetworkManagerPrefabPath = "Assets/_Game/Prefabs/Managers/NetworkManager.prefab";
    private const string BackgroundPath = "Assets/_Game/Resources/UI/LobbyAutoBackground.png";

    [MenuItem("Tools/The Flower/Generate Lobby Remake")]
    public static void GenerateScene()
    {
        ConfigureBackgroundImport();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "LobbyRemake";

        CreateCamera();
        CreateDirectionalLight();
        CreateNetworkServices();
        CreateLobbyServices();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = Object.FindFirstObjectByType<LobbyAutoController>()?.gameObject;
        Debug.Log($"<color=#53D7CD><b>[LobbyRemake]</b></color> Generated {ScenePath}. Press Play to preview the redesigned lobby.");
    }

    private static void ConfigureBackgroundImport()
    {
        AssetDatabase.ImportAsset(BackgroundPath, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(BackgroundPath) is not TextureImporter importer) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = false;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }

    private static void CreateCamera()
    {
        GameObject cameraObject = new("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.03f, 0.07f, 0.11f, 1f);
        camera.cullingMask = 0;
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
    }

    private static void CreateDirectionalLight()
    {
        GameObject lightObject = new("Directional Light", typeof(Light));
        Light light = lightObject.GetComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.96f, 0.84f, 1f);
        light.intensity = 1f;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static void CreateNetworkServices()
    {
        GameObject networkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
        if (networkPrefab == null)
            throw new System.InvalidOperationException($"NetworkManager prefab not found at {NetworkManagerPrefabPath}");

        GameObject networkObject = (GameObject)PrefabUtility.InstantiatePrefab(networkPrefab);
        networkObject.name = "NetworkManager";

        if (networkObject.GetComponent<NetworkManagerWrapper>() == null)
            networkObject.AddComponent<NetworkManagerWrapper>();
    }

    private static void CreateLobbyServices()
    {
        GameObject services = new("LobbyRemake_Services");
        services.AddComponent<LobbyManager>();
        GameStateMachine stateMachine = services.AddComponent<GameStateMachine>();
        SceneLoader sceneLoader = services.AddComponent<SceneLoader>();

        SerializedObject serializedLoader = new(sceneLoader);
        serializedLoader.FindProperty("_gameStateMachine").objectReferenceValue = stateMachine;
        serializedLoader.ApplyModifiedPropertiesWithoutUndo();

        GameObject interfaceRoot = new("LobbyRemake_Interface");
        interfaceRoot.AddComponent<LobbyAutoController>();
    }

    private static void AddSceneToBuildSettings()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        if (scenes.Any(scene => scene.path == ScenePath)) return;

        EditorBuildSettings.scenes = scenes
            .Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) })
            .ToArray();
    }
}
#endif
