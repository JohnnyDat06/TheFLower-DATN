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
    private void HandlePlayerDied(ulong clientId)
    {
        Debug.Log($"<color=red>[RespawnManager] Phát hiện Player {clientId} đã chết! Bắt đầu đếm ngược {_respawnDelay} giây...</color>");
        StartCoroutine(RespawnRoutine(clientId));
    }

    private IEnumerator RespawnRoutine(ulong clientId)
    {
        // 1. Chờ vài giây để player kịp load xong hiệu ứng chết
        yield return new WaitForSeconds(_respawnDelay);

        Debug.Log($"[RespawnManager] Đã ngâm xác đủ thời gian! Đang lôi {clientId} dậy...");

        // 2. Chỉ máy sở hữu nhân vật đã chết mới di chuyển nhân vật local.
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId != clientId)
        {
            yield break;
        }

        NetworkObject netObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (netObj == null)
        {
            var allHealths = Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
            foreach (var playerHealth in allHealths)
            {
                if (playerHealth.OwnerClientId == clientId)
                {
                    netObj = playerHealth.NetworkObject;
                    break;
                }
            }
        }

        if (netObj == null)
        {
            Debug.LogError($"[RespawnManager] Lỗi: PlayerObject của Client {clientId} bị rỗng!");
            yield break;
        }

        var fsm = netObj.GetComponent<PlayerStateMachine>();
        var health = netObj.GetComponent<PlayerHealth>();
        var rb = netObj.GetComponent<Rigidbody>();

        bool isHost = clientId == NetworkManager.ServerClientId;
        Vector3 spawnPos = isHost ? _currentHostSpawnPos.Value : _currentClientSpawnPos.Value;

        Debug.Log($"[RespawnManager] HỒI SINH OWNER {clientId}! Đẩy về vị trí Checkpoint: {spawnPos}");

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.MovePosition(spawnPos);
            netObj.transform.position = spawnPos;
        }
        else
        {
            netObj.transform.position = spawnPos;
        }

        EventBus.RaisePlayerRespawned(clientId, spawnPos);

        if (health != null)
        {
            health.RestoreFullHealth();
        }

        if (fsm != null)
        {
            fsm.TransitionTo(PlayerStateType.Respawning);
            yield return new WaitForSeconds(0.5f);
            fsm.TransitionTo(PlayerStateType.Idle);
        }
    }
}
