using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Server-authoritative lifecycle for the final boss-room attempt.</summary>
public sealed class BossEncounterManager : NetworkBehaviour
{
    public enum EncounterState : byte { WaitingForPlayers, Intro, Active, WipeReset, Victory }

    public static BossEncounterManager Instance { get; private set; }

    [SerializeField] private SOBossEncounterConfig _config;
    [SerializeField] private Transform _hostRespawnPoint;
    [SerializeField] private Transform _clientRespawnPoint;
    [SerializeField] private GameObject[] _resetTargets;
    [SerializeField] private GameObject[] _doorsToClose;

    private readonly NetworkVariable<EncounterState> _state = new(
        EncounterState.WaitingForPlayers,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly HashSet<ulong> _playersInEntry = new();
    private bool _resetInProgress;
    private BossNetworkState _bossNetworkState;

    public EncounterState State => _state.Value;
    public SOBossEncounterConfig Config => _config;
    public bool IsActive => _state.Value == EncounterState.Active;
    /// <summary>True from the first boss intro through wipe recovery, until the boss is defeated.</summary>
    public bool HasEncounterStarted => _state.Value is EncounterState.Intro or EncounterState.Active or EncounterState.WipeReset;

    private void Awake()
    {
        Instance = this;
        _bossNetworkState = GetComponent<BossNetworkState>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer && SceneManager.GetActiveScene().name == Constants.Scenes.BOSS_FINAL)
            SetDoorsClosed(false);
    }

    public override void OnNetworkDespawn()
    {
        StopAllCoroutines();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    /// <summary>Called by the server-side room trigger when a player enters the arena.</summary>
    public void RegisterPlayerEntry(ulong clientId)
    {
        if (!IsServer || _state.Value != EncounterState.WaitingForPlayers) return;
        if (!_playersInEntry.Add(clientId)) return;

        int requiredPlayers = RequiredPlayerCount();
        Debug.Log($"[BossEncounterManager] Player {clientId} entered EnterBoss. {_playersInEntry.Count}/{requiredPlayers} ready.", this);
        if (_playersInEntry.Count >= requiredPlayers) StartCoroutine(BeginEncounterRoutine());
    }

    /// <summary>Routes an owner-local EnterBoss trigger to the authoritative Host.</summary>
    public void RequestPlayerEntry(ulong clientId)
    {
        if (!IsSpawned || NetworkManager.Singleton == null) return;
        if (IsServer)
        {
            RegisterPlayerEntry(clientId);
            return;
        }

        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        RequestPlayerEntryRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestPlayerEntryRpc(RpcParams rpcParams = default)
    {
        RegisterPlayerEntry(rpcParams.Receive.SenderClientId);
    }

    /// <summary>Registers players placed at the boss-room spawn points by PlayerSpawner.</summary>
    public void RegisterSpawnedPlayersServer()
    {
        if (!IsServer || NetworkManager.Singleton == null) return;
        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            RegisterPlayerEntry(client.ClientId);
    }

    public void NotifyBothPlayersDeadServer()
    {
        if (!IsServer || _resetInProgress || _state.Value is EncounterState.WipeReset or EncounterState.Victory) return;
        StartCoroutine(WipeRoutine());
    }

    /// <summary>Resolves a respawn pose, preferring the most recently reached shared checkpoint.</summary>
    public bool TryGetRespawnPose(ulong clientId, out Vector3 position, out Quaternion rotation)
    {
        Transform initialPoint = clientId == NetworkManager.ServerClientId ? _hostRespawnPoint : _clientRespawnPoint;
        rotation = initialPoint != null ? initialPoint.rotation : Quaternion.identity;

        if (RespawnManager.Instance != null &&
            RespawnManager.Instance.TryGetCurrentSpawnPosition(clientId, out position))
        {
            return true;
        }

        position = initialPoint != null ? initialPoint.position : default;
        return initialPoint != null;
    }

    public void CompleteEncounterServer()
    {
        if (!IsServer || _state.Value != EncounterState.Active) return;
        _state.Value = EncounterState.Victory;
        SetDoorsClosed(false);
    }

    private int RequiredPlayerCount() => Mathf.Min(2, NetworkManager.Singleton.ConnectedClientsList.Count);

    private IEnumerator BeginEncounterRoutine()
    {
        if (_state.Value != EncounterState.WaitingForPlayers) yield break;
        _state.Value = EncounterState.Intro;
        Debug.Log("[BossEncounterManager] Both players entered EnterBoss. Boss intro started.", this);
        SetDoorsClosed(true);
        yield return new WaitForSeconds(_config != null ? _config.IntroDuration : 2f);
        if (!_resetInProgress)
        {
            _state.Value = EncounterState.Active;
            Debug.Log("[BossEncounterManager] Boss encounter is Active.", this);
        }
    }

    private IEnumerator WipeRoutine()
    {
        _resetInProgress = true;
        _state.Value = EncounterState.WipeReset;
        yield return new WaitForSeconds(_config != null ? _config.WipeResetDelay : 2f);

        if (_bossNetworkState == null) _bossNetworkState = GetComponent<BossNetworkState>();
        _bossNetworkState?.ResetEncounterServer();
        ResetTargets();
        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            bool teleportConfirmed = true;
            if (TryGetRespawnPose(client.ClientId, out Vector3 respawnPosition, out Quaternion respawnRotation) &&
                client.PlayerObject.TryGetComponent<NGOPlayerSync>(out var sync))
            {
                yield return sync.TeleportAndConfirmWithRetry(
                    respawnPosition,
                    respawnRotation,
                    confirmed => teleportConfirmed = confirmed);
            }
            else
            {
                teleportConfirmed = false;
                Debug.LogError($"[BossEncounterManager] Wipe reset has no valid teleport target for owner {client.ClientId}.");
            }

            if (teleportConfirmed && client.PlayerObject.TryGetComponent<PlayerHealth>(out var health))
                health.ReviveAtHealthPercent(1f);
            else if (!teleportConfirmed)
                Debug.LogError($"[BossEncounterManager] Wipe reset did not revive owner {client.ClientId}; teleport was not confirmed.");
        }

        _resetInProgress = false;
        SetDoorsClosed(true);
        _state.Value = EncounterState.Active;
        Debug.Log("[BossEncounterManager] Both players revived. Boss encounter resumed without leaving boss mode.", this);
    }

    private void ResetTargets()
    {
        if (_resetTargets == null) return;
        foreach (GameObject target in _resetTargets)
        {
            if (target != null) target.SetActive(true);
        }
    }

    private void SetDoorsClosed(bool closed)
    {
        if (_doorsToClose == null) return;
        foreach (GameObject door in _doorsToClose)
        {
            if (door != null) door.SetActive(closed);
        }
    }
}
