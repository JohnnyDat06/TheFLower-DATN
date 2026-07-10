using System.Collections;
using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

public class Level04FlowManager : NetworkBehaviour
{
    public static Level04FlowManager Instance { get; private set; }

    [SerializeField, Min(0f)] private float _wingUnlockDuration = 2f;
    [SerializeField, Min(0f)] private float _endTransitionDelay = 4f;
    [SerializeField] private string _endSceneName = Constants.Scenes.LOBBY;
    [SerializeField] private int _levelIndex = 4;
    [SerializeField] private Transform _directPlayHostSpawn;
    [SerializeField] private Transform _directPlayClientSpawn;

    [Header("Development Debug")]
    [Tooltip("Cho phép Host chơi một mình trong Editor/Development Build. Bị vô hiệu hóa trong Release Build.")]
    [SerializeField] private bool _enableHostSoloDebug;

    private readonly NetworkVariable<Level04Phase> _phase = new(
        Level04Phase.IntroPeak,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public Level04Phase Phase => _phase.Value;

    public bool IsHostSoloDebugActive
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return _enableHostSoloDebug
                && IsServer
                && NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsHost
                && NetworkManager.Singleton.ConnectedClientsList.Count == 1;
#else
            return false;
#endif
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _phase.OnValueChanged += HandlePhaseChanged;
        EventBus.RaiseLevel04PhaseChanged(_phase.Value);

        if (IsServer && Networking.LobbySystem.LobbyManager.Instance == null)
        {
            StartCoroutine(InitializeDirectPlayPlayers());
        }
    }

    public override void OnNetworkDespawn()
    {
        _phase.OnValueChanged -= HandlePhaseChanged;
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void Update()
    {
        if (!IsHostSoloDebugActive || Keyboard.current == null) return;

        if (Keyboard.current.f8Key.wasPressedThisFrame)
        {
            BeginWingUnlockServer();
        }
    }
#endif

    public bool CanUseHostSoloDebug(ulong playerId)
    {
        return IsHostSoloDebugActive
            && playerId == NetworkManager.ServerClientId;
    }

    public void BeginWingUnlockServer()
    {
        if (!IsServer || _phase.Value != Level04Phase.IntroPeak) return;
        StartCoroutine(WingUnlockRoutine());
    }

    public void SetPhaseServer(Level04Phase phase)
    {
        if (!IsServer || phase < _phase.Value) return;
        _phase.Value = phase;
    }

    public void CompleteFlightServer()
    {
        if (!IsServer || _phase.Value == Level04Phase.EndTransition) return;

        _phase.Value = Level04Phase.EndTransition;
        ForEachPlayer((flight, wing) =>
        {
            flight?.SetFlightEnabledServer(false);
            wing?.SetStateServer(PlayerWingState.Landing);
        });
        EventBus.RaiseLevelCompleted(_levelIndex);
        StartCoroutine(EndTransitionRoutine());
    }

    private IEnumerator WingUnlockRoutine()
    {
        _phase.Value = Level04Phase.WingUnlock;
        ForEachPlayer((flight, wing) => wing?.SetStateServer(PlayerWingState.Unlocking));

        yield return new WaitForSeconds(_wingUnlockDuration);

        _phase.Value = Level04Phase.TakeOff;
        ForEachPlayer((flight, wing) =>
        {
            wing?.SetStateServer(PlayerWingState.Gliding);
            flight?.SetFlightEnabledServer(true);
        });
    }

    private IEnumerator EndTransitionRoutine()
    {
        if (LoadingSyncManager.Instance != null)
        {
            LoadingSyncManager.Instance.ShowToBeContinuedClientRpc(
                true,
                "The Last Flight Home",
                false);
            LoadingSyncManager.Instance.FadeInClientRpc();
        }

        yield return new WaitForSecondsRealtime(_endTransitionDelay);

        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadScene(_endSceneName);
        }
        else if (NetworkManager.Singleton != null)
        {
            if (SceneLoader.CanLoadScene(_endSceneName))
            {
                NetworkManager.Singleton.SceneManager.LoadScene(
                    _endSceneName,
                    UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
        }
    }

    private IEnumerator InitializeDirectPlayPlayers()
    {
        yield return new WaitForSeconds(0.2f);

        if (NetworkManager.Singleton == null) yield break;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            bool isHost = client.ClientId == NetworkManager.ServerClientId;
            Transform spawn = isHost ? _directPlayHostSpawn : _directPlayClientSpawn;
            if (spawn == null) continue;

            var sync = client.PlayerObject.GetComponent<NGOPlayerSync>();
            if (sync == null) continue;
            sync.Teleport(spawn.position, spawn.rotation);
            sync.ReleasePlayerClientRpc();
        }
    }

    private void ForEachPlayer(System.Action<Level04FlightController, PlayerWingController> action)
    {
        if (NetworkManager.Singleton == null) return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            action(
                client.PlayerObject.GetComponent<Level04FlightController>(),
                client.PlayerObject.GetComponent<PlayerWingController>());
        }
    }

    private static void HandlePhaseChanged(Level04Phase previous, Level04Phase current)
    {
        EventBus.RaiseLevel04PhaseChanged(current);
    }
}
