using UnityEngine;

/// <summary>Shows readable red floor telegraphs for straight, double-paw, and Earthquake attacks.</summary>
public sealed class FloorPatternController : MonoBehaviour
{
    [Tooltip("Chieu dai duong do canh bao tu Boss vao arena.")]
    [SerializeField, Min(1f)] private float _telegraphLength = 28f;
    [Tooltip("Be rong duong do canh bao tren mat san.")]
    [SerializeField, Min(0.02f)] private float _telegraphWidth = 0.28f;
    [Tooltip("Do cao cua duong do so voi ShockwaveOrigin de tranh bi san che.")]
    [SerializeField, Min(0f)] private float _heightOffset = 0.3f;
    [Tooltip("Mau duong canh bao truoc khi Boss dap.")]
    [SerializeField] private Color _telegraphColor = new(1f, 0.03f, 0.02f, 1f);

    private BossController _bossController;
    private BossArenaReferences _arenaReferences;
    private LineRenderer _telegraphLine;
    private LineRenderer _secondaryTelegraphLine;
    private LineRenderer _earthquakeRing;
    private Vector3 _targetTelegraphDirection;
    private Vector3 _doubleLeftDirection;
    private Vector3 _doubleRightDirection;
    private float _targetTelegraphUntil;
    private float _doubleTelegraphUntil;
    private float _earthquakeTelegraphUntil;

    private void Awake()
    {
        _bossController = GetComponent<BossController>();
        _arenaReferences = GetComponent<BossArenaReferences>();
        CreateTelegraphLine();
        SetTelegraphVisible(false);
    }

    private void LateUpdate()
    {
        bool hasTargetTelegraph = Time.time < _targetTelegraphUntil;
        bool hasDoubleTelegraph = Time.time < _doubleTelegraphUntil;
        bool hasEarthquakeTelegraph = Time.time < _earthquakeTelegraphUntil;
        bool shouldShow = _arenaReferences != null &&
                          (hasTargetTelegraph || hasDoubleTelegraph || hasEarthquakeTelegraph ||
                           (_bossController != null && _bossController.CurrentState == BossState.Telegraph));
        SetTelegraphVisible(shouldShow);
        if (_secondaryTelegraphLine != null) _secondaryTelegraphLine.enabled = hasDoubleTelegraph;
        if (_earthquakeRing != null) _earthquakeRing.enabled = hasEarthquakeTelegraph;
        if (!shouldShow || _arenaReferences == null) return;

        Vector3 origin = _arenaReferences.ShockwaveOrigin.position + Vector3.up * _heightOffset;
        Vector3 direction = hasTargetTelegraph || hasDoubleTelegraph
            ? _targetTelegraphDirection
            : _arenaReferences.ShockwaveDirection;
        _telegraphLine.SetPosition(0, origin);
        _telegraphLine.SetPosition(1, origin + direction * _telegraphLength);

        if (hasDoubleTelegraph)
        {
            _telegraphLine.SetPosition(1, origin + _doubleLeftDirection * _telegraphLength);
            _secondaryTelegraphLine.SetPosition(0, origin);
            _secondaryTelegraphLine.SetPosition(1, origin + _doubleRightDirection * _telegraphLength);
        }

        if (hasEarthquakeTelegraph) UpdateEarthquakeRing();
    }

    /// <summary>Shows a temporary target or diagonal red telegraph used by the Phase 2 Target Slam.</summary>
    public void ShowTargetTelegraph(Vector3 direction, float duration)
    {
        Vector3 flattenedDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (flattenedDirection.sqrMagnitude < 0.0001f) return;

        _targetTelegraphDirection = flattenedDirection;
        _targetTelegraphUntil = Time.time + Mathf.Max(0f, duration);
    }

    /// <summary>Shows the two warning lanes that lead from the Boss to the two current players.</summary>
    public void ShowDoubleTelegraph(Vector3 firstPlayerDirection, Vector3 secondPlayerDirection, float duration)
    {
        Vector3 flattenedFirstDirection = Vector3.ProjectOnPlane(firstPlayerDirection, Vector3.up).normalized;
        Vector3 flattenedSecondDirection = Vector3.ProjectOnPlane(secondPlayerDirection, Vector3.up).normalized;
        if (flattenedFirstDirection.sqrMagnitude < 0.0001f || flattenedSecondDirection.sqrMagnitude < 0.0001f) return;

        _doubleLeftDirection = flattenedFirstDirection;
        _doubleRightDirection = flattenedSecondDirection;
        _targetTelegraphDirection = _doubleLeftDirection;
        _doubleTelegraphUntil = Time.time + Mathf.Max(0f, duration);
    }

