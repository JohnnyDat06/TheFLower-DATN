using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Performance
{
    /// <summary>
    /// Streams only safe static colliders around the players in Map1_Main and
    /// Map2_Main. The active physics area is a 3x3 cell neighborhood while
    /// safe ambient components are kept warm in a 5x5 preload neighborhood.
    /// Terrain, triggers, spawn points, gameplay and network colliders remain enabled.
    ///
    /// This is intentionally separate from visual activation streaming: enabling
    /// a collider is queued and processed in small batches to avoid a PhysX spike.
    /// </summary>
    public sealed class MapGridColliderStreamer : MonoBehaviour
    {
        private const float InitialDelaySeconds = 1.5f;
        private const float PlayerRefreshIntervalSeconds = 0.25f;
        private const float CellRefreshIntervalSeconds = 0.1f;
        private const int ActiveCellRadius = 1;
        private const int PreloadCellRadius = 2;
        private const int DebugGridRadius = 2;
        private const int MaxColliderOperationsPerFrame = 32;
        private const int MaxAmbientOperationsPerFrame = 8;
        private const int MaxCoveredCellsPerRuntimeObject = 64;
        private const KeyCode ToggleKey = KeyCode.F8;
        private const KeyCode DebugToggleKey = KeyCode.BackQuote;

        private static readonly GridSceneConfig[] SceneConfigs =
        {
            // Map1 extents: approximately x[-1478.79, 2166.50], z[-1477.60, 2454.97].
            // A 400m cell keeps the 3x3 physics neighborhood close to the players
            // while the 5x5 ambient preload area hides transition work.
            new GridSceneConfig(
                "Map1_Main",
                new Vector3(-1478.7937f, 0f, -1477.6016f),
                400f),

            // Map2 authored zones are approximately 1365.33m apart. The collider
            // grid is intentionally the same size as Map1 so both maps use the same
            // runtime tuning and multiplayer diagnostics.
            new GridSceneConfig(
                "Map2_Main",
                new Vector3(-1607.6709f, 0f, -418.69617f),
                400f)
        };

        private static bool _bootstrapRegistered;
        private static MapGridColliderStreamer _instance;

        private readonly List<ColliderRuntime> _colliders = new List<ColliderRuntime>();
        private readonly List<AmbientRuntime> _ambientComponents = new List<AmbientRuntime>();
        private readonly List<Vector3> _playerPositions = new List<Vector3>(2);
        private readonly List<GridCell> _currentPlayerCells = new List<GridCell>(2);
        private readonly List<GridCell> _lastLoggedPlayerCells = new List<GridCell>(2);
        private readonly HashSet<GridCell> _desiredCells = new HashSet<GridCell>();
        private readonly HashSet<GridCell> _preloadCells = new HashSet<GridCell>();
        private readonly Queue<ColliderRuntime> _pendingLoads = new Queue<ColliderRuntime>();
        private readonly Queue<ColliderRuntime> _pendingUnloads = new Queue<ColliderRuntime>();
        private readonly HashSet<ColliderRuntime> _queuedLoads = new HashSet<ColliderRuntime>();
        private readonly HashSet<ColliderRuntime> _queuedUnloads = new HashSet<ColliderRuntime>();
        private readonly Queue<AmbientRuntime> _pendingAmbientLoads = new Queue<AmbientRuntime>();
        private readonly Queue<AmbientRuntime> _pendingAmbientUnloads = new Queue<AmbientRuntime>();
        private readonly HashSet<AmbientRuntime> _queuedAmbientLoads = new HashSet<AmbientRuntime>();
        private readonly HashSet<AmbientRuntime> _queuedAmbientUnloads = new HashSet<AmbientRuntime>();

        private GridSceneConfig _sceneConfig;
        private float _nextPlayerRefreshTime;
        private float _nextCellRefreshTime;
        private bool _initialized;
        private bool _streamingEnabled = true;
        private bool _debugVisualizationEnabled;

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

            MapGridColliderStreamer existing = FindFirstObjectByType<MapGridColliderStreamer>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            GameObject streamerObject = new GameObject("[Prototype] Map Grid Collider Streamer");
            SceneManager.MoveGameObjectToScene(streamerObject, scene);
            _instance = streamerObject.AddComponent<MapGridColliderStreamer>();
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

            BuildColliderCatalog();
            BuildAmbientCatalog();
            _initialized = _colliders.Count > 0 || _ambientComponents.Count > 0;
            _nextPlayerRefreshTime = 0f;
            _nextCellRefreshTime = 0f;

            if (!_initialized)
            {
                Debug.LogWarning(
                    $"[MapGridColliderStreamer] No streamable static colliders found in " +
                    $"'{_sceneConfig.SceneName}'. All existing colliders remain unchanged.");
                yield break;
            }

            Debug.Log(
                $"[MapGridColliderStreamer] Ready on {_sceneConfig.SceneName}. " +
                    $"Grid={_sceneConfig.CellSize:0.00}m, ActiveArea=3x3, PreloadArea=5x5, " +
                    $"Colliders={_colliders.Count}, Ambient={_ambientComponents.Count}, " +
                    $"F8 toggles streaming, " +
                    $"~ toggles debug visualization.");

            RefreshPlayerPositions(forceRefresh: true);
            RefreshDesiredCells();
            QueueColliderStateChanges();
            QueueAmbientStateChanges();
        }

        private void Update()
        {
            if (!_initialized) return;

            if (Input.GetKeyDown(ToggleKey))
            {
                SetStreamingEnabled(!_streamingEnabled);
            }

            if (Input.GetKeyDown(DebugToggleKey))
            {
                _debugVisualizationEnabled = !_debugVisualizationEnabled;
                Debug.Log(
                    $"[MapGridColliderStreamer] Debug visualization " +
                    $"{(_debugVisualizationEnabled ? "ENABLED" : "DISABLED")} by ~.");
            }

            bool playerRefreshDue = Time.unscaledTime >= _nextPlayerRefreshTime;
            if (playerRefreshDue)
            {
                RefreshPlayerPositions(forceRefresh: false);
            }

            if (playerRefreshDue || Time.unscaledTime >= _nextCellRefreshTime)
            {
                _nextCellRefreshTime = Time.unscaledTime + CellRefreshIntervalSeconds;
                RefreshDesiredCells();
                QueueColliderStateChanges();
                QueueAmbientStateChanges();
            }

            ProcessPendingStreamChanges();
        }

        private void BuildColliderCatalog()
        {
            _colliders.Clear();

            Collider[] sceneColliders = FindObjectsByType<Collider>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            int alwaysLoadedCount = 0;
            int skippedCount = 0;

            for (int i = 0; i < sceneColliders.Length; i++)
            {
                Collider collider = sceneColliders[i];
                if (collider == null || !collider.enabled)
                {
                    skippedCount++;
                    continue;
                }

                if (!IsSafeStaticCollider(collider))
                {
                    skippedCount++;
                    continue;
                }

                bool alwaysLoaded = IsAlwaysLoadedCollider(collider);
                List<GridCell> cells = alwaysLoaded ? null : ResolveCoveredCells(collider);
                if (!alwaysLoaded && (cells == null || cells.Count == 0))
                {
                    alwaysLoaded = true;
                }

                ColliderRuntime runtime = new ColliderRuntime(
                    collider,
                    cells,
                    alwaysLoaded);
                _colliders.Add(runtime);
                if (alwaysLoaded) alwaysLoadedCount++;
            }

            Debug.Log(
                $"[MapGridColliderStreamer] Catalog {_sceneConfig.SceneName}: " +
                $"managed={_colliders.Count}, alwaysLoaded={alwaysLoadedCount}, skipped={skippedCount}.");
        }

        private void BuildAmbientCatalog()
        {
            _ambientComponents.Clear();

            ParticleSystem[] particleSystems = FindObjectsByType<ParticleSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particleSystem = particleSystems[i];
                if (!IsSafeAmbientComponent(particleSystem)) continue;

                Renderer particleRenderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                if (particleRenderer == null) continue;

                _ambientComponents.Add(AmbientRuntime.ForParticleSystem(
                    particleSystem,
                    particleRenderer,
                    ResolveComponentCells(particleSystem, particleRenderer.bounds)));
            }

            AudioSource[] audioSources = FindObjectsByType<AudioSource>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < audioSources.Length; i++)
            {
                AudioSource audioSource = audioSources[i];
                if (!IsSafeAmbientComponent(audioSource)) continue;

                _ambientComponents.Add(AmbientRuntime.ForBehaviour(
                    audioSource,
                    ResolveComponentCells(audioSource, CreatePointBounds(audioSource.transform.position))));
            }

            Animator[] animators = FindObjectsByType<Animator>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < animators.Length; i++)
            {
                Animator animator = animators[i];
                if (!IsSafeAmbientComponent(animator)) continue;

                Bounds bounds;
                if (!TryGetRendererBounds(animator.transform, out bounds))
                {
                    bounds = CreatePointBounds(animator.transform.position);
                }

                _ambientComponents.Add(AmbientRuntime.ForBehaviour(
                    animator,
                    ResolveComponentCells(animator, bounds)));
            }

            Debug.Log(
                $"[MapGridColliderStreamer] Ambient catalog {_sceneConfig.SceneName}: " +
                $"managed={_ambientComponents.Count}. " +
                "Network/gameplay behaviours are intentionally excluded.");
        }

        private void RefreshPlayerPositions(bool forceRefresh)
        {
            if (!forceRefresh && Time.unscaledTime < _nextPlayerRefreshTime) return;

            _nextPlayerRefreshTime = Time.unscaledTime + PlayerRefreshIntervalSeconds;
            _playerPositions.Clear();

            GameObject[] players = GameObject.FindGameObjectsWithTag(Constants.Tags.PLAYER);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null) _playerPositions.Add(players[i].transform.position);
            }
        }

        private void RefreshDesiredCells()
        {
            _desiredCells.Clear();
            _preloadCells.Clear();
            _currentPlayerCells.Clear();

            // Before NGO has spawned a player, do not disable anything. This also
            // keeps the spawn barrier safe and prevents an incorrect guessed cell.
            if (_playerPositions.Count == 0) return;

            for (int i = 0; i < _playerPositions.Count; i++)
            {
                GridCell centerCell = _sceneConfig.WorldToCell(_playerPositions[i]);
                _currentPlayerCells.Add(centerCell);
                for (int x = -ActiveCellRadius; x <= ActiveCellRadius; x++)
                {
                    for (int z = -ActiveCellRadius; z <= ActiveCellRadius; z++)
                    {
                        _desiredCells.Add(new GridCell(centerCell.X + x, centerCell.Z + z));
                    }
                }

                for (int x = -PreloadCellRadius; x <= PreloadCellRadius; x++)
                {
                    for (int z = -PreloadCellRadius; z <= PreloadCellRadius; z++)
                    {
                        _preloadCells.Add(new GridCell(centerCell.X + x, centerCell.Z + z));
                    }
                }
            }

            if (!ArePlayerCellsEqual(_currentPlayerCells, _lastLoggedPlayerCells))
            {
                _lastLoggedPlayerCells.Clear();
                _lastLoggedPlayerCells.AddRange(_currentPlayerCells);
                Debug.Log(
                    $"[MapGridColliderStreamer] {_sceneConfig.SceneName} player cell changed: " +
                    $"{FormatCells(_currentPlayerCells)}, active={_desiredCells.Count}, " +
                    $"preload={_preloadCells.Count}, " +
                    $"managed={_colliders.Count}.");
            }
        }

        private void QueueColliderStateChanges()
        {
            for (int i = 0; i < _colliders.Count; i++)
            {
                ColliderRuntime runtime = _colliders[i];
                if (runtime.Collider == null || !runtime.OriginalEnabled) continue;

                bool shouldBeEnabled = _playerPositions.Count == 0 ||
                    !_streamingEnabled ||
                    runtime.AlwaysLoaded ||
                    runtime.IsCoveredBy(_desiredCells);

                if (shouldBeEnabled)
                {
                    _queuedUnloads.Remove(runtime);
                    if (!runtime.Collider.enabled)
                    {
                        if (_queuedLoads.Add(runtime)) _pendingLoads.Enqueue(runtime);
                    }
                }
                else
                {
                    _queuedLoads.Remove(runtime);
                    if (runtime.Collider.enabled)
                    {
                        if (_queuedUnloads.Add(runtime)) _pendingUnloads.Enqueue(runtime);
                    }
                }
            }
        }

        private void QueueAmbientStateChanges()
        {
            for (int i = 0; i < _ambientComponents.Count; i++)
            {
                AmbientRuntime runtime = _ambientComponents[i];
                if (runtime.Component == null || !runtime.OriginalEnabled) continue;

                bool shouldBeEnabled = _playerPositions.Count == 0 ||
                    !_streamingEnabled ||
                    runtime.AlwaysLoaded ||
                    runtime.IsCoveredBy(_preloadCells);

                if (shouldBeEnabled)
                {
                    _queuedAmbientUnloads.Remove(runtime);
                    if (!runtime.IsEnabled)
                    {
                        if (_queuedAmbientLoads.Add(runtime)) _pendingAmbientLoads.Enqueue(runtime);
                    }
                }
                else
                {
                    _queuedAmbientLoads.Remove(runtime);
                    if (runtime.IsEnabled)
                    {
                        if (_queuedAmbientUnloads.Add(runtime)) _pendingAmbientUnloads.Enqueue(runtime);
                    }
                }
            }
        }

        private void ProcessPendingStreamChanges()
        {
            int colliderOperations = 0;

            // Load first so the player never crosses into an already-unloaded cell
            // before its replacement colliders have been inserted into PhysX.
            while (colliderOperations < MaxColliderOperationsPerFrame && _pendingLoads.Count > 0)
            {
                ColliderRuntime runtime = _pendingLoads.Dequeue();
                if (!_queuedLoads.Remove(runtime)) continue;
                if (runtime.Collider == null || !runtime.OriginalEnabled) continue;

                if (ShouldBeEnabled(runtime) && !runtime.Collider.enabled)
                {
                    runtime.Collider.enabled = true;
                    colliderOperations++;
                }
            }

            while (colliderOperations < MaxColliderOperationsPerFrame && _pendingUnloads.Count > 0)
            {
                ColliderRuntime runtime = _pendingUnloads.Dequeue();
                if (!_queuedUnloads.Remove(runtime)) continue;
                if (runtime.Collider == null || !runtime.OriginalEnabled) continue;

                if (!ShouldBeEnabled(runtime) && runtime.Collider.enabled)
                {
                    runtime.Collider.enabled = false;
                    colliderOperations++;
                }
            }

            int ambientOperations = 0;
            while (ambientOperations < MaxAmbientOperationsPerFrame && _pendingAmbientLoads.Count > 0)
            {
                AmbientRuntime runtime = _pendingAmbientLoads.Dequeue();
                if (!_queuedAmbientLoads.Remove(runtime)) continue;
                if (runtime.Component == null || !runtime.OriginalEnabled) continue;

                if (ShouldBeEnabled(runtime) && !runtime.IsEnabled)
                {
                    runtime.SetEnabled(true);
                    ambientOperations++;
                }
            }

            while (ambientOperations < MaxAmbientOperationsPerFrame && _pendingAmbientUnloads.Count > 0)
            {
                AmbientRuntime runtime = _pendingAmbientUnloads.Dequeue();
                if (!_queuedAmbientUnloads.Remove(runtime)) continue;
                if (runtime.Component == null || !runtime.OriginalEnabled) continue;

                if (!ShouldBeEnabled(runtime) && runtime.IsEnabled)
                {
                    runtime.SetEnabled(false);
                    ambientOperations++;
                }
            }
        }

        private void OnGUI()
        {
            if (!_initialized) return;

            if (!_debugVisualizationEnabled)
            {
                return;
            }

            int enabledCount = 0;
            int disabledCount = 0;
            for (int i = 0; i < _colliders.Count; i++)
            {
                Collider collider = _colliders[i].Collider;
                if (collider == null) continue;
                if (collider.enabled) enabledCount++;
                else disabledCount++;
            }

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.Box(new Rect(12f, 12f, 500f, 190f), GUIContent.none);
            GUI.color = Color.white;

            string playerCells = _currentPlayerCells.Count == 0
                ? "waiting for PlayerObject"
                : FormatCells(_currentPlayerCells);
            GUI.Label(new Rect(24f, 22f, 420f, 22f), $"Collider Chunk Debug — {_sceneConfig.SceneName}");
            GUI.Label(new Rect(24f, 44f, 420f, 22f), $"Player cell: {playerCells}");
            GUI.Label(new Rect(24f, 66f, 460f, 22f), $"Active cells: {_desiredCells.Count} / 9 | Preload: {_preloadCells.Count} / 25");
            GUI.Label(new Rect(24f, 88f, 460f, 22f), $"Collider enabled: {enabledCount} | disabled: {disabledCount}");
            GUI.Label(new Rect(24f, 110f, 460f, 22f), $"Ambient components: {_ambientComponents.Count} | active: {CountEnabledAmbientComponents()}");
            GUI.Label(new Rect(24f, 132f, 460f, 22f), $"Streaming: {(_streamingEnabled ? "ON" : "OFF")} | F8 | ~ hide/show debug");
            GUI.color = previousColor;
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !_initialized || !_debugVisualizationEnabled || _sceneConfig == null)
            {
                return;
            }

            for (int playerIndex = 0; playerIndex < _currentPlayerCells.Count; playerIndex++)
            {
                GridCell playerCell = _currentPlayerCells[playerIndex];
                float gizmoY = _playerPositions.Count > playerIndex
                    ? _playerPositions[playerIndex].y + 1f
                    : _sceneConfig.Origin.y + 1f;

                for (int x = -DebugGridRadius; x <= DebugGridRadius; x++)
                {
                    for (int z = -DebugGridRadius; z <= DebugGridRadius; z++)
                    {
                        GridCell cell = new GridCell(playerCell.X + x, playerCell.Z + z);
                        GetCellColliderState(cell, out int totalColliders, out int enabledColliders);

                        bool isDesired = _desiredCells.Contains(cell);
                        bool isPreloaded = _preloadCells.Contains(cell);
                        bool isCenter = cell.Equals(playerCell);
                        Color color;

                        if (isCenter)
                        {
                            color = new Color(1f, 0.85f, 0.05f, 0.95f);
                        }
                        else if (isDesired && totalColliders == 0)
                        {
                            color = new Color(0.3f, 0.8f, 1f, 0.55f);
                        }
                        else if (isDesired && enabledColliders == totalColliders)
                        {
                            color = new Color(0.15f, 1f, 0.2f, 0.85f);
                        }
                        else if (isPreloaded && totalColliders == 0)
                        {
                            color = new Color(0.2f, 0.65f, 1f, 0.55f);
                        }
                        else if (totalColliders > 0 && enabledColliders == 0)
                        {
                            color = new Color(1f, 0.15f, 0.15f, 0.8f);
                        }
                        else
                        {
                            color = new Color(1f, 0.55f, 0.05f, 0.75f);
                        }

                        Vector3 center = _sceneConfig.CellToWorldCenter(cell);
                        center.y = gizmoY;
                        Gizmos.color = color;
                        Gizmos.DrawWireCube(
                            center,
                            new Vector3(_sceneConfig.CellSize, 2f, _sceneConfig.CellSize));
                    }
                }
            }
        }

        private void GetCellColliderState(GridCell cell, out int totalColliders, out int enabledColliders)
        {
            totalColliders = 0;
            enabledColliders = 0;

            for (int i = 0; i < _colliders.Count; i++)
            {
                ColliderRuntime runtime = _colliders[i];
                if (runtime.AlwaysLoaded || runtime.Cells == null) continue;

                for (int cellIndex = 0; cellIndex < runtime.Cells.Count; cellIndex++)
                {
                    if (!runtime.Cells[cellIndex].Equals(cell)) continue;

                    totalColliders++;
                    if (runtime.Collider != null && runtime.Collider.enabled) enabledColliders++;
                    break;
                }
            }
        }

        private static bool ArePlayerCellsEqual(List<GridCell> first, List<GridCell> second)
        {
            if (first.Count != second.Count) return false;

            for (int i = 0; i < first.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < second.Count; j++)
                {
                    if (!first[i].Equals(second[j])) continue;
                    found = true;
                    break;
                }

                if (!found) return false;
            }

            return true;
        }

        private static string FormatCells(List<GridCell> cells)
        {
            if (cells.Count == 0) return "none";

            string result = string.Empty;
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0) result += ", ";
                result += cells[i].ToString();
            }

            return result;
        }

        private bool ShouldBeEnabled(ColliderRuntime runtime)
        {
            return _playerPositions.Count == 0 ||
                !_streamingEnabled ||
                runtime.AlwaysLoaded ||
                runtime.IsCoveredBy(_desiredCells);
        }

        private bool ShouldBeEnabled(AmbientRuntime runtime)
        {
            return _playerPositions.Count == 0 ||
                !_streamingEnabled ||
                runtime.AlwaysLoaded ||
                runtime.IsCoveredBy(_preloadCells);
        }

        private int CountEnabledAmbientComponents()
        {
            int count = 0;
            for (int i = 0; i < _ambientComponents.Count; i++)
            {
                if (_ambientComponents[i].IsEnabled) count++;
            }

            return count;
        }

        private void SetStreamingEnabled(bool enabledState)
        {
            _streamingEnabled = enabledState;
            _pendingLoads.Clear();
            _pendingUnloads.Clear();
            _queuedLoads.Clear();
            _queuedUnloads.Clear();
            _pendingAmbientLoads.Clear();
            _pendingAmbientUnloads.Clear();
            _queuedAmbientLoads.Clear();
            _queuedAmbientUnloads.Clear();

            Debug.Log(
                $"[MapGridColliderStreamer] Collider streaming " +
                $"{(enabledState ? "ENABLED" : "DISABLED")} by F8 on {_sceneConfig.SceneName}.");

            QueueColliderStateChanges();
            QueueAmbientStateChanges();
        }

        private List<GridCell> ResolveComponentCells(Component component, Bounds bounds)
        {
            if (IsAlwaysLoadedComponent(component.transform)) return null;
            return ResolveCoveredCells(bounds);
        }

        private static Bounds CreatePointBounds(Vector3 position)
        {
            return new Bounds(position, Vector3.one);
        }

        private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
        {
            Renderer renderer = root.GetComponentInChildren<Renderer>(true);
            if (renderer != null)
            {
                bounds = renderer.bounds;
                return true;
            }

            bounds = default;
            return false;
        }

        private static bool IsSafeAmbientComponent(Component component)
        {
            if (component == null || !component.gameObject.scene.IsValid()) return false;
            if (component.GetComponentInParent<NetworkObject>(true) != null) return false;
            if (component.GetComponentInParent<MonoBehaviour>(true) != null) return false;
            return true;
        }

        private static bool IsAlwaysLoadedComponent(Transform transform)
        {
            Transform current = transform;
            while (current != null)
            {
                string objectName = current.name.ToLowerInvariant();
                if (objectName.Contains("spawn") ||
                    objectName.Contains("checkpoint") ||
                    objectName.Contains("check point") ||
                    objectName.Contains("portal") ||
                    objectName.Contains("tele_") ||
                    objectName.Contains("tele ") ||
                    objectName.Contains("death") ||
                    objectName.Contains("boundary") ||
                    objectName.Contains("boss") ||
                    objectName.Contains("music") ||
                    objectName.Contains("audio manager"))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private List<GridCell> ResolveCoveredCells(Collider collider)
        {
            return ResolveCoveredCells(collider.bounds);
        }

        private List<GridCell> ResolveCoveredCells(Bounds bounds)
        {
            int minX = _sceneConfig.WorldToCellIndexX(bounds.min.x);
            int maxX = _sceneConfig.WorldToCellIndexX(bounds.max.x);
            int minZ = _sceneConfig.WorldToCellIndexZ(bounds.min.z);
            int maxZ = _sceneConfig.WorldToCellIndexZ(bounds.max.z);

            int width = maxX - minX + 1;
            int depth = maxZ - minZ + 1;
            if (width <= 0 || depth <= 0 || width * depth > MaxCoveredCellsPerRuntimeObject) return null;

            List<GridCell> cells = new List<GridCell>(width * depth);
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    cells.Add(new GridCell(x, z));
                }
            }

            return cells;
        }

        private static bool IsSafeStaticCollider(Collider collider)
        {
            if (collider.isTrigger) return false;
            if (collider is TerrainCollider) return false;
            if (collider.GetComponentInParent<Rigidbody>(true) != null) return false;
            if (collider.GetComponentInParent<NetworkObject>(true) != null) return false;
            if (collider.GetComponentInParent<Behaviour>(true) != null) return false;
            return true;
        }

        private static bool IsAlwaysLoadedCollider(Collider collider)
        {
            Transform current = collider.transform;
            while (current != null)
            {
                string objectName = current.name.ToLowerInvariant();
                if (objectName.Contains("spawn") ||
                    objectName.Contains("checkpoint") ||
                    objectName.Contains("check point") ||
                    objectName.Contains("portal") ||
                    objectName.Contains("tele_") ||
                    objectName.Contains("tele ") ||
                    objectName.Contains("death") ||
                    objectName.Contains("boundary") ||
                    objectName.Contains("boss"))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static GridSceneConfig FindSceneConfig(string sceneName)
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

        private void OnDestroy()
        {
            for (int i = 0; i < _colliders.Count; i++)
            {
                ColliderRuntime runtime = _colliders[i];
                if (runtime.Collider != null) runtime.Collider.enabled = runtime.OriginalEnabled;
            }

            for (int i = 0; i < _ambientComponents.Count; i++)
            {
                AmbientRuntime runtime = _ambientComponents[i];
                if (runtime.Component != null) runtime.Restore();
            }

            if (_instance == this) _instance = null;
        }

        private sealed class GridSceneConfig
        {
            public readonly string SceneName;
            public readonly Vector3 Origin;
            public readonly float CellSize;

            public GridSceneConfig(string sceneName, Vector3 origin, float cellSize)
            {
                SceneName = sceneName;
                Origin = origin;
                CellSize = cellSize;
            }

            public GridCell WorldToCell(Vector3 worldPosition)
            {
                return new GridCell(WorldToCellIndexX(worldPosition.x), WorldToCellIndexZ(worldPosition.z));
            }

            public int WorldToCellIndexX(float worldX)
            {
                return Mathf.FloorToInt((worldX - Origin.x) / CellSize);
            }

            public int WorldToCellIndexZ(float worldZ)
            {
                return Mathf.FloorToInt((worldZ - Origin.z) / CellSize);
            }

            public Vector3 CellToWorldCenter(GridCell cell)
            {
                return new Vector3(
                    Origin.x + ((cell.X + 0.5f) * CellSize),
                    Origin.y,
                    Origin.z + ((cell.Z + 0.5f) * CellSize));
            }
        }

        private sealed class ColliderRuntime
        {
            public readonly Collider Collider;
            public readonly List<GridCell> Cells;
            public readonly bool AlwaysLoaded;
            public readonly bool OriginalEnabled;

            public ColliderRuntime(Collider collider, List<GridCell> cells, bool alwaysLoaded)
            {
                Collider = collider;
                Cells = cells;
                AlwaysLoaded = alwaysLoaded;
                OriginalEnabled = collider.enabled;
            }

            public bool IsCoveredBy(HashSet<GridCell> desiredCells)
            {
                if (AlwaysLoaded || Cells == null) return true;

                for (int i = 0; i < Cells.Count; i++)
                {
                    if (desiredCells.Contains(Cells[i])) return true;
                }

                return false;
            }
        }

        private sealed class AmbientRuntime
        {
            private readonly Behaviour _behaviour;
            private readonly ParticleSystem _particleSystem;
            private readonly Renderer _particleRenderer;
            private readonly bool _originalPlaying;

            public readonly Component Component;
            public readonly List<GridCell> Cells;
            public readonly bool AlwaysLoaded;
            public readonly bool OriginalEnabled;

            public bool IsEnabled
            {
                get
                {
                    if (_particleRenderer != null) return _particleRenderer.enabled;
                    return _behaviour != null && _behaviour.enabled;
                }
            }

            private AmbientRuntime(
                Component component,
                Behaviour behaviour,
                ParticleSystem particleSystem,
                Renderer particleRenderer,
                List<GridCell> cells)
            {
                Component = component;
                _behaviour = behaviour;
                _particleSystem = particleSystem;
                _particleRenderer = particleRenderer;
                Cells = cells;
                AlwaysLoaded = cells == null;
                OriginalEnabled = IsEnabled;
                _originalPlaying = particleSystem != null
                    ? particleSystem.isPlaying
                    : behaviour is AudioSource audioSource && audioSource.isPlaying;
            }

            public static AmbientRuntime ForBehaviour(Behaviour behaviour, List<GridCell> cells)
            {
                return new AmbientRuntime(behaviour, behaviour, null, null, cells);
            }

            public static AmbientRuntime ForParticleSystem(
                ParticleSystem particleSystem,
                Renderer particleRenderer,
                List<GridCell> cells)
            {
                return new AmbientRuntime(
                    particleSystem,
                    null,
                    particleSystem,
                    particleRenderer,
                    cells);
            }

            public void SetEnabled(bool enabled)
            {
                if (_particleRenderer != null)
                {
                    _particleRenderer.enabled = enabled;
                    if (_particleSystem != null)
                    {
                        if (enabled)
                        {
                            if (_originalPlaying && !_particleSystem.isPlaying) _particleSystem.Play(true);
                        }
                        else
                        {
                            _particleSystem.Pause(true);
                        }
                    }

                    return;
                }

                if (_behaviour == null) return;
                _behaviour.enabled = enabled;
                if (enabled && _behaviour is AudioSource audioSource && _originalPlaying && !audioSource.isPlaying)
                {
                    audioSource.Play();
                }
            }

            public bool IsCoveredBy(HashSet<GridCell> preloadCells)
            {
                if (AlwaysLoaded || Cells == null) return true;

                for (int i = 0; i < Cells.Count; i++)
                {
                    if (preloadCells.Contains(Cells[i])) return true;
                }

                return false;
            }

            public void Restore()
            {
                if (_particleRenderer != null)
                {
                    _particleRenderer.enabled = OriginalEnabled;
                    if (_particleSystem != null)
                    {
                        if (_originalPlaying && !_particleSystem.isPlaying) _particleSystem.Play(true);
                        if (!_originalPlaying && _particleSystem.isPlaying) _particleSystem.Pause(true);
                    }

                    return;
                }

                if (_behaviour == null) return;
                _behaviour.enabled = OriginalEnabled;
                if (_behaviour is AudioSource audioSource)
                {
                    if (_originalPlaying && !audioSource.isPlaying) audioSource.Play();
                    if (!_originalPlaying && audioSource.isPlaying) audioSource.Stop();
                }
            }
        }

        private struct GridCell : IEquatable<GridCell>
        {
            public readonly int X;
            public readonly int Z;

            public GridCell(int x, int z)
            {
                X = x;
                Z = z;
            }

            public bool Equals(GridCell other)
            {
                return X == other.X && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is GridCell other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (X * 397) ^ Z;
            }

            public override string ToString()
            {
                return $"({X},{Z})";
            }
        }
    }
}
