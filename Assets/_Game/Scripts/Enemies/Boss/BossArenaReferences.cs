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