    /// <summary>Immediately hides temporary target, double-paw, and Earthquake warning visuals at attack impact.</summary>
    public void ClearAttackTelegraphs()
    {
        _targetTelegraphUntil = 0f;
        _doubleTelegraphUntil = 0f;
        _earthquakeTelegraphUntil = 0f;
        SetTelegraphVisible(false);
        if (_secondaryTelegraphLine != null) _secondaryTelegraphLine.enabled = false;
        if (_earthquakeRing != null) _earthquakeRing.enabled = false;
    }

    /// <summary>Shows a wider red centre-line warning for the Phase 3 Earthquake impact.</summary>
    public void ShowEarthquakeTelegraph(float duration)
    {
        _earthquakeTelegraphUntil = Time.time + Mathf.Max(0f, duration);
    }

    private void CreateTelegraphLine()
    {
        GameObject lineObject = new("Straight Shockwave Telegraph");
        lineObject.transform.SetParent(transform, false);
        _telegraphLine = lineObject.AddComponent<LineRenderer>();
        _telegraphLine.useWorldSpace = true;
        ConfigureGroundAlignedLine(lineObject.transform, _telegraphLine);
        _telegraphLine.positionCount = 2;
        _telegraphLine.startWidth = _telegraphWidth;
        _telegraphLine.endWidth = _telegraphWidth;
        _telegraphLine.startColor = _telegraphColor;
        _telegraphLine.endColor = _telegraphColor;

        Shader lineShader = Shader.Find("Sprites/Default");
        if (lineShader == null) lineShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (lineShader == null) return;

        _telegraphLine.material = new Material(lineShader);
        _telegraphLine.material.color = _telegraphColor;
        _secondaryTelegraphLine = CreateAdditionalLine("Double Paw Telegraph", lineShader, 2);
        _earthquakeRing = CreateAdditionalLine("Earthquake Outer Ring Telegraph", lineShader, 33);
        _earthquakeRing.loop = true;
    }

    private LineRenderer CreateAdditionalLine(string objectName, Shader shader, int positionCount)
    {
        GameObject lineObject = new(objectName);
        lineObject.transform.SetParent(transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        ConfigureGroundAlignedLine(lineObject.transform, line);
        line.positionCount = positionCount;
        line.startWidth = _telegraphWidth;
        line.endWidth = _telegraphWidth;
        line.startColor = _telegraphColor;
        line.endColor = _telegraphColor;
        line.material = new Material(shader);
        line.material.color = _telegraphColor;
        line.enabled = false;
        return line;
    }

    /// <summary>Keeps the warning ribbon flat on the floor instead of billboarding toward the camera.</summary>
    private static void ConfigureGroundAlignedLine(Transform lineTransform, LineRenderer line)
    {
        // A LineRenderer using View alignment changes its visible width with camera angle.
        // Aligning its local Z axis with world-up fixes the ribbon to the XZ floor plane.
        line.alignment = LineAlignment.TransformZ;
        lineTransform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
    }

    private void UpdateEarthquakeRing()
    {
        if (_earthquakeRing == null || _arenaReferences == null) return;

        FloorTileManager tileManager = GetComponent<FloorTileManager>();
        FloorTile[] tiles = tileManager != null ? tileManager.Tiles : null;
        float outerRadius = 8f;
        if (tiles != null)
        {
            foreach (FloorTile tile in tiles)
                if (tile != null) outerRadius = Mathf.Max(outerRadius, Vector3.Distance(transform.position, tile.WorldCenter));
        }

        Vector3 centre = transform.position + Vector3.up * _heightOffset;
        for (int index = 0; index < _earthquakeRing.positionCount; index++)
        {
            float angle = index / (float)(_earthquakeRing.positionCount - 1) * Mathf.PI * 2f;
            Vector3 point = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * outerRadius * 0.72f;
            _earthquakeRing.SetPosition(index, point);
        }
    }

    private void SetTelegraphVisible(bool isVisible)
    {
        if (_telegraphLine != null) _telegraphLine.enabled = isVisible;
    }
}
