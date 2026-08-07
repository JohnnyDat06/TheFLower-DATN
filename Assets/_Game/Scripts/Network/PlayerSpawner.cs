using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Network
{
    /// <summary>
    /// PlayerSpawner — Quản lý việc spawn player và đồng bộ hóa việc bắt đầu game.
    /// Triển khai Loading Barrier: Đợi cả 2 người chơi nạp xong cảnh mới cho phép di chuyển.
    /// </summary>
    public class PlayerSpawner : NetworkBehaviour
    {
        public static PlayerSpawner Instance { get; private set; }

        [Header("Spawn Settings")]
        [SerializeField] private Transform[] spawnPoints;
        
        [Tooltip("Nếu bật, tất cả Player sẽ được ép về cùng độ cao Y của điểm Spawn đầu tiên.")]
        [SerializeField] private bool forceSameHeight = true;
        
        private HashSet<ulong> _readyPlayers = new HashSet<ulong>();
        private HashSet<ulong> _spawnedPlayers = new HashSet<ulong>();
        private bool _isSpawningFinished = false;

        private const int MaxTeleportAttempts = 30;
        private const float TeleportRetryDelay = 0.1f;
        private const float SpawnPhysicsSettleDelay = 0.1f;

        private void Awake()
        {
            Instance = this;
            var activeNM = NetworkManager.Singleton;
            var isNoAttached = TryGetComponent<Unity.Netcode.NetworkObject>(out var no);
            Debug.Log($"[PlayerSpawner] Awake called. Instance set to {this.gameObject.name}. Active={gameObject.activeInHierarchy}. HasNetworkObject={isNoAttached}");
            if (isNoAttached)
            {
                string path = gameObject.name;
                Transform t = transform.parent;
                while (t != null)
                {
                    path = t.name + "/" + path;
                    t = t.parent;
                }
                Debug.Log($"[PlayerSpawner] NetworkObject diagnostics: Path={path}, IsSceneObject={no.IsSceneObject}, no.NetworkManager={no.NetworkManager}, activeNetworkManager={activeNM}, Scene={gameObject.scene.name} (handle={gameObject.scene.handle})");
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            Debug.Log($"[PlayerSpawner] OnNetworkSpawn called on Server. Connected clients count: {NetworkManager.Singleton.ConnectedClientsList.Count}");
            _readyPlayers.Clear();
            _spawnedPlayers.Clear();
            _isSpawningFinished = false;

            // Nhập những client đã báo cáo sẵn sàng trước đó từ LoadingSyncManager
            if (LoadingSyncManager.Instance != null)
            {
                foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
                {
                    if (LoadingSyncManager.Instance.IsClientReady(id))
                    {
                        Debug.Log($"[PlayerSpawner] Importing early ready client {id}");
                        _readyPlayers.Add(id);
                    }
                }
            }

            Debug.Log($"[PlayerSpawner] OnNetworkSpawn initial ready players count: {_readyPlayers.Count}");

            TryStartSynchronizedSpawn();
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            if (Instance == this) Instance = null;
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Được gọi từ NGOPlayerSync qua ServerRpc khi một client đã nạp xong scene.
        /// </summary>
        public void ReportPlayerReady(ulong clientId)
        {
            if (!IsServer || _isSpawningFinished) return;

            Debug.Log($"[PlayerSpawner] Player {clientId} reported READY.");
            _readyPlayers.Add(clientId);

            TryStartSynchronizedSpawn();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (!IsServer || _isSpawningFinished) return;

            _readyPlayers.Remove(clientId);
            _spawnedPlayers.Remove(clientId);
            Debug.Log($"[PlayerSpawner] Client {clientId} disconnected while loading. Rechecking barrier.");
            TryStartSynchronizedSpawn();
        }

        private void TryStartSynchronizedSpawn()
        {
            if (!IsServer || _isSpawningFinished || NetworkManager == null) return;

            var connectedIds = NetworkManager.ConnectedClientsIds;
            if (connectedIds.Count == 0) return;

            foreach (ulong id in connectedIds)
            {
                if (!_readyPlayers.Contains(id)) return;
            }

            Debug.Log("[PlayerSpawner] Every connected client is ready. Starting synchronized spawn.");
            StartCoroutine(ExecuteSynchronizedSpawn());
        }

        private System.Collections.IEnumerator ExecuteSynchronizedSpawn()
        {
            _isSpawningFinished = true;
            Debug.Log("<color=green>[PlayerSpawner] ALL PLAYERS READY. Executing synchronized teleport...</color>");

            if (!TryResolveSpawnPoints())
            {
                Debug.LogError("[PlayerSpawner] Spawn points are missing or invalid. Players remain frozen and the loading overlay stays visible.");
                _isSpawningFinished = false;
                yield break;
            }

            // 1. Sắp xếp danh sách người chơi để gán spawn point cố định
            var clientIds = new List<ulong>(NetworkManager.ConnectedClientsIds);
            clientIds.Sort();

            ConfigureInitialRespawnPoints(clientIds);

            // 2. Chỉ giải phóng player sau khi mọi PlayerObject đã nhận lệnh teleport.
            bool allPlayersTeleported = false;
            for (int attempt = 1; attempt <= MaxTeleportAttempts && !allPlayersTeleported; attempt++)
            {
                allPlayersTeleported = true;
                for (int i = 0; i < clientIds.Count; i++)
                {
                    ulong id = clientIds[i];
                    if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(id, out var client) || client.PlayerObject == null)
                    {
                        allPlayersTeleported = false;
                        continue;
                    }

                    if (!client.PlayerObject.TryGetComponent<NGOPlayerSync>(out var playerSync))
                    {
                        Debug.LogWarning($"[PlayerSpawner] Player {id} is missing NGOPlayerSync; retrying teleport.");
                        allPlayersTeleported = false;
                        continue;
                    }

                    int spawnIndex = i % spawnPoints.Length;
                    Vector3 spawnPos = spawnPoints[spawnIndex].position;
                    if (forceSameHeight) spawnPos.y = spawnPoints[0].position.y;
                    bool teleportConfirmed = false;
                    yield return playerSync.TeleportAndWaitForOwner(
                        spawnPos,
                        spawnPoints[spawnIndex].rotation,
                        confirmed => teleportConfirmed = confirmed);

                    if (!teleportConfirmed)
                    {
                        Debug.LogWarning($"[PlayerSpawner] Teleport confirmation missing for player {id}; retrying before release.");
                        allPlayersTeleported = false;
                        continue;
                    }

                    _spawnedPlayers.Add(id);
                }

                if (!allPlayersTeleported)
                {
                    if (attempt == MaxTeleportAttempts)
                    {
                        Debug.LogError("[PlayerSpawner] Could not resolve every PlayerObject for teleport. Players remain frozen; refusing to release them at (0,0,0).");
                        _isSpawningFinished = false;
                        yield break;
                    }

                    yield return new WaitForSecondsRealtime(TeleportRetryDelay);
                }
            }

            // Teleport confirmation already proves that each owner applied the
            // pose. Only keep a short physics settle window before input release.
            yield return new WaitForSecondsRealtime(SpawnPhysicsSettleDelay);

            Debug.Log($"[PlayerSpawner] Verified teleport command for {_spawnedPlayers.Count}/{clientIds.Count} players before release.");

            // PlayerObjects persist across NGO scene loads, so OnNetworkSpawn
            // does not reset their health. A boss attempt must always begin
            // from a deterministic full-health state.
            if (SceneManager.GetActiveScene().name == Constants.Scenes.BOSS_FINAL)
            {
                foreach (ulong id in clientIds)
                {
                    if (NetworkManager.Singleton.ConnectedClients.TryGetValue(id, out var client) &&
                        client.PlayerObject != null &&
                        client.PlayerObject.TryGetComponent<PlayerHealth>(out var health))
                    {
                        health.RestoreFullHealth();
                    }
                }

                BossEncounterManager.Instance?.RegisterSpawnedPlayersServer();
            }

            SynchronizePlayersToAliveState(clientIds);

            ReleasePlayersAndLoadingOverlay(clientIds);
        }

        /// <summary>
        /// Player objects persist across scene loads. Always synchronize their
        /// health and owner-local FSM before release: RestoreFullHealth alone does
        /// not notify a client that is still in the Dead state.
        /// </summary>
        private void SynchronizePlayersToAliveState(List<ulong> clientIds)
        {
            foreach (ulong id in clientIds)
            {
                if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(id, out var client)
                    || client.PlayerObject == null
                    || !client.PlayerObject.TryGetComponent<PlayerHealth>(out var health))
                {
                    continue;
                }

                Debug.Log($"[PlayerSpawner] Synchronizing alive state for player {id} before releasing the scene spawn.");
                health.ReviveAtHealthPercent(1f);
            }
        }

        /// <summary>
        /// Seeds respawn locations from the same deterministic assignment used for
        /// the initial teleport. This prevents a death during the loading barrier
        /// from falling back to an old scene position or Vector3.zero.
        /// </summary>
        private void ConfigureInitialRespawnPoints(List<ulong> clientIds)
        {
            if (RespawnManager.Instance == null)
            {
                Debug.LogWarning("[PlayerSpawner] RespawnManager is unavailable; initial respawn points cannot be seeded.");
                return;
            }

            for (int i = 0; i < clientIds.Count; i++)
            {
                int spawnIndex = i % spawnPoints.Length;
                Vector3 spawnPosition = spawnPoints[spawnIndex].position;
                if (forceSameHeight) spawnPosition.y = spawnPoints[0].position.y;

                RespawnManager.Instance.SetInitialSpawnPoint(
                    clientIds[i],
                    spawnPosition);
            }
        }

        private bool TryResolveSpawnPoints()
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                bool valid = true;
                foreach (Transform point in spawnPoints)
                {
                    if (point == null || !point.gameObject.activeInHierarchy || !IsFinite(point.position))
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid) return true;
            }

            Transform first = transform.Find("P1");
            Transform second = transform.Find("P2");
            if (first == null || second == null || !first.gameObject.activeInHierarchy || !second.gameObject.activeInHierarchy)
                return false;

            spawnPoints = new[] { first, second };
            Debug.LogWarning("[PlayerSpawner] Recovered spawn points from child transforms P1/P2.");
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private void ReleasePlayersAndLoadingOverlay(List<ulong> clientIds)
        {
            // 4. Kiểm tra xem có TrailerManager không, nếu có thì kích hoạt Trailer
            if (Game.Core.TrailerManager.Instance != null)
            {
                Game.Core.TrailerManager.Instance.StartTrailerClientRpc();
            }
            else
            {
                // Nếu không có trailer, giải phóng nhân vật ngay
                foreach (var id in clientIds)
                {
                    if (NetworkManager.Singleton.ConnectedClients.TryGetValue(id, out var client) && client.PlayerObject != null)
                    {
                        if (client.PlayerObject.TryGetComponent<NGOPlayerSync>(out var playerSync))
                        {
                            playerSync.ReleaseServerSimulation();
                            playerSync.ReleasePlayerClientRpc();
                        }
                    }
                }
            }

            // 5. Mở màn hình Loading (Fade Out cái overlay đen)
            if (LoadingSyncManager.Instance != null)
            {
                LoadingSyncManager.Instance.EndLoadingFadeClientRpc();
            }
        }
    }
}
