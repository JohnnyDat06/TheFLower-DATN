using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Performance
{
    /// <summary>
    /// Phase-1 streaming prototype for the existing static environment groups in the
    /// playable scenes. This intentionally uses activation streaming so it can be
    /// tested without splitting the network scene into additive scenes first.
    /// Terrain, gameplay roots, network objects and runtime behaviours are never
    /// disabled by this prototype.
    /// </summary>
    public sealed class Map2ZoneChunkStreamer : MonoBehaviour
    {
        private const float InitialDelaySeconds = 1.5f;
        private const float RefreshIntervalSeconds = 0.5f;
        private const float PlayerSearchIntervalSeconds = 1f;
        private const KeyCode ToggleKey = KeyCode.F8;

        private static readonly SceneStreamingConfig[] SceneConfigs =
        {
            // Map4 is authored as seven flight zones. Flight systems and
            // checkpoints do not use these name prefixes.
            new SceneStreamingConfig(
                "Map4_Flying",
                1100f,
                1500f,
                "01_ZONE__",
                "02_ZONE__",
                "03_ZONE__",
                "04_ZONE__",
                "05_ZONE__",
                "06_ZONE__",
                "07_ZONE__"),

        };

        private static bool _bootstrapRegistered;
        private static Map2ZoneChunkStreamer _instance;

        private SceneStreamingConfig _sceneConfig;
        private readonly List<ChunkRuntime> _chunks = new List<ChunkRuntime>();
        private readonly List<Vector3> _playerPositions = new List<Vector3>(2);
        private float _nextRefreshTime;
        private float _nextPlayerSearchTime;
        private bool _initialized;
        private bool _streamingEnabled = true;

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
            if (!scene.IsValid() || FindSceneConfig(scene.name) == null) return;

            if (_instance != null && _instance.gameObject.scene == scene) return;

            Map2ZoneChunkStreamer existing = FindFirstObjectByType<Map2ZoneChunkStreamer>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            GameObject streamerObject = new GameObject("[Prototype] Map Scene Chunk Streamer");
            SceneManager.MoveGameObjectToScene(streamerObject, scene);
            _instance = streamerObject.AddComponent<Map2ZoneChunkStreamer>();
        }

        private void Awake()
        {
            _sceneConfig = FindSceneConfig(gameObject.scene.name);
            if (_sceneConfig == null)
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
            StartCoroutine(InitializeAfterSceneStartup());
        }

        private IEnumerator InitializeAfterSceneStartup()
        {
            yield return new WaitForSecondsRealtime(InitialDelaySeconds);

            ResolveStreamableChunks();
            _initialized = _chunks.Count > 0;
            _nextPlayerSearchTime = 0f;
            _nextRefreshTime = 0f;

            if (!_initialized)
            {
                Debug.LogWarning(
                    $"[Map2ZoneChunkStreamer] No safe streamable groups were found in " +
                    $"'{_sceneConfig.SceneName}'. Prototype remains idle.");
                yield break;
            }

            Debug.Log(
                $"[Map2ZoneChunkStreamer] Prototype ready on {_sceneConfig.SceneName}. " +
                $"Load={_sceneConfig.LoadDistance:0}m, Unload={_sceneConfig.UnloadDistance:0}m, " +
                $"Chunks={_chunks.Count}. F8 toggles streaming.");

            RefreshChunkStates(forceRefresh: true);
        }

        private void Update()
        {
            if (!_initialized) return;

            if (Input.GetKeyDown(ToggleKey))
            {
                SetStreamingEnabled(!_streamingEnabled);
            }

            if (Time.unscaledTime < _nextRefreshTime) return;

            _nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
            RefreshChunkStates(forceRefresh: false);
        }

        private void ResolveStreamableChunks()
        {
            _chunks.Clear();
            HashSet<GameObject> uniqueRoots = new HashSet<GameObject>();

            for (int i = 0; i < _sceneConfig.StreamableNamePrefixes.Length; i++)
            {
                List<GameObject> matches = FindSceneObjectsByNamePrefix(
                    gameObject.scene,
                    _sceneConfig.StreamableNamePrefixes[i]);
                if (matches.Count == 0)
                {
                    Debug.LogWarning(
                        $"[Map2ZoneChunkStreamer] Missing expected stream group prefix " +
                        $"'{_sceneConfig.StreamableNamePrefixes[i]}' in '{_sceneConfig.SceneName}'.");
                    continue;
                }

                for (int j = 0; j < matches.Count; j++)
                {
                    GameObject chunkObject = matches[j];
                    if (!uniqueRoots.Add(chunkObject)) continue;

                    if (HasUnsafeRuntimeComponent(chunkObject, out string reason))
                    {
                        Debug.LogWarning(
                            $"[Map2ZoneChunkStreamer] Keeping '{chunkObject.name}' always active because it contains {reason}.");
                        continue;
                    }

                    _chunks.Add(new ChunkRuntime(chunkObject));
                }
            }
        }

        private void RefreshChunkStates(bool forceRefresh)
        {
            RefreshPlayerPositions();

            // Until a PlayerObject exists, leave every environment group loaded.
            // This protects NGO's spawn/loading barrier and avoids guessing a spawn point.
            if (_playerPositions.Count == 0) return;

            for (int i = 0; i < _chunks.Count; i++)
            {
                ChunkRuntime chunk = _chunks[i];
                bool shouldBeActive = !_streamingEnabled || ShouldKeepLoaded(chunk.Root.transform);

                if (forceRefresh || chunk.Root.activeSelf != shouldBeActive)
                {
                    chunk.Root.SetActive(shouldBeActive);
                    Debug.Log(
                        $"[Map2ZoneChunkStreamer] {(shouldBeActive ? "Loaded" : "Unloaded")} " +
                        $"'{chunk.Root.name}' at {chunk.Root.transform.position}. " +
                        $"NearestPlayerDistance={NearestPlayerDistance(chunk.Root.transform):0}m.");
                }
            }
        }

        private void RefreshPlayerPositions()
        {
            if (Time.unscaledTime < _nextPlayerSearchTime) return;

            _nextPlayerSearchTime = Time.unscaledTime + PlayerSearchIntervalSeconds;
            _playerPositions.Clear();

            GameObject[] players = GameObject.FindGameObjectsWithTag(Constants.Tags.PLAYER);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null) _playerPositions.Add(players[i].transform.position);
            }
        }

        private bool ShouldKeepLoaded(Transform chunkTransform)
        {
            float distance = NearestPlayerDistance(chunkTransform);
            return chunkTransform.gameObject.activeSelf
                ? distance <= _sceneConfig.UnloadDistance
                : distance <= _sceneConfig.LoadDistance;
        }

        private float NearestPlayerDistance(Transform chunkTransform)
        {
            float nearestDistance = float.MaxValue;
            Vector3 chunkPosition = chunkTransform.position;

            for (int i = 0; i < _playerPositions.Count; i++)
            {
                Vector3 offset = _playerPositions[i] - chunkPosition;
                offset.y = 0f;
                nearestDistance = Mathf.Min(nearestDistance, offset.magnitude);
            }

            return nearestDistance;
        }

        private void SetStreamingEnabled(bool enabledState)
        {
            _streamingEnabled = enabledState;
            Debug.Log($"[Map2ZoneChunkStreamer] Streaming {(enabledState ? "ENABLED" : "DISABLED")} by F8.");

            if (!enabledState)
            {
                for (int i = 0; i < _chunks.Count; i++)
                {
                    if (!_chunks[i].Root.activeSelf) _chunks[i].Root.SetActive(true);
                }
            }
            else
            {
                RefreshChunkStates(forceRefresh: true);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private static List<GameObject> FindSceneObjectsByNamePrefix(Scene scene, string namePrefix)
        {
            List<GameObject> matches = new List<GameObject>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(roots[i]);
                }

                Transform[] descendants = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < descendants.Length; j++)
                {
                    if (descendants[j].name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(descendants[j].gameObject);
                    }
                }
            }

            return matches;
        }

        private static SceneStreamingConfig FindSceneConfig(string sceneName)
        {
            for (int i = 0; i < SceneConfigs.Length; i++)
            {
                if (string.Equals(SceneConfigs[i].SceneName, sceneName, StringComparison.Ordinal))
                {
                    return SceneConfigs[i];
                }
            }

            return null;
        }

        private static bool HasUnsafeRuntimeComponent(GameObject root, out string reason)
        {
            NetworkObject[] networkObjects = root.GetComponentsInChildren<NetworkObject>(true);
            if (networkObjects.Length > 0)
            {
                reason = "a NetworkObject";
                return true;
            }

            MonoBehaviour[] customBehaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            if (customBehaviours.Length > 0)
            {
                reason = $"the custom behaviour '{customBehaviours[0].GetType().Name}'";
                return true;
            }

            Behaviour[] runtimeBehaviours = root.GetComponentsInChildren<Behaviour>(true);
            if (runtimeBehaviours.Length > 0)
            {
                reason = $"the runtime behaviour '{runtimeBehaviours[0].GetType().Name}'";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        private sealed class SceneStreamingConfig
        {
            public readonly string SceneName;
            public readonly float LoadDistance;
            public readonly float UnloadDistance;
            public readonly string[] StreamableNamePrefixes;

            public SceneStreamingConfig(
                string sceneName,
                float loadDistance,
                float unloadDistance,
                params string[] streamableNamePrefixes)
            {
                SceneName = sceneName;
                LoadDistance = loadDistance;
                UnloadDistance = unloadDistance;
                StreamableNamePrefixes = streamableNamePrefixes;
            }
        }

        private sealed class ChunkRuntime
        {
            public readonly GameObject Root;

            public ChunkRuntime(GameObject root)
            {
                Root = root;
            }
        }
    }
}
