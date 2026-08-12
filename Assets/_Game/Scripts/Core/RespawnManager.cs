using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// RespawnManager — Xử lý hồi sinh Player khi nhân vật chết và lưu điểm Checkpoint.
/// Luôn luôn lắng nghe EventBus trên Singleton (để trên Scene Game).
/// Đã được cập nhật để kế thừa NetworkBehaviour và sử dụng NetworkVariable để đồng bộ vị trí Checkpoint qua mạng.
/// </summary>
public class RespawnManager : NetworkBehaviour
{
    public static RespawnManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private float _respawnDelay = 3f;
    [SerializeField] private float _fallRespawnY = -80f;
    [SerializeField] private float _fallRespawnDelay = 0.15f;
    [SerializeField] private Transform _initialHostSpawnPoint;
    [SerializeField] private Transform _initialClientSpawnPoint;

    // Sử dụng NetworkVariable để đồng bộ tọa độ hồi sinh từ Server xuống tất cả Client
    // Điều này đảm bảo khi Client hồi sinh, họ sẽ lấy đúng vị trí mới nhất mà Server đã lưu.
    private readonly NetworkVariable<Vector3> _currentHostSpawnPos = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Vector3> _currentClientSpawnPos = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private bool _eventsSubscribed;
    private bool _hasHostSpawnPoint;
    private bool _hasClientSpawnPoint;

