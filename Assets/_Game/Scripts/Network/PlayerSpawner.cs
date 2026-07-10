using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

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

            // Kiểm tra xem đã đủ tất cả người chơi trong session hiện tại chưa
            if (_readyPlayers.Count >= NetworkManager.Singleton.ConnectedClientsList.Count)
            {
                Debug.Log("[PlayerSpawner] All players ready during OnNetworkSpawn, launching ExecuteSynchronizedSpawn.");
                StartCoroutine(ExecuteSynchronizedSpawn());
            }
        }

        /// <summary>
        /// Được gọi từ NGOPlayerSync qua ServerRpc khi một client đã nạp xong scene.
        /// </summary>
        public void ReportPlayerReady(ulong clientId)
        {
            if (!IsServer || _isSpawningFinished) return;

            Debug.Log($"[PlayerSpawner] Player {clientId} reported READY.");
            _readyPlayers.Add(clientId);

            // Kiểm tra xem đã đủ tất cả người chơi trong session hiện tại chưa
            if (_readyPlayers.Count >= NetworkManager.Singleton.ConnectedClientsList.Count)
            {
                StartCoroutine(ExecuteSynchronizedSpawn());
            }
        }

        private System.Collections.IEnumerator ExecuteSynchronizedSpawn()
        {
            _isSpawningFinished = true;
            Debug.Log("<color=green>[PlayerSpawner] ALL PLAYERS READY. Executing synchronized teleport...</color>");

            // 1. Sắp xếp danh sách người chơi để gán spawn point cố định
            var clientIds = new List<ulong>(_readyPlayers);
            clientIds.Sort();

            // 2. Thực hiện Teleport cho từng người
            for (int i = 0; i < clientIds.Count; i++)
            {
                ulong id = clientIds[i];
                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(id, out var client) && client.PlayerObject != null)
                {
                    if (client.PlayerObject.TryGetComponent<NGOPlayerSync>(out var playerSync))
                    {
                        int spawnIndex = i % spawnPoints.Length;
                        Vector3 spawnPos = spawnPoints[spawnIndex].position;
                        
                        if (forceSameHeight && spawnPoints.Length > 0)
                        {
                            spawnPos.y = spawnPoints[0].position.y;
                        }

                        playerSync.Teleport(spawnPos, spawnPoints[spawnIndex].rotation);
                    }
                }
            }

            // 3. Đợi vài frame để đảm bảo lệnh Teleport đã tới Client và Physics đã ổn định
            yield return new WaitForSeconds(0.5f);

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
