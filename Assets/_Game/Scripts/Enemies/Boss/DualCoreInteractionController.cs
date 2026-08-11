using Unity.Netcode;
using UnityEngine;

/// <summary>Validates that two different players activate different Core points within one sync window.</summary>
public sealed class DualCoreInteractionController : MonoBehaviour
{
    [Tooltip("Thoi gian toi da giua hai kich hoat Core hop le.")]
    [SerializeField, Range(1f, 2f)] private float _syncWindow = 1.5f;
    [Tooltip("Core chi nhan mot hit sau khi hai diem duoc kich hoat dung dieu kien.")]
    [SerializeField] private BossCoreController _coreController;

    private CoreInteractionPoint _firstPoint;
    private ulong _firstPlayerId;
    private float _firstActivationTime;
    private int _replicatedPendingPoint = -1;

    /// <summary>True while the Core is Exposed, allowing point markers to be visible before interaction.</summary>
    public bool IsCoreExposed => _coreController != null && _coreController.CanAcceptDualActivation;

    /// <summary>Pending Core point identifier, or -1 when no activation is waiting.</summary>
    public int PendingPointId => _firstPoint != null ? (int)_firstPoint.PointId : -1;

    /// <summary>Returns true when the supplied point may be used during the exposed Core window.</summary>
    public bool CanActivatePoint(CoreInteractionPoint point)
    {
        if (point == null || _coreController == null || !_coreController.CanAcceptDualActivation)
            return false;

        if (!IsServerAuthority()) return _replicatedPendingPoint != (int)point.PointId;

        ResetExpiredAttempt();
        return _firstPoint == null || _firstPoint != point;
    }

    /// <summary>Returns true while this exact point is waiting for the other player to activate the other point.</summary>
    public bool IsPointPending(CoreInteractionPoint point) => IsServerAuthority()
        ? _firstPoint == point
        : point != null && _replicatedPendingPoint == (int)point.PointId;

    /// <summary>Copies the Host-owned pending Core point for Client marker feedback.</summary>
    public void ApplyNetworkPendingPoint(int pointId)
    {
        _replicatedPendingPoint = pointId is >= 0 and <= 1 ? pointId : -1;
    }

    /// <summary>Records a player activation and registers exactly one Core Hit only on a valid dual activation.</summary>
    public bool TryActivatePoint(CoreInteractionPoint point, ulong playerId)
    {
        if (!CanActivatePoint(point)) return false;

        if (_firstPoint == null)
        {
            _firstPoint = point;
            _firstPlayerId = playerId;
            _firstActivationTime = Time.time;
            Debug.Log($"[DualCoreInteractionController] Player {playerId} activated Core Point {point.PointId}.", point);
            return false;
        }

        if (_firstPlayerId == playerId)
        {
            Debug.LogWarning("[DualCoreInteractionController] One player cannot activate both Core points.", point);
            return false;
        }

        bool didHitCore = _coreController.TryRegisterCoreHit();
        ResetAttempt();
        if (didHitCore)
            Debug.Log("[DualCoreInteractionController] Dual activation succeeded: Core Hit.", this);
        return didHitCore;
    }

    private void Update()
    {
        ResetExpiredAttempt();
        if (Input.GetKeyDown(KeyCode.J))
        {
            DebugDualActivate();
        }
    }

    private void Awake()
    {
        if (_coreController == null) _coreController = GetComponent<BossCoreController>();
    }

    private void ResetExpiredAttempt()
    {
        if (_firstPoint == null || Time.time - _firstActivationTime <= _syncWindow) return;

        Debug.Log("[DualCoreInteractionController] Core activation sync window expired.", this);
        ResetAttempt();
    }

    private void ResetAttempt()
    {
        _firstPoint = null;
        _firstPlayerId = 0;
        _firstActivationTime = 0f;
    }

    [ContextMenu("Debug/Activate A - Player 1")]
    private void DebugActivateAAsPlayerOne() => DebugActivate(CorePointId.A, 1001UL);

    [ContextMenu("Debug/Activate B - Player 1")]
    private void DebugActivateBAsPlayerOne() => DebugActivate(CorePointId.B, 1001UL);

    [ContextMenu("Debug/Activate A - Player 2")]
    private void DebugActivateAAsPlayerTwo() => DebugActivate(CorePointId.A, 1002UL);

    [ContextMenu("Debug/Activate B - Player 2")]
    private void DebugActivateBAsPlayerTwo() => DebugActivate(CorePointId.B, 1002UL);

    [ContextMenu("Debug/Dual Activate A P1 + B P2")]
    private void DebugDualActivate()
    {
        DebugActivate(CorePointId.A, 1001UL);
        DebugActivate(CorePointId.B, 1002UL);
    }

    [ContextMenu("Debug/Reset Core Attempt")]
    private void DebugResetAttempt()
    {
        ResetAttempt();
        Debug.Log("[DualCoreInteractionController] Debug Core activation attempt reset.", this);
    }

    private void DebugActivate(CorePointId pointId, ulong playerId)
    {
        CoreInteractionPoint point = FindPoint(pointId);
        if (point == null)
        {
            Debug.LogError($"[DualCoreInteractionController] Core Point {pointId} is missing from the arena.", this);
            return;
        }

        if (!TryActivatePoint(point, playerId))
            Debug.Log($"[DualCoreInteractionController] Debug activation for Point {pointId}, Player {playerId} did not create a Core Hit.", point);
    }

    private CoreInteractionPoint FindPoint(CorePointId pointId)
    {
        foreach (CoreInteractionPoint point in GetComponentsInChildren<CoreInteractionPoint>(true))
            if (point.PointId == pointId) return point;

        return null;
    }

    private static bool IsServerAuthority() =>
        NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
}
