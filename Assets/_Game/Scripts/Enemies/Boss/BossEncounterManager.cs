using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Server-authoritative lifecycle for the final boss room.</summary>
public sealed class BossEncounterManager : NetworkBehaviour
{
    public enum EncounterState : byte
    {
        WaitingForPlayers,
        Intro,
        Active,
        WipeReset,
        Victory
    }

    [SerializeField] private SOBossEncounterConfig _config;
    [SerializeField] private Transform _hostRespawnPoint;
    [SerializeField] private Transform _clientRespawnPoint;
    [SerializeField] private GameObject[] _resetTargets;

    private readonly NetworkVariable<EncounterState> _state = new(
        EncounterState.WaitingForPlayers,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private bool _resetInProgress;

    public EncounterState State => _state.Value;
    public SOBossEncounterConfig Config => _config;
    public bool IsActive => _state.Value == EncounterState.Active;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer && SceneManager.GetActiveScene().name == Constants.Scenes.BOSS_FINAL)
        {
            StartCoroutine(BeginEncounterRoutine());
        }
    }

    public override void OnNetworkDespawn()
    {
        StopAllCoroutines();
        base.OnNetworkDespawn();
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

    private IEnumerator BeginEncounterRoutine()
    {
        while (NetworkManager.Singleton == null || NetworkManager.Singleton.ConnectedClientsList.Count == 0)
            yield return null;

        _state.Value = EncounterState.Intro;
        yield return new WaitForSeconds(_config != null ? _config.IntroDuration : 2f);
        if (!_resetInProgress) _state.Value = EncounterState.Active;
    }

    private IEnumerator WipeRoutine()
    {
        _resetInProgress = true;
        _state.Value = EncounterState.WipeReset;
        yield return new WaitForSeconds(_config != null ? _config.WipeResetDelay : 2f);

        ResetTargets();
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            if (TryGetRespawnPoint(client.ClientId, out Transform point) &&
                client.PlayerObject.TryGetComponent<NGOPlayerSync>(out var sync))
            {
                sync.Teleport(point.position, point.rotation);
            }

            if (client.PlayerObject.TryGetComponent<PlayerHealth>(out var health))
                health.RestoreFullHealth();
        }

        _state.Value = EncounterState.Active;
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
}
