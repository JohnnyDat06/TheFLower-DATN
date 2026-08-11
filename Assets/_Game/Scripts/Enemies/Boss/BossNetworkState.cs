using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Keeps the Cat Sphinx encounter server-authoritative and mirrors its gameplay state to clients.
/// This component lives on the existing BOSS ENCOUNTER scene NetworkObject.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class BossNetworkState : NetworkBehaviour
{
    private const ulong NoNetworkObject = ulong.MaxValue;

    [Tooltip("BossArena_Architecture chua toan bo controller va marker cua Cat Sphinx.")]
    [SerializeField] private BossArenaReferences _arenaReferences;

    private readonly NetworkVariable<BossFightNetworkSnapshot> _fightSnapshot = new(
        new BossFightNetworkSnapshot
        {
            TargetNetworkObjectId = NoNetworkObject,
            PendingCorePoint = -1
        },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<BossAttackNetworkSnapshot> _attackSnapshot = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkList<byte> _runeStates;
    private NetworkList<byte> _sealStates;
    private NetworkList<byte> _floorTileStates;

    private BossController _bossController;
    private BossTargetIndicator _targetIndicator;
    private BossPawSlamAttack _pawSlamAttack;
    private BossTargetSlamAttack _targetSlamAttack;
    private BossDoublePawAttack _doublePawAttack;
    private BossEarthquakeAttack _earthquakeAttack;
    private BossAnimationController _animationController;
    private FloorPatternController _floorPatternController;
    private RuneManager _runeManager;
    private SealManager _sealManager;
    private BossStunController _stunController;
    private BossCoreController _coreController;
    private DualCoreInteractionController _dualCoreController;
    private BossPhaseController _phaseController;
    private FloorTileManager _floorTileManager;
    private BossDefeatController _defeatController;
    private RuneController[] _runes = Array.Empty<RuneController>();
    private SealController[] _seals = Array.Empty<SealController>();
    private FloorTile[] _floorTiles = Array.Empty<FloorTile>();

    private BossNetworkAttackType _serverAttackType;
    private double _serverAttackStartedAt;
    private BossNetworkAttackType _clientAttackType;
    private bool _clientAttackPoseReset;

    /// <summary>The active Cat Sphinx network adapter in Final_Boss_Room.</summary>
    public static BossNetworkState Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("[BossNetworkState] More than one boss network adapter exists in the scene.", this);
        else
            Instance = this;

        _runeStates = new NetworkList<byte>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        _sealStates = new NetworkList<byte>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        _floorTileStates = new NetworkList<byte>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        CacheArenaComponents();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CacheArenaComponents();

        _fightSnapshot.OnValueChanged += HandleFightSnapshotChanged;
        _runeStates.OnListChanged += HandleRuneListChanged;
        _sealStates.OnListChanged += HandleSealListChanged;
        _floorTileStates.OnListChanged += HandleFloorTileListChanged;

        if (IsServer)
        {
            ShockwaveController.ShockwaveSpawned += HandleServerShockwaveSpawned;
            CaptureServerState();
        }
        else
        {
            DisableClientGameplaySimulation();
            ApplyAllReplicatedState();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) ShockwaveController.ShockwaveSpawned -= HandleServerShockwaveSpawned;
        _fightSnapshot.OnValueChanged -= HandleFightSnapshotChanged;
        _runeStates.OnListChanged -= HandleRuneListChanged;
        _sealStates.OnListChanged -= HandleSealListChanged;
        _floorTileStates.OnListChanged -= HandleFloorTileListChanged;
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    private void Update()
    {
        if (!IsSpawned) return;

        if (IsServer)
        {
            CaptureServerState();
            return;
        }

        ApplyReplicatedTargetIfAvailable();
        UpdateClientAttackVisual();
    }

    /// <summary>Sends one local Client Seal interaction to the authoritative Host.</summary>
    public void RequestSealInteraction(SealController seal)
    {
        CacheArenaComponents();
        int sealIndex = Array.IndexOf(_seals, seal);
        if (sealIndex < 0 || !IsSpawned) return;

        if (IsServer)
        {
            _sealManager?.TryActivateSeal(seal, NetworkManager.LocalClientId);
            return;
        }

        RequestSealInteractionRpc(sealIndex);
    }

    /// <summary>Sends one local Client Core-point interaction to the authoritative Host.</summary>
    public void RequestCoreInteraction(CoreInteractionPoint point)
    {
        if (point == null || !IsSpawned) return;

        if (IsServer)
        {
            _dualCoreController?.TryActivatePoint(point, NetworkManager.LocalClientId);
            return;
        }

        RequestCoreInteractionRpc((int)point.PointId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestSealInteractionRpc(int sealIndex, RpcParams rpcParams = default)
    {
        CacheArenaComponents();
        if (!IsServer || sealIndex < 0 || sealIndex >= _seals.Length) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        _sealManager?.TryActivateSeal(_seals[sealIndex], senderClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestCoreInteractionRpc(int pointId, RpcParams rpcParams = default)
    {
        CacheArenaComponents();
        if (!IsServer || !Enum.IsDefined(typeof(CorePointId), pointId)) return;

        CoreInteractionPoint point = FindCorePoint((CorePointId)pointId);
        if (point == null || !IsPlayerNearCorePoint(rpcParams.Receive.SenderClientId, point)) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        _dualCoreController?.TryActivatePoint(point, senderClientId);
    }

    [Rpc(SendTo.Everyone)]
    private void SpawnShockwaveRpc(Vector3 position, Vector3 direction, float speed, float width, float maxRange)
    {
        // SendTo.Everyone also invokes on Host. The Host already owns the authoritative wave.
        if (IsServer) return;
        ShockwaveController.Spawn(position, direction, speed, width, maxRange, false, false);
    }

    private void CaptureServerState()
    {
        CacheArenaComponents();
        if (_arenaReferences == null) return;

        _fightSnapshot.Value = new BossFightNetworkSnapshot
        {
            BossState = (int)(_bossController != null ? _bossController.CurrentState : BossState.Idle),
            TargetNetworkObjectId = ResolveTargetNetworkObjectId(),
            Phase = (int)(_phaseController != null ? _phaseController.CurrentPhase : BossCombatPhase.PhaseOne),
            CoreState = (int)(_coreController != null ? _coreController.State : BossCoreState.Locked),
            CurrentCoreHealth = _phaseController != null ? _phaseController.CurrentCoreHealth : 0,
            CoreHitCount = _phaseController != null ? _phaseController.CoreHitCount : 0,
            PendingCorePoint = _dualCoreController != null ? _dualCoreController.PendingPointId : -1,
            IsStunned = _stunController != null && _stunController.IsStunned,
            IsDefeated = _defeatController != null && _defeatController.IsDefeated,
            IsExitDoorUnlocked = _defeatController != null && _defeatController.IsExitDoorUnlocked
        };

        _attackSnapshot.Value = BuildServerAttackSnapshot();
        SynchronizeStateList(_runeStates, _runes.Length, index => (byte)_runes[index].State);
        SynchronizeStateList(_sealStates, _seals.Length, index => (byte)_seals[index].State);
        SynchronizeStateList(_floorTileStates, _floorTiles.Length, index => (byte)_floorTiles[index].State);
    }

    private BossAttackNetworkSnapshot BuildServerAttackSnapshot()
    {
        BossNetworkAttackType attackType = ResolveActiveAttackType();
        if (attackType != _serverAttackType)
        {
            _serverAttackType = attackType;
            _serverAttackStartedAt = attackType == BossNetworkAttackType.None
                ? 0d
                : NetworkManager.ServerTime.Time;
        }

        Vector3 directionA = _arenaReferences != null ? _arenaReferences.ShockwaveDirection : Vector3.forward;
        Vector3 directionB = Vector3.zero;
        float telegraphDuration = 0f;
        float impactReturnDuration = 0f;

        switch (attackType)
        {
            case BossNetworkAttackType.PawSlam:
                telegraphDuration = _pawSlamAttack.TelegraphDuration;
                impactReturnDuration = _pawSlamAttack.ImpactReturnDuration;
                break;
            case BossNetworkAttackType.TargetSlam:
            case BossNetworkAttackType.DiagonalSlam:
                directionA = _targetSlamAttack.CurrentTelegraphDirection;
                telegraphDuration = _targetSlamAttack.TelegraphDuration;
                impactReturnDuration = _targetSlamAttack.ImpactReturnDuration;
                break;
            case BossNetworkAttackType.DoublePaw:
                directionA = _doublePawAttack.FirstTelegraphDirection;
                directionB = _doublePawAttack.SecondTelegraphDirection;
                telegraphDuration = _doublePawAttack.TelegraphDuration;
                break;
            case BossNetworkAttackType.Earthquake:
                telegraphDuration = _earthquakeAttack.TelegraphDuration;
                break;
        }

        return new BossAttackNetworkSnapshot
        {
            AttackType = (int)attackType,
            DirectionA = directionA,
            DirectionB = directionB,
            TelegraphDuration = telegraphDuration,
            ImpactReturnDuration = impactReturnDuration,
            StartedAtServerTime = _serverAttackStartedAt
        };
    }

    private BossNetworkAttackType ResolveActiveAttackType()
    {
        if (_doublePawAttack != null && _doublePawAttack.IsRunning)
            return BossNetworkAttackType.DoublePaw;
        if (_earthquakeAttack != null && _earthquakeAttack.IsRunning)
            return BossNetworkAttackType.Earthquake;
        if (_targetSlamAttack != null && _targetSlamAttack.IsRunning)
            return _targetSlamAttack.IsDiagonal
                ? BossNetworkAttackType.DiagonalSlam
                : BossNetworkAttackType.TargetSlam;
        if (_pawSlamAttack != null && _pawSlamAttack.IsRunning)
            return BossNetworkAttackType.PawSlam;
        return BossNetworkAttackType.None;
    }

    private void HandleFightSnapshotChanged(BossFightNetworkSnapshot previous, BossFightNetworkSnapshot current)
    {
        if (!IsServer) ApplyFightSnapshot(current);
    }

    private void ApplyAllReplicatedState()
    {
        ApplyFightSnapshot(_fightSnapshot.Value);
        ApplyRuneStates();
        ApplySealStates();
        ApplyFloorTileStates();
    }

    private void ApplyFightSnapshot(BossFightNetworkSnapshot snapshot)
    {
        CacheArenaComponents();
        Transform target = ResolveTarget(snapshot.TargetNetworkObjectId);

        _phaseController?.ApplyNetworkState(
            (BossCombatPhase)snapshot.Phase,
            snapshot.CurrentCoreHealth,
            snapshot.CoreHitCount);
        _bossController?.ApplyNetworkState((BossState)snapshot.BossState, target);
        _stunController?.ApplyNetworkState(snapshot.IsStunned);
        _coreController?.ApplyNetworkState((BossCoreState)snapshot.CoreState);
        _dualCoreController?.ApplyNetworkPendingPoint(snapshot.PendingCorePoint);
        _defeatController?.ApplyNetworkState(snapshot.IsDefeated, snapshot.IsExitDoorUnlocked);
    }

    private void ApplyReplicatedTargetIfAvailable()
    {
        ulong targetId = _fightSnapshot.Value.TargetNetworkObjectId;
        if (targetId == NoNetworkObject) return;

        Transform target = ResolveTarget(targetId);
        if (target != null && (_targetIndicator == null || _targetIndicator.CurrentTarget != target))
            _bossController?.ApplyNetworkState((BossState)_fightSnapshot.Value.BossState, target);
    }

    private void UpdateClientAttackVisual()
    {
        BossAttackNetworkSnapshot snapshot = _attackSnapshot.Value;
        BossNetworkAttackType attackType = (BossNetworkAttackType)snapshot.AttackType;
        if (attackType != _clientAttackType)
        {
            _floorPatternController?.ClearAttackTelegraphs();
            if (_clientAttackType != BossNetworkAttackType.None) _animationController?.ResetPose();

            _clientAttackType = attackType;
            _clientAttackPoseReset = false;
            if (attackType != BossNetworkAttackType.None) _animationController?.PlayPawSlam();
        }

        if (attackType == BossNetworkAttackType.None) return;

        float elapsed = (float)Math.Max(0d, NetworkManager.ServerTime.Time - snapshot.StartedAtServerTime);
        bool isTelegraphing = elapsed < snapshot.TelegraphDuration;
        if (isTelegraphing)
        {
            ShowClientTelegraph(attackType, snapshot);
            if (_animationController != null && !_animationController.UsesAuthoredPawSlam)
                _animationController.SetTelegraphProgress(elapsed / Mathf.Max(0.01f, snapshot.TelegraphDuration));
            return;
        }

        _floorPatternController?.ClearAttackTelegraphs();
        bool usesDescent = attackType is BossNetworkAttackType.PawSlam or
            BossNetworkAttackType.TargetSlam or BossNetworkAttackType.DiagonalSlam;
        float descentElapsed = elapsed - snapshot.TelegraphDuration;
        if (usesDescent && descentElapsed < snapshot.ImpactReturnDuration)
        {
            if (_animationController != null && !_animationController.UsesAuthoredPawSlam)
                _animationController.SetSlamDescentProgress(
                    descentElapsed / Mathf.Max(0.01f, snapshot.ImpactReturnDuration));
            return;
        }

        if (_clientAttackPoseReset) return;
        _animationController?.ResetPose();
        _clientAttackPoseReset = true;
    }

    private void ShowClientTelegraph(BossNetworkAttackType attackType, BossAttackNetworkSnapshot snapshot)
    {
        const float RefreshDuration = 0.1f;
        switch (attackType)
        {
            case BossNetworkAttackType.DoublePaw:
                _floorPatternController?.ShowDoubleTelegraph(
                    snapshot.DirectionA,
                    snapshot.DirectionB,
                    RefreshDuration);
                break;
            case BossNetworkAttackType.Earthquake:
                _floorPatternController?.ShowEarthquakeTelegraph(RefreshDuration);
                break;
            default:
                _floorPatternController?.ShowTargetTelegraph(snapshot.DirectionA, RefreshDuration);
                break;
        }
    }

    private void HandleServerShockwaveSpawned(ShockwaveSpawnInfo spawnInfo)
    {
        if (!IsServer) return;
        SpawnShockwaveRpc(
            spawnInfo.Position,
            spawnInfo.Direction,
            spawnInfo.Speed,
            spawnInfo.Width,
            spawnInfo.MaxRange);
    }

    private void HandleRuneListChanged(NetworkListEvent<byte> changeEvent)
    {
        if (!IsServer) ApplyRuneStates();
    }

    private void HandleSealListChanged(NetworkListEvent<byte> changeEvent)
    {
        if (!IsServer) ApplySealStates();
    }

    private void HandleFloorTileListChanged(NetworkListEvent<byte> changeEvent)
    {
        if (!IsServer) ApplyFloorTileStates();
    }

    private void ApplyRuneStates()
    {
        int count = Mathf.Min(_runes.Length, _runeStates.Count);
        for (int index = 0; index < count; index++)
            _runes[index]?.ApplyNetworkState((RuneState)_runeStates[index]);
    }

    private void ApplySealStates()
    {
        int count = Mathf.Min(_seals.Length, _sealStates.Count);
        for (int index = 0; index < count; index++)
            _seals[index]?.ApplyNetworkState((SealState)_sealStates[index]);
    }

    private void ApplyFloorTileStates()
    {
        int count = Mathf.Min(_floorTiles.Length, _floorTileStates.Count);
        for (int index = 0; index < count; index++)
            _floorTiles[index]?.ApplyNetworkState((FloorTileState)_floorTileStates[index]);
    }

    private void DisableClientGameplaySimulation()
    {
        SetEnabled(_phaseController, false);
        SetEnabled(_arenaReferences.GetComponent<BossAttackSequence>(), false);
        SetEnabled(_pawSlamAttack, false);
        SetEnabled(_targetSlamAttack, false);
        SetEnabled(_doublePawAttack, false);
        SetEnabled(_earthquakeAttack, false);
        SetEnabled(_stunController, false);
        SetEnabled(_coreController, false);
        SetEnabled(_arenaReferences.GetComponent<DualRuneChallengeController>(), false);
        SetEnabled(_dualCoreController, false);
    }

    private void CacheArenaComponents()
    {
        if (_arenaReferences == null)
            _arenaReferences = FindFirstObjectByType<BossArenaReferences>();
        if (_arenaReferences == null) return;

        GameObject arena = _arenaReferences.gameObject;
        _bossController ??= arena.GetComponent<BossController>();
        _targetIndicator ??= arena.GetComponent<BossTargetIndicator>();
        _pawSlamAttack ??= arena.GetComponent<BossPawSlamAttack>();
        _targetSlamAttack ??= arena.GetComponent<BossTargetSlamAttack>();
        _doublePawAttack ??= arena.GetComponent<BossDoublePawAttack>();
        _earthquakeAttack ??= arena.GetComponent<BossEarthquakeAttack>();
        _animationController ??= arena.GetComponent<BossAnimationController>();
        _floorPatternController ??= arena.GetComponent<FloorPatternController>();
        _runeManager ??= arena.GetComponent<RuneManager>();
        _sealManager ??= arena.GetComponent<SealManager>();
        _stunController ??= arena.GetComponent<BossStunController>();
        _coreController ??= arena.GetComponent<BossCoreController>();
        _dualCoreController ??= arena.GetComponent<DualCoreInteractionController>();
        _phaseController ??= arena.GetComponent<BossPhaseController>();
        _floorTileManager ??= arena.GetComponent<FloorTileManager>();
        _defeatController ??= arena.GetComponent<BossDefeatController>();
        _runes = _runeManager != null ? _runeManager.Runes : Array.Empty<RuneController>();
        _seals = _sealManager != null ? _sealManager.Seals : Array.Empty<SealController>();
        _floorTiles = _floorTileManager != null && _floorTileManager.Tiles != null
            ? _floorTileManager.Tiles
            : Array.Empty<FloorTile>();
    }

    private ulong ResolveTargetNetworkObjectId()
    {
        Transform target = _targetIndicator != null ? _targetIndicator.CurrentTarget : null;
        NetworkObject targetNetworkObject = target != null ? target.GetComponentInParent<NetworkObject>() : null;
        return targetNetworkObject != null && targetNetworkObject.IsSpawned
            ? targetNetworkObject.NetworkObjectId
            : NoNetworkObject;
    }

    private Transform ResolveTarget(ulong networkObjectId)
    {
        if (networkObjectId == NoNetworkObject || NetworkManager == null) return null;
        return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject target)
            ? target.transform
            : null;
    }

    private CoreInteractionPoint FindCorePoint(CorePointId pointId)
    {
        if (_arenaReferences == null) return null;
        foreach (CoreInteractionPoint point in _arenaReferences.GetComponentsInChildren<CoreInteractionPoint>(true))
            if (point.PointId == pointId) return point;
        return null;
    }

    private bool IsPlayerNearCorePoint(ulong clientId, CoreInteractionPoint point)
    {
        if (NetworkManager == null ||
            !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
            client.PlayerObject == null)
            return false;

        return Vector3.Distance(client.PlayerObject.transform.position, point.transform.position) <=
               point.ServerInteractionDistance;
    }

    private static void SynchronizeStateList(
        NetworkList<byte> networkList,
        int stateCount,
        Func<int, byte> readState)
    {
        if (networkList.Count != stateCount)
        {
            networkList.Clear();
            for (int index = 0; index < stateCount; index++) networkList.Add(readState(index));
            return;
        }

        for (int index = 0; index < stateCount; index++)
        {
            byte state = readState(index);
            if (networkList[index] != state) networkList[index] = state;
        }
    }

    private static void SetEnabled(Behaviour behaviour, bool isEnabled)
    {
        if (behaviour != null) behaviour.enabled = isEnabled;
    }
}

/// <summary>Long-lived boss state replicated to every connected peer.</summary>
public struct BossFightNetworkSnapshot : INetworkSerializable, IEquatable<BossFightNetworkSnapshot>
{
    public int BossState;
    public ulong TargetNetworkObjectId;
    public int Phase;
    public int CoreState;
    public int CurrentCoreHealth;
    public int CoreHitCount;
    public int PendingCorePoint;
    public bool IsStunned;
    public bool IsDefeated;
    public bool IsExitDoorUnlocked;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref BossState);
        serializer.SerializeValue(ref TargetNetworkObjectId);
        serializer.SerializeValue(ref Phase);
        serializer.SerializeValue(ref CoreState);
        serializer.SerializeValue(ref CurrentCoreHealth);
        serializer.SerializeValue(ref CoreHitCount);
        serializer.SerializeValue(ref PendingCorePoint);
        serializer.SerializeValue(ref IsStunned);
        serializer.SerializeValue(ref IsDefeated);
        serializer.SerializeValue(ref IsExitDoorUnlocked);
    }

    public bool Equals(BossFightNetworkSnapshot other) =>
        BossState == other.BossState &&
        TargetNetworkObjectId == other.TargetNetworkObjectId &&
        Phase == other.Phase &&
        CoreState == other.CoreState &&
        CurrentCoreHealth == other.CurrentCoreHealth &&
        CoreHitCount == other.CoreHitCount &&
        PendingCorePoint == other.PendingCorePoint &&
        IsStunned == other.IsStunned &&
        IsDefeated == other.IsDefeated &&
        IsExitDoorUnlocked == other.IsExitDoorUnlocked;
}

/// <summary>Short-lived attack cue used to present the same telegraph and pose on Client.</summary>
public struct BossAttackNetworkSnapshot : INetworkSerializable, IEquatable<BossAttackNetworkSnapshot>
{
    public int AttackType;
    public Vector3 DirectionA;
    public Vector3 DirectionB;
    public float TelegraphDuration;
    public float ImpactReturnDuration;
    public double StartedAtServerTime;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref AttackType);
        serializer.SerializeValue(ref DirectionA);
        serializer.SerializeValue(ref DirectionB);
        serializer.SerializeValue(ref TelegraphDuration);
        serializer.SerializeValue(ref ImpactReturnDuration);
        serializer.SerializeValue(ref StartedAtServerTime);
    }

    public bool Equals(BossAttackNetworkSnapshot other) =>
        AttackType == other.AttackType &&
        DirectionA == other.DirectionA &&
        DirectionB == other.DirectionB &&
        Mathf.Approximately(TelegraphDuration, other.TelegraphDuration) &&
        Mathf.Approximately(ImpactReturnDuration, other.ImpactReturnDuration) &&
        StartedAtServerTime.Equals(other.StartedAtServerTime);
}

/// <summary>Attack categories required to reproduce Phase 1-3 warnings on remote peers.</summary>
public enum BossNetworkAttackType
{
    None,
    PawSlam,
    TargetSlam,
    DiagonalSlam,
    DoublePaw,
    Earthquake
}
