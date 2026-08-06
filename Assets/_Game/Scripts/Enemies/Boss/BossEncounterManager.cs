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

    public EncounterState State => _state.Value;
    public SOBossEncounterConfig Config => _config;
    public bool IsActive => _state.Value == EncounterState.Active;

    private void Awake()
    {
        Instance = this;
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

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Called by the server-side room trigger when a player enters the arena.</summary>
    public void RegisterPlayerEntry(ulong clientId)
    {
        if (!IsServer || _state.Value != EncounterState.WaitingForPlayers) return;
        _playersInEntry.Add(clientId);
        if (_playersInEntry.Count >= RequiredPlayerCount()) StartCoroutine(BeginEncounterRoutine());
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

    public bool TryGetRespawnPoint(ulong clientId, out Transform point)
    {
        point = clientId == NetworkManager.ServerClientId ? _hostRespawnPoint : _clientRespawnPoint;
        return point != null;
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
        SetDoorsClosed(true);
        yield return new WaitForSeconds(_config != null ? _config.IntroDuration : 2f);
        if (!_resetInProgress) _state.Value = EncounterState.Active;
    }

    private IEnumerator WipeRoutine()
    {
        _resetInProgress = true;
        _state.Value = EncounterState.WipeReset;
        yield return new WaitForSeconds(_config != null ? _config.WipeResetDelay : 2f);

        ResetTargets();
        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            if (TryGetRespawnPoint(client.ClientId, out Transform point) &&
                client.PlayerObject.TryGetComponent<NGOPlayerSync>(out var sync))
            {
                sync.Teleport(point.position, point.rotation);
            }

            if (client.PlayerObject.TryGetComponent<PlayerHealth>(out var health))
                health.ReviveAtHealthPercent(1f);
        }

        _playersInEntry.Clear();
        _state.Value = EncounterState.WaitingForPlayers;
        SetDoorsClosed(false);
        RegisterSpawnedPlayersServer();
        _resetInProgress = false;
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
