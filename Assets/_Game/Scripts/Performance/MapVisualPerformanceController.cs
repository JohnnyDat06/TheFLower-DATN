using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Performance
{
    /// <summary>
    /// Applies a reversible runtime quality profile to the large playable maps.
    /// Existing baked occlusion data and SRP batching remain unchanged.
    /// </summary>
    public sealed class MapVisualPerformanceController : MonoBehaviour
    {
        private const float MapShadowDistance = 35f;
        private const float MapLodBias = 1.5f;
        private const float MapTerrainDetailDistance = 60f;
        private const float MapTerrainTreeDistance = 3000f;
        private const float MapTerrainPixelError = 2f;

        private static bool _bootstrapRegistered;
        private static MapVisualPerformanceController _instance;

        private bool _profileApplied;
        private float _originalShadowDistance;
        private float _originalLodBias;
        private float _originalTerrainDetailDistance;
        private float _originalTerrainTreeDistance;
        private float _originalTerrainPixelError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _bootstrapRegistered = false;
            _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterSceneHook()
        {
            if (_bootstrapRegistered) return;

            _bootstrapRegistered = true;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode loadMode)
        {
            if (!scene.IsValid() || !IsTargetScene(scene.name)) return;
            if (_instance != null && _instance.gameObject.scene == scene) return;

            MapVisualPerformanceController[] existingControllers =
                FindObjectsByType<MapVisualPerformanceController>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            for (int i = 0; i < existingControllers.Length; i++)
            {
                if (existingControllers[i].gameObject.scene != scene) continue;

                _instance = existingControllers[i];
                return;
            }

            GameObject controllerObject = new GameObject("[Performance] Map Visual Profile");
            SceneManager.MoveGameObjectToScene(controllerObject, scene);
            _instance = controllerObject.AddComponent<MapVisualPerformanceController>();
        }

        private void Awake()
        {
            if (!IsTargetScene(gameObject.scene.name))
            {
                enabled = false;
                return;
            }

            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        private void Start()
        {
            ApplyProfile();
        }

        private void ApplyProfile()
        {
            if (_profileApplied) return;

            _originalShadowDistance = QualitySettings.shadowDistance;
            _originalLodBias = QualitySettings.lodBias;
            _originalTerrainDetailDistance = QualitySettings.terrainDetailDistance;
            _originalTerrainTreeDistance = QualitySettings.terrainTreeDistance;
            _originalTerrainPixelError = QualitySettings.terrainPixelError;

            QualitySettings.shadowDistance = Mathf.Min(_originalShadowDistance, MapShadowDistance);
            QualitySettings.lodBias = Mathf.Min(_originalLodBias, MapLodBias);
            QualitySettings.terrainDetailDistance = Mathf.Min(
                _originalTerrainDetailDistance,
                MapTerrainDetailDistance);
            QualitySettings.terrainTreeDistance = Mathf.Min(
                _originalTerrainTreeDistance,
                MapTerrainTreeDistance);
            QualitySettings.terrainPixelError = Mathf.Max(
                _originalTerrainPixelError,
                MapTerrainPixelError);

            _profileApplied = true;

            Debug.Log(
                $"[MapVisualPerformanceController] Applied reversible profile on " +
                $"{gameObject.scene.name}: Shadow={QualitySettings.shadowDistance:0}, " +
                $"LOD={QualitySettings.lodBias:0.00}, " +
                $"TerrainDetail={QualitySettings.terrainDetailDistance:0}, " +
                $"TerrainTrees={QualitySettings.terrainTreeDistance:0}.");
        }

        private void OnDestroy()
        {
            RestoreProfile();
            if (_instance == this) _instance = null;
        }

        private void RestoreProfile()
        {
            if (!_profileApplied) return;

            QualitySettings.shadowDistance = _originalShadowDistance;
            QualitySettings.lodBias = _originalLodBias;
            QualitySettings.terrainDetailDistance = _originalTerrainDetailDistance;
            QualitySettings.terrainTreeDistance = _originalTerrainTreeDistance;
            QualitySettings.terrainPixelError = _originalTerrainPixelError;
            _profileApplied = false;
        }

        private static bool IsTargetScene(string sceneName)
        {
            return string.Equals(sceneName, "Map1_Main", StringComparison.Ordinal) ||
                string.Equals(sceneName, "Map2_Main", StringComparison.Ordinal);
        }
    }
}
