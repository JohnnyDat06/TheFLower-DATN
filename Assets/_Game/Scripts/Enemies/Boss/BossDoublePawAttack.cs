using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Runs the Phase 3 Double Paw Slam and directs one Shockwave at each valid player.</summary>
public sealed class BossDoublePawAttack : MonoBehaviour
{
    [Tooltip("Thoi gian telegraph cho Double Paw Slam.")]
    [SerializeField, Range(0.8f, 1.8f)] private float _telegraphDuration = 1.1f;
    [Tooltip("Van toc hai Shockwave cua Double Paw Slam.")]
    [SerializeField, Min(0.1f)] private float _shockwaveSpeed = 15f;

    private BossAnimationController _animationController;
    private BossArenaReferences _arenaReferences;
    private FloorPatternController _floorPatternController;
    private Coroutine _routine;
    private Vector3 _firstTelegraphDirection;
    private Vector3 _secondTelegraphDirection;

    /// <summary>True while the Double Paw attack owns the combat timeline.</summary>
    public bool IsRunning => _routine != null;

    /// <summary>Current warning direction toward the first valid player.</summary>
    public Vector3 FirstTelegraphDirection => _firstTelegraphDirection;

    /// <summary>Current warning direction toward the second valid player.</summary>
    public Vector3 SecondTelegraphDirection => _secondTelegraphDirection;

    /// <summary>Telegraph duration replicated to remote peers by BossNetworkState.</summary>
    public float TelegraphDuration => _telegraphDuration;

    /// <summary>Starts one Double Paw Slam when no previous instance is running.</summary>
    public bool TryStart()
    {
        if (_routine != null || _arenaReferences == null || _arenaReferences.ShockwaveOrigin == null) return false;
        if (!TryGetTwoPlayers(out Transform firstPlayer, out Transform secondPlayer)) return false;

        _firstTelegraphDirection = DirectionTo(firstPlayer);
        _secondTelegraphDirection = DirectionTo(secondPlayer);
        _routine = StartCoroutine(RunAttack(firstPlayer, secondPlayer));
        return true;
    }

    /// <summary>Cancels the current Double Paw routine before the server resets the encounter.</summary>
    public void ResetEncounterState()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        _firstTelegraphDirection = Vector3.zero;
        _secondTelegraphDirection = Vector3.zero;
        _floorPatternController?.ClearAttackTelegraphs();
        _animationController?.ResetPose();
        enabled = true;
    }

    private void Awake()
    {
        _animationController = GetComponent<BossAnimationController>();
        _arenaReferences = GetComponent<BossArenaReferences>();
        _floorPatternController = GetComponent<FloorPatternController>();
    }

    private IEnumerator RunAttack(Transform firstPlayer, Transform secondPlayer)
    {
        _animationController?.PlayPawSlam();

        float elapsed = 0f;
        Vector3 firstDirection = DirectionTo(firstPlayer);
        Vector3 secondDirection = DirectionTo(secondPlayer);
        while (elapsed < _telegraphDuration)
        {
            elapsed += Time.deltaTime;
            firstDirection = DirectionTo(firstPlayer);
            secondDirection = DirectionTo(secondPlayer);
            _firstTelegraphDirection = firstDirection;
            _secondTelegraphDirection = secondDirection;
            _floorPatternController?.ShowDoubleTelegraph(firstDirection, secondDirection, 0.1f);
            if (_animationController != null && !_animationController.UsesAuthoredPawSlam)
                _animationController.SetTelegraphProgress(elapsed / _telegraphDuration);
            yield return null;
        }

        _animationController?.ResetPose();
        _floorPatternController?.ClearAttackTelegraphs();
        yield return null;

        // The directions are sampled after the red warning is removed, so each wave follows
        // the player's final telegraphed position instead of a stale position from attack start.
        firstDirection = DirectionTo(firstPlayer);
        secondDirection = DirectionTo(secondPlayer);
        _firstTelegraphDirection = firstDirection;
        _secondTelegraphDirection = secondDirection;
        SpawnAtBothPlayers(firstDirection, secondDirection);
        Debug.Log("[BossDoublePawAttack] Double Paw impact.", this);
        _routine = null;
    }

    private void SpawnAtBothPlayers(Vector3 firstDirection, Vector3 secondDirection)
    {
        ShockwaveController.Spawn(_arenaReferences.ShockwaveOrigin, firstDirection, _shockwaveSpeed, 4f, 28f);
        ShockwaveController.Spawn(_arenaReferences.ShockwaveOrigin, secondDirection, _shockwaveSpeed, 4f, 28f);
    }

    private static bool TryGetTwoPlayers(out Transform firstPlayer, out Transform secondPlayer)
    {
        firstPlayer = null;
        secondPlayer = null;
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null) return false;

        List<Transform> playerTransforms = new(2);
        foreach (NetworkClient client in networkManager.ConnectedClientsList)
        {
            NetworkObject player = client.PlayerObject;
            if (player == null || !player.IsSpawned || !player.gameObject.activeInHierarchy) continue;
            if (player.TryGetComponent(out PlayerHealth health) && health.IsDead) continue;

            playerTransforms.Add(player.transform);
            if (playerTransforms.Count == 2) break;
        }

        if (playerTransforms.Count < 2) return false;
        firstPlayer = playerTransforms[0];
        secondPlayer = playerTransforms[1];
        return true;
    }

    private Vector3 DirectionTo(Transform player)
    {
        if (player == null) return Vector3.zero;
        return Vector3.ProjectOnPlane(
            player.position - _arenaReferences.ShockwaveOrigin.position,
            Vector3.up).normalized;
    }
}
