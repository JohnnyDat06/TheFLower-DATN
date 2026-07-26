using System.Collections.Generic;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// NGOPlayerSync — Quản lý đồng bộ và trạng thái vật lý của Player.
/// Hỗ trợ cơ chế Loading Barrier: Đóng băng nhân vật cho đến khi cả 2 người chơi sẵn sàng.
/// </summary>
public class NGOPlayerSync : NetworkBehaviour
{
    [Header("Local Simulation")]
    [SerializeField] private PlayerInputHandler _inputHandler;
    [SerializeField] private PlayerStateMachine _stateMachine;
    [SerializeField] private PlayerController _playerController;
    [SerializeField] private PlayerAnimator _playerAnimator;

    [Header("Netcode Components")]
    [SerializeField] private ClientNetworkTransform _networkTransform;
    [SerializeField] private NetworkRigidbody _networkRigidbody;
    [SerializeField] private ClientNetworkAnimator _networkAnimator;
    [SerializeField] private Rigidbody _rigidbody;

    [Header("Optional Owner-Only Behaviours")]
    [SerializeField] private Behaviour[] _ownerOnlyBehaviours;

    private bool _isTeleporting; 
    private bool _isFrozenBySystem = true; // Trạng thái đóng băng hệ thống khi đổi màn

    private static readonly HashSet<ulong> MissingSpawnerWarnings = new();
    private Coroutine _readyReportRoutine;

    public bool IsTeleporting => _isTeleporting || _isFrozenBySystem;

    private void Awake()
    {
        CacheReferences();
        ApplyNetcodeDefaults();
    }

    private bool IsTestMode() 
    {
        return Networking.LobbySystem.LobbyManager.Instance == null || string.IsNullOrEmpty(Networking.LobbySystem.LobbyManager.Instance.GetPlayerId());
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete += HandleSceneLoaded;
        }

        _isFrozenBySystem = !IsTestMode();
        if (_rigidbody != null)
        {
            if (!_rigidbody.isKinematic)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
            _rigidbody.isKinematic = true;
        }

        ApplyAuthorityState();

