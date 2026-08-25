using UnityEngine;

/// <summary>
/// Presents interactive quest destinations with a see-through outline.
/// Point destinations are presented by the screen-space marker owned by <see cref="QuestHUD"/>.
/// </summary>
public sealed class QuestWorldMarker : MonoBehaviour
{
    [Header("Appearance")]
    [SerializeField] private Color markerColor = new(1f, 0.82f, 0.15f, 0.95f);
    [SerializeField, Range(0f, 10f)] private float outlineWidth = 5f;

    private Transform _target;
    private Outline _activeOutline;
    private bool _ownsOutline;
    private bool _previousOutlineEnabled;
    private Outline.Mode _previousOutlineMode;
    private Color _previousOutlineColor;
    private float _previousOutlineWidth;
    private QuestMarkerStyle _activeStyle;

    private void OnDisable() => Clear();

    /// <summary>Updates the presentation for a newly active route step.</summary>
    public void SetTarget(QuestRouteStep step)
    {
        Transform destination = step?.MarkerTarget;
        QuestMarkerStyle style = ResolveMarkerStyle(step, destination);
        if (_target == destination && _activeStyle == style)
            return;

        Clear();
        _target = destination;
        if (_target == null)
            return;

        _activeStyle = style;
        if (style == QuestMarkerStyle.Outline)
            ShowOutline(_target);
    }

    /// <summary>Removes the current marker and restores any pre-existing outline settings.</summary>
    public void Clear()
    {
        if (_activeOutline != null)
        {
            if (_ownsOutline)
                Destroy(_activeOutline);
            else
            {
                _activeOutline.OutlineMode = _previousOutlineMode;
                _activeOutline.OutlineColor = _previousOutlineColor;
                _activeOutline.OutlineWidth = _previousOutlineWidth;
                _activeOutline.enabled = _previousOutlineEnabled;
            }
        }

        _activeOutline = null;
        _ownsOutline = false;
        _target = null;
        _activeStyle = QuestMarkerStyle.Automatic;
    }

    private static QuestMarkerStyle ResolveMarkerStyle(QuestRouteStep step, Transform destination)
    {
        if (step == null)
            return QuestMarkerStyle.Orb;

        QuestMarkerStyle style = step.markerStyle == QuestMarkerStyle.Automatic
            ? step.RequiresInteraction ? QuestMarkerStyle.Outline : QuestMarkerStyle.Orb
            : step.markerStyle;

        return style == QuestMarkerStyle.Outline && !HasRenderer(destination)
            ? QuestMarkerStyle.Orb
            : style;
    }

    private static bool HasRenderer(Component target) => target != null && target.GetComponentInChildren<Renderer>(true) != null;

    private void ShowOutline(Transform destination)
    {
        _activeOutline = destination.GetComponent<Outline>();
        if (_activeOutline == null)
            _activeOutline = destination.GetComponentInChildren<Outline>();

        _ownsOutline = _activeOutline == null;
        if (_ownsOutline)
        {
            _activeOutline = destination.gameObject.AddComponent<Outline>();
        }
        else
        {
            _previousOutlineEnabled = _activeOutline.enabled;
            _previousOutlineMode = _activeOutline.OutlineMode;
            _previousOutlineColor = _activeOutline.OutlineColor;
            _previousOutlineWidth = _activeOutline.OutlineWidth;
        }

        _activeOutline.OutlineMode = Outline.Mode.OutlineAll;
        _activeOutline.OutlineColor = markerColor;
        _activeOutline.OutlineWidth = outlineWidth;
        _activeOutline.enabled = true;
    }

}