    private void Awake()
    {
        if (Instance != null && Instance != this && Instance.gameObject.scene == gameObject.scene)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnEnable()
    {
        if (IsSpawned)
        {
            SubscribeEvents();
        }
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        SubscribeEvents();

        if (IsServer)
        {
            SeedConfiguredSpawnPoints();
        }
    }

    private void SubscribeEvents()
    {
        if (_eventsSubscribed) return;

        EventBus.OnCheckpointReached += HandleCheckpointReached;
        EventBus.OnPlayerDied += HandlePlayerDied;
        _eventsSubscribed = true;
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeEvents();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        UnsubscribeEvents();

        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    private void UnsubscribeEvents()
    {
        if (!_eventsSubscribed) return;

        EventBus.OnCheckpointReached -= HandleCheckpointReached;
        EventBus.OnPlayerDied -= HandlePlayerDied;
        _eventsSubscribed = false;
    }

    /// <summary>
    /// Records the spawn selected by PlayerSpawner for one owner. PlayerSpawner
    /// calls this before any player is released from the loading barrier, so the
    /// first death cannot use a stale scene position or Vector3.zero.
    /// </summary>
    public void SetInitialSpawnPoint(ulong clientId, Vector3 position)
    {
        if (!IsServer || !IsFinite(position)) return;

        if (clientId == NetworkManager.ServerClientId)
        {
            _currentHostSpawnPos.Value = position;
            _hasHostSpawnPoint = true;
        }
        else
        {
            _currentClientSpawnPos.Value = position;
            _hasClientSpawnPoint = true;
        }

        Debug.Log($"[RespawnManager] Seeded initial spawn for owner {clientId}: {position}");
    }

    /// <summary>
    /// Returns the latest server-authoritative spawn position for one player.
    /// Boss-room revive systems use this so a reached checkpoint is not replaced
    /// by the arena's initial spawn point.
    /// </summary>
    public bool TryGetCurrentSpawnPosition(ulong clientId, out Vector3 position)
    {
        bool isHost = NetworkManager != null && clientId == NetworkManager.ServerClientId;
        bool hasSpawnPoint = isHost ? _hasHostSpawnPoint : _hasClientSpawnPoint;
        position = isHost ? _currentHostSpawnPos.Value : _currentClientSpawnPos.Value;
        return hasSpawnPoint && IsFinite(position);
    }

    private void SeedConfiguredSpawnPoints()
    {
        if (_initialHostSpawnPoint != null)
        {
            SetInitialSpawnPoint(NetworkManager.ServerClientId, _initialHostSpawnPoint.position);
        }

        if (_initialClientSpawnPoint != null)
        {
            ulong firstRemoteClientId = FindFirstRemoteClientId();
            if (firstRemoteClientId != ulong.MaxValue)
            {
                SetInitialSpawnPoint(firstRemoteClientId, _initialClientSpawnPoint.position);
            }
        }
    }

    private ulong FindFirstRemoteClientId()
    {
        if (NetworkManager == null) return ulong.MaxValue;

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (clientId != NetworkManager.ServerClientId) return clientId;
        }

        return ulong.MaxValue;
    }

    /// <summary>
    /// Lưu điểm checkpoint mới nhất. Chỉ Server mới thực hiện ghi vào NetworkVariable.
    /// </summary>
    private void HandleCheckpointReached(string checkpointId, Vector3 hostSpawnPos, Vector3 clientSpawnPos)
    {
        if (IsServer)
        {
            _currentHostSpawnPos.Value = hostSpawnPos;
            _currentClientSpawnPos.Value = clientSpawnPos;
            _hasHostSpawnPoint = true;
            _hasClientSpawnPoint = true;
            Debug.Log($"<color=green>[RespawnManager] SERVER LƯU CHECKPOINT THÀNH CÔNG!</color> Trạm: {checkpointId} | Vị trí Host: {hostSpawnPos} | Vị trí Client: {clientSpawnPos}");
        }
    }

    /// <summary>
    /// Khi nhân vật bị chết, bắt đầu chạy routine delay để hồi sinh.
    /// Sự kiện này được bắn từ PlayerHealth qua ClientRpc nên sẽ chạy trên cả Host và Client.
    /// </summary>
    private readonly System.Collections.Generic.HashSet<ulong> _respawningPlayers = new();
    private readonly System.Collections.Generic.Dictionary<ulong, int> _respawnRequestVersions = new();

    private int BeginRespawnRequest(ulong clientId)
    {
        int version = _respawnRequestVersions.TryGetValue(clientId, out int previous)
            ? previous + 1
            : 1;
        _respawnRequestVersions[clientId] = version;
        _respawningPlayers.Add(clientId);
        return version;
    }

    private bool IsCurrentRespawnRequest(ulong clientId, int version)
    {
        return _respawnRequestVersions.TryGetValue(clientId, out int current)
            && current == version;
    }

    private void FinishRespawnRequest(ulong clientId, int version)
    {
        if (!IsCurrentRespawnRequest(clientId, version)) return;
        _respawnRequestVersions.Remove(clientId);
        _respawningPlayers.Remove(clientId);
    }

    /// <summary>
    /// Requests a server-authoritative immediate respawn at the owner's latest
    /// checkpoint. Used by minigames that must reset their arena promptly after
    /// a player dies instead of waiting for the normal world delay.
    /// </summary>
    public void RequestImmediateRespawn(ulong clientId)
    {
        if (!IsServer || SceneManager.GetActiveScene().name == Constants.Scenes.BOSS_FINAL)
        {
            return;
        }

        int requestVersion = BeginRespawnRequest(clientId);
        StartCoroutine(RespawnRoutine(clientId, true, requestVersion));
    }

    private void Update()
    {
        if (!IsServer || NetworkManager.Singleton == null) return;

        // A player can lose the board/ground without producing a normal death
        // event. Detect that on the server before the owner falls indefinitely.
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                || client.PlayerObject == null
                || _respawningPlayers.Contains(clientId))
            {
                continue;
            }

            Vector3 position = client.PlayerObject.transform.position;
            if (!IsFinite(position) || position.y > _fallRespawnY) continue;

            if (_respawningPlayers.Add(clientId))
            {
                Debug.LogWarning($"[RespawnManager] Player {clientId} fell below Y={_fallRespawnY}; immediate checkpoint respawn requested.");
                int requestVersion = _respawnRequestVersions.TryGetValue(clientId, out int previous)
                    ? previous + 1
                    : 1;
                _respawnRequestVersions[clientId] = requestVersion;
                StartCoroutine(RespawnRoutine(clientId, true, requestVersion));
            }
        }
    }

    private void HandlePlayerDied(ulong clientId)
    {
        // Boss rooms have a different, server-authoritative revive/wipe policy.
        if (SceneManager.GetActiveScene().name == Constants.Scenes.BOSS_FINAL) return;

        // Respawn is server-authoritative. Previously every peer moved its local
        // copy, racing ClientNetworkTransform/physics and causing players to fling.
        if (!IsServer || _respawningPlayers.Contains(clientId)) return;
        int requestVersion = BeginRespawnRequest(clientId);
        StartCoroutine(RespawnRoutine(clientId, false, requestVersion));
    }

    private IEnumerator RespawnRoutine(ulong clientId, bool fellOutOfWorld, int requestVersion)
    {
        float delay = fellOutOfWorld ? Mathf.Max(0f, _fallRespawnDelay) : Mathf.Max(0f, _respawnDelay);
        yield return new WaitForSecondsRealtime(delay);

        // A later immediate request supersedes the old normal-delay routine.
        if (!IsCurrentRespawnRequest(clientId, requestVersion)) yield break;

        if (!IsServer || NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) ||
            client.PlayerObject == null)
        {
            FinishRespawnRequest(clientId, requestVersion);
            yield break;
        }

        NetworkObject netObj = client.PlayerObject;
        bool isHost = clientId == NetworkManager.ServerClientId;
        Vector3 spawnPos = isHost ? _currentHostSpawnPos.Value : _currentClientSpawnPos.Value;
        bool hasSpawnPoint = isHost ? _hasHostSpawnPoint : _hasClientSpawnPoint;
        if (!hasSpawnPoint || !IsFinite(spawnPos))
        {
            Debug.LogError($"[RespawnManager] Refusing to respawn owner {clientId}; no valid PlayerSpawner point was seeded.");
            FinishRespawnRequest(clientId, requestVersion);
            yield break;
        }
        Quaternion spawnRotation = netObj.transform.rotation;
        Debug.Log($"[RespawnManager] SERVER respawning owner {clientId} at {spawnPos}");

        bool teleportConfirmed = true;
        if (netObj.TryGetComponent<NGOPlayerSync>(out var playerSync))
        {
            yield return playerSync.TeleportAndConfirmWithRetry(
                spawnPos,
                spawnRotation,
                confirmed => teleportConfirmed = confirmed);
        }
        else
        {
            netObj.transform.SetPositionAndRotation(spawnPos, spawnRotation);
        }

        if (!teleportConfirmed)
        {
            Debug.LogError($"[RespawnManager] Respawn aborted for owner {clientId}; teleport was not confirmed. Player remains dead instead of reviving at an unsafe pose.");
            FinishRespawnRequest(clientId, requestVersion);
            yield break;
        }

        PlayerHealth health = netObj.GetComponent<PlayerHealth>();
        if (health != null) health.ReviveAtHealthPercent(1f);
        FinishRespawnRequest(clientId, requestVersion);
    }

    private static bool IsFinite(Vector3 position)
    {
        return float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z);
    }
}
