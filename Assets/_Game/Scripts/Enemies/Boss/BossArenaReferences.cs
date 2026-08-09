using UnityEngine;

/// <summary>
/// Holds only the Phase 0 scene placement references for the Cat Sphinx arena.
/// No boss gameplay is implemented here.
/// </summary>
public sealed class BossArenaReferences : MonoBehaviour
{
    [Header("Arena")]
    [SerializeField] private Transform _floorRoot;
    [SerializeField] private Transform _boss;
    [SerializeField] private Transform _playerOneSpawn;
    [SerializeField] private Transform _playerTwoSpawn;

    [Header("Puzzle Placeholders")]
    [SerializeField] private Transform _sealA;
    [SerializeField] private Transform _sealB;
    [SerializeField] private Transform _runeA;
    [SerializeField] private Transform _runeB;
    [SerializeField] private Transform _runeC;
    [SerializeField] private Transform _runeD;
    [SerializeField] private Transform _corePointA;
    [SerializeField] private Transform _corePointB;
    [SerializeField] private Transform _exitDoor;

    [Header("Attack Placement Markers")]
    [SerializeField] private Transform _slamOrigin;
    [SerializeField] private Transform _shockwaveOrigin;
    [SerializeField] private Transform _facingDirection;

    /// <summary>Origin used by the Phase 4 Shockwave prototype.</summary>
    public Transform ShockwaveOrigin => _shockwaveOrigin;

    /// <summary>First authored interaction marker for the future dual-Core mechanic.</summary>
    public Transform CorePointA => _corePointA;

    /// <summary>Second authored interaction marker for the future dual-Core mechanic.</summary>
    public Transform CorePointB => _corePointB;

    /// <summary>World-space midpoint between the two Core markers used for the Phase 9 Core visual.</summary>
    public Vector3 CoreCenter
    {
        get
        {
            if (_corePointA == null || _corePointB == null) return transform.position;
            return Vector3.Lerp(_corePointA.position, _corePointB.position, 0.5f);
        }
    }

    /// <summary>World-space arena direction for Shockwaves, derived from the authored markers.</summary>
    public Vector3 ShockwaveDirection
    {
        get
        {
            if (_shockwaveOrigin == null || _facingDirection == null) return Vector3.zero;

            Vector3 direction = Vector3.ProjectOnPlane(
                _facingDirection.position - _shockwaveOrigin.position,
                Vector3.up);
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
        }
    }

    private void OnDrawGizmos()
    {
        DrawMarker(_playerOneSpawn, Color.blue);
        DrawMarker(_playerTwoSpawn, Color.blue);
        DrawMarker(_sealA, Color.yellow);
        DrawMarker(_sealB, Color.yellow);
        DrawMarker(_runeA, Color.cyan);
        DrawMarker(_runeB, Color.cyan);
        DrawMarker(_runeC, Color.cyan);
        DrawMarker(_runeD, Color.cyan);
        DrawMarker(_corePointA, Color.magenta);
        DrawMarker(_corePointB, Color.magenta);
        DrawMarker(_exitDoor, Color.green);
        DrawMarker(_slamOrigin, Color.red);
        DrawMarker(_shockwaveOrigin, Color.red);

        if (_shockwaveOrigin == null || _facingDirection == null) return;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(_shockwaveOrigin.position, _facingDirection.position);
    }

    private static void DrawMarker(Transform marker, Color color)
    {
        if (marker == null) return;

        Gizmos.color = color;
        Gizmos.DrawWireSphere(marker.position, 0.5f);
    }
}
