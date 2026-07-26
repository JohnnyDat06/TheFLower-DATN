using System.Collections;
using Unity.Netcode;
using UnityEngine;

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
    [SerializeField] private Transform _initialHostSpawnPoint;
    [SerializeField] private Transform _initialClientSpawnPoint;

    // Sử dụng NetworkVariable để đồng bộ tọa độ hồi sinh từ Server xuống tất cả Client
    // Điều này đảm bảo khi Client hồi sinh, họ sẽ lấy đúng vị trí mới nhất mà Server đã lưu.
    private readonly NetworkVariable<Vector3> _currentHostSpawnPos = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Vector3> _currentClientSpawnPos = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private bool _eventsSubscribed;

    private void Awake()
    {
        if (Instance != null && Instance != this && Instance.gameObject.scene == gameObject.scene)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        // Chờ một chút để Server kết nối và Player tự load ra xong lúc bắt đầu màn.
        Invoke(nameof(SetInitialSpawnPoints), 2f);
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

    private void SetInitialSpawnPoints()
    {
        if (!IsServer) return;

        if (_initialHostSpawnPoint != null)
        {
            _currentHostSpawnPos.Value = _initialHostSpawnPoint.position;
        }

        if (_initialClientSpawnPoint != null)
        {
            _currentClientSpawnPos.Value = _initialClientSpawnPoint.position;
        }

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                if (client.ClientId == NetworkManager.ServerClientId && _initialHostSpawnPoint == null)
                    _currentHostSpawnPos.Value = client.PlayerObject.transform.position;
                else if (client.ClientId != NetworkManager.ServerClientId && _initialClientSpawnPoint == null)
                    _currentClientSpawnPos.Value = client.PlayerObject.transform.position;
            }
        }
        Debug.Log($"[RespawnManager] Khởi tạo vị trí vạch xuất phát: Host ({_currentHostSpawnPos.Value}) | Client ({_currentClientSpawnPos.Value})");
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
            Debug.Log($"<color=green>[RespawnManager] SERVER LƯU CHECKPOINT THÀNH CÔNG!</color> Trạm: {checkpointId} | Vị trí Host: {hostSpawnPos} | Vị trí Client: {clientSpawnPos}");
        }
    }

    /// <summary>
    /// Khi nhân vật bị chết, bắt đầu chạy routine delay để hồi sinh.
    /// Sự kiện này được bắn từ PlayerHealth qua ClientRpc nên sẽ chạy trên cả Host và Client.
    /// </summary>
    private readonly System.Collections.Generic.HashSet<ulong> _respawningPlayers = new();

    private void HandlePlayerDied(ulong clientId)
    {
        // Respawn is server-authoritative. Previously every peer moved its local
        // copy, racing ClientNetworkTransform/physics and causing players to fling.
        if (!IsServer || !_respawningPlayers.Add(clientId)) return;
        StartCoroutine(RespawnRoutine(clientId));
    }

    private IEnumerator RespawnRoutine(ulong clientId)
    {
        yield return new WaitForSeconds(_respawnDelay);

        if (!IsServer || NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) ||
            client.PlayerObject == null)
        {
            _respawningPlayers.Remove(clientId);
            yield break;
        }

        NetworkObject netObj = client.PlayerObject;
        bool isHost = clientId == NetworkManager.ServerClientId;
        Vector3 spawnPos = isHost ? _currentHostSpawnPos.Value : _currentClientSpawnPos.Value;
        Quaternion spawnRotation = netObj.transform.rotation;
        Debug.Log($"[RespawnManager] SERVER respawning owner {clientId} at {spawnPos}");

        if (netObj.TryGetComponent<NGOPlayerSync>(out var playerSync))
        {
            playerSync.Teleport(spawnPos, spawnRotation);
        }
        else
        {
            netObj.transform.SetPositionAndRotation(spawnPos, spawnRotation);
        }

        PlayerHealth health = netObj.GetComponent<PlayerHealth>();
        if (health != null) health.RestoreFullHealth();
        RespawnClientRpc(clientId, spawnPos);
        _respawningPlayers.Remove(clientId);
    }

    [ClientRpc]
    private void RespawnClientRpc(ulong clientId, Vector3 spawnPos)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId != clientId)
            return;

        EventBus.RaisePlayerRespawned(clientId, spawnPos);
        NetworkObject netObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
        PlayerStateMachine fsm = netObj != null ? netObj.GetComponent<PlayerStateMachine>() : null;
        if (fsm != null) StartCoroutine(ReturnToIdleAfterRespawn(fsm));
    }

    private IEnumerator ReturnToIdleAfterRespawn(PlayerStateMachine fsm)
    {
        fsm.TransitionTo(PlayerStateType.Respawning);
        yield return new WaitForSeconds(0.5f);
        if (fsm != null) fsm.TransitionTo(PlayerStateType.Idle);
    }
}
