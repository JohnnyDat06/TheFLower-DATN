using UnityEngine;

/// <summary>Shows the Phase 13 straight red floor telegraph only while Paw Slam is charging.</summary>
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
    private Vector3 _targetTelegraphDirection;
    private float _targetTelegraphUntil;

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
        bool shouldShow = _arenaReferences != null &&
                          (hasTargetTelegraph ||
                           (_bossController != null && _bossController.CurrentState == BossState.Telegraph));
        SetTelegraphVisible(shouldShow);
        if (!shouldShow) return;

        Vector3 origin = _arenaReferences.ShockwaveOrigin.position + Vector3.up * _heightOffset;
        Vector3 direction = hasTargetTelegraph
            ? _targetTelegraphDirection
            : _arenaReferences.ShockwaveDirection;
        _telegraphLine.SetPosition(0, origin);
        _telegraphLine.SetPosition(1, origin + direction * _telegraphLength);
    }

    /// <summary>Shows a temporary target or diagonal red telegraph used by the Phase 2 Target Slam.</summary>
    public void ShowTargetTelegraph(Vector3 direction, float duration)
    {
        Vector3 flattenedDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (flattenedDirection.sqrMagnitude < 0.0001f) return;

        _targetTelegraphDirection = flattenedDirection;
        _targetTelegraphUntil = Time.time + Mathf.Max(0f, duration);
    }

    private void CreateTelegraphLine()
    {
        GameObject lineObject = new("Straight Shockwave Telegraph");
        lineObject.transform.SetParent(transform, false);
        _telegraphLine = lineObject.AddComponent<LineRenderer>();
        _telegraphLine.useWorldSpace = true;
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
    }

    private void SetTelegraphVisible(bool isVisible)
    {
        if (_telegraphLine != null) _telegraphLine.enabled = isVisible;
    }
}
