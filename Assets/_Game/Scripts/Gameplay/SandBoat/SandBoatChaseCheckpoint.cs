using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Restores the Sand Boat chase to its authored start after a Phase 12 failure.
/// Player seating is delegated to <see cref="SandBoatBoarding"/> so the existing
/// NGO teleport path remains the only player-position authority.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandBoatChaseCheckpoint : MonoBehaviour
{
    [Header("Sand Boat References")]
    [SerializeField, Tooltip("Route movement restored to its configured Start Progress on retry.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Lateral offset component reset to the center of the route.")]
    private SandBoatHorizontalOffset _horizontalOffset;
    [SerializeField, Tooltip("P1 steering state cleared before chase input is resumed.")]
    private SandBoatSteering _steering;
    [SerializeField, Tooltip("P2 speed and collision recovery state reset to the configured base speed.")]
    private SandBoatSpeedController _speedController;
    [SerializeField, Tooltip("Obstacle cooldown reset so a retry is not blocked by the previous attempt.")]
    private SandBoatCollisionHandler _collisionHandler;
    [SerializeField, Tooltip("Storm distance reset to its configured initial safe value.")]
    private SandstormChaseController _stormLogic;
    [SerializeField, Tooltip("Phase 12 failure gate that requests this checkpoint retry.")]
    private SandBoatChaseFailController _failController;
    [SerializeField, Tooltip("Existing boarding owner that reseats players and resumes chase input through NGO.")]
    private SandBoatBoarding _boarding;

    [Header("Runtime Debug")]
    [SerializeField, Tooltip("True only while a single checkpoint reset transaction is running.")]
    private bool _isResetting;

    /// <summary>True while the current retry transaction is restoring the chase state.</summary>
    public bool IsResetting => _isResetting;

    private void Update()
    {
        if (_isResetting || _failController == null || !_failController.IsReadyForReset)
        {
            return;
        }

        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        StartCoroutine(ResetChaseRoutine());
    }

    private IEnumerator ResetChaseRoutine()
    {
        _isResetting = true;
        _movement?.SetRouteMovementEnabled(false);
        _movement?.ResetMovement();
        _horizontalOffset?.ResetOffset();
        _steering?.ResetSteeringState();
        _speedController?.ResetSpeed();
        _collisionHandler?.ResetCollisionState();
        _stormLogic?.ResetStormDistance();
        _failController?.ResetFailState();

        // Allow the movement pose reset to update the authored seat transforms first.
        yield return null;
        _boarding?.ResumeChaseAfterReset();
        _isResetting = false;
    }
}