        // Players also exist in the Lobby, where no PlayerSpawner is expected.
        if (IsOwner && !IsTestMode() && IsGameplayScene())
        {
            BeginReadyReporting();
        }
    }

    private void BeginReadyReporting()
    {
        if (_readyReportRoutine != null || !IsSpawned || !IsOwner || IsTestMode() || !IsGameplayScene()) return;
        _readyReportRoutine = StartCoroutine(ReadyReportRoutine());
    }

    private IEnumerator ReadyReportRoutine()
    {
        yield return new WaitForEndOfFrame();

        while (IsSpawned && IsOwner && _isFrozenBySystem && IsGameplayScene())
        {
            ReportReadyToServerRpc();
            yield return new WaitForSecondsRealtime(1f);
        }

        _readyReportRoutine = null;
    }

    private void StopReadyReporting()
    {
        if (_readyReportRoutine == null) return;
        StopCoroutine(_readyReportRoutine);
        _readyReportRoutine = null;
    }

    public override void OnNetworkDespawn()
    {
        StopReadyReporting();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= HandleSceneLoaded;
        }
        base.OnNetworkDespawn();
    }

    private void HandleSceneLoaded(ulong clientId, string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode)
    {
        if (NetworkManager.Singleton == null || clientId != NetworkManager.Singleton.LocalClientId) return;

        _isFrozenBySystem = !IsTestMode();
        ApplyAuthorityState();

        if (IsOwner && !IsTestMode() && IsGameplayScene(sceneName))
        {
            BeginReadyReporting();
        }
    }

    [ServerRpc]
    private void ReportReadyToServerRpc(ServerRpcParams rpcParams = default)
    {
        var senderId = rpcParams.Receive.SenderClientId;

        if (LoadingSyncManager.Instance != null)
        {
            LoadingSyncManager.Instance.MarkClientReady(senderId);
        }

        if (Game.Network.PlayerSpawner.Instance != null)
        {
            MissingSpawnerWarnings.Remove(senderId);
            Game.Network.PlayerSpawner.Instance.ReportPlayerReady(senderId);
        }
        else if (MissingSpawnerWarnings.Add(senderId))
        {
            Debug.LogWarning($"[NGOPlayerSync] PlayerSpawner.Instance is NULL on Server! senderId={senderId}");
        }
    }

    private static bool IsGameplayScene(string sceneName = null)
    {
        sceneName ??= UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return !string.IsNullOrEmpty(sceneName) && !sceneName.Contains("Lobby");
    }


    /// <summary>
    /// Lệnh từ Server để giải phóng nhân vật sau khi đã dịch chuyển xong.
    /// </summary>
    [ClientRpc]
    public void ReleasePlayerClientRpc()
    {
        Debug.Log($"[NGOPlayerSync] System Released Player {OwnerClientId}. Game starts now!");
        _isFrozenBySystem = false;
        StopReadyReporting();
        ApplyAuthorityState();
    }

    public void Teleport(Vector3 position, Quaternion rotation)
    {
        if (IsServer)
        {
            if (IsOwner) StartCoroutine(PerformTeleportCoroutine(position, rotation));
            TeleportClientRpc(position, rotation);
        }
    }

    [ClientRpc]
    private void TeleportClientRpc(Vector3 position, Quaternion rotation)
    {
        if (IsServer && IsOwner) return;

        if (IsOwner)
        {
            StartCoroutine(PerformTeleportCoroutine(position, rotation));
        }
    }

    private IEnumerator PerformTeleportCoroutine(Vector3 position, Quaternion rotation)
    {
        if (_isTeleporting) yield break;
        _isTeleporting = true;
        
        // Đưa lên cao 0.15f thay vì 0.3f để mượt hơn nhưng vẫn tránh kẹt sàn
        Vector3 safePosition = position + Vector3.up * 0.15f;

        if (_rigidbody != null)
        {
            ResetRigidbodyMotion();
            _rigidbody.isKinematic = true; 
        }

        transform.SetPositionAndRotation(safePosition, rotation);
        if (_networkTransform != null) _networkTransform.Teleport(safePosition, rotation, transform.localScale);

        // Đợi Physics ổn định - giảm số lượng frame đợi để giảm lag
        int framesToWait = 5; 
        while (framesToWait > 0)
        {
            if (this == null) yield break;
            framesToWait--;
            yield return new WaitForFixedUpdate(); 
        }

        _isTeleporting = false;
        ApplyAuthorityState();
    }

    private void ApplyAuthorityState()
    {
        bool isLocked = _isTeleporting || _isFrozenBySystem;

        if (_rigidbody != null)
        {
            if (isLocked) ResetRigidbodyMotion();
            _rigidbody.isKinematic = isLocked || !IsOwner;
            _rigidbody.useGravity = !isLocked && IsOwner;
        }

        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("Lobby"))
        {
            SetLocalSimulationEnabled(false);
            return;
        }

        SetLocalSimulationEnabled(IsOwner && !isLocked);
    }

    private void ResetRigidbodyMotion()
    {
        if (_rigidbody == null || _rigidbody.isKinematic) return;

        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
    }

    private void SetLocalSimulationEnabled(bool enabled)
    {
        if (_ownerOnlyBehaviours == null) BuildOwnerOnlyBehaviourList();
        foreach (var behaviour in _ownerOnlyBehaviours)
        {
            if (behaviour != null) behaviour.enabled = enabled;
        }
    }

    private void CacheReferences()
    {
        _inputHandler ??= GetComponent<PlayerInputHandler>();
        _stateMachine ??= GetComponent<PlayerStateMachine>();
        _playerController ??= GetComponent<PlayerController>();
        _playerAnimator ??= GetComponent<PlayerAnimator>();
        _networkTransform ??= GetComponent<ClientNetworkTransform>();
        _networkRigidbody ??= GetComponent<NetworkRigidbody>();
        _networkAnimator ??= GetComponent<ClientNetworkAnimator>();
        _rigidbody ??= GetComponent<Rigidbody>();
    }

    private void BuildOwnerOnlyBehaviourList()
    {
        _ownerOnlyBehaviours = new Behaviour[] { _inputHandler, _stateMachine, _playerController, _playerAnimator };
    }

    private void ApplyNetcodeDefaults()
    {
        if (_networkTransform != null) {
            _networkTransform.Interpolate = true;
            _networkTransform.SlerpPosition = false;
        }
    }
}
