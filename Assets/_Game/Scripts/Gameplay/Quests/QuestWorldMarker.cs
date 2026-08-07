using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Presents the current route destination in world space without owning quest state.
/// Interactive destinations use a see-through outline; all other destinations use a marker orb.
/// </summary>
public sealed class QuestWorldMarker : MonoBehaviour
{
    [Header("Appearance")]
    [SerializeField] private Color markerColor = new(1f, 0.82f, 0.15f, 0.95f);
    [SerializeField, Min(0.1f)] private float markerScale = 0.6f;
    [SerializeField] private Vector3 markerOffset = new(0f, 1.25f, 0f);
    [SerializeField, Range(0f, 10f)] private float outlineWidth = 5f;

    private Transform _target;
    private GameObject _markerOrb;
    private Material _markerMaterial;
    private Outline _activeOutline;
    private bool _ownsOutline;
    private bool _previousOutlineEnabled;
    private Outline.Mode _previousOutlineMode;
    private Color _previousOutlineColor;
    private float _previousOutlineWidth;

    private void LateUpdate()
    {
        if (_markerOrb != null && _target != null)
            _markerOrb.transform.position = _target.position + markerOffset;
    }

    private void OnDisable() => Clear();

    private void OnDestroy()
    {
        Clear();
        if (_markerMaterial != null)
            Destroy(_markerMaterial);
    }

    /// <summary>Updates the presentation for a newly active route step.</summary>
    public void SetTarget(QuestRouteStep step)
    {
        Transform destination = step?.destination;
        bool shouldOutline = destination != null && IsInteractiveDestination(step, destination);
        if (_target == destination && IsShowingOutline == shouldOutline)
            return;

        Clear();
        _target = destination;
        if (_target == null)
            return;

        if (shouldOutline)
            ShowOutline(_target);
        else
            ShowMarkerOrb();
    }

    /// <summary>Removes the current marker and restores any pre-existing outline settings.</summary>
    public void Clear()
    {
        if (_markerOrb != null)
        {
            Destroy(_markerOrb);
            _markerOrb = null;
        }

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
    }

    private bool IsShowingOutline => _activeOutline != null;

    private static bool IsInteractiveDestination(QuestRouteStep step, Transform destination)
    {
        return step.requiresInteraction || destination.GetComponentInParent<InteractableBase>() != null ||
               destination.GetComponentInChildren<InteractableBase>() != null;
    }

    private void ShowOutline(Transform destination)
    {
        InteractableBase interactable = destination.GetComponentInParent<InteractableBase>();
        if (interactable == null)
            interactable = destination.GetComponentInChildren<InteractableBase>();

        Transform outlineTarget = interactable != null ? interactable.transform : destination;
        _activeOutline = outlineTarget.GetComponent<Outline>();
        if (_activeOutline == null)
            _activeOutline = outlineTarget.GetComponentInChildren<Outline>();

        _ownsOutline = _activeOutline == null;
        if (_ownsOutline)
        {
            _activeOutline = outlineTarget.gameObject.AddComponent<Outline>();
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

    private void ShowMarkerOrb()
    {
        _markerOrb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _markerOrb.name = "Quest Destination Marker";
        _markerOrb.transform.position = _target.position + markerOffset;
        _markerOrb.transform.localScale = Vector3.one * markerScale;

        Collider markerCollider = _markerOrb.GetComponent<Collider>();
        if (markerCollider != null)
            Destroy(markerCollider);

        Renderer markerRenderer = _markerOrb.GetComponent<Renderer>();
        markerRenderer.sharedMaterial = GetMarkerMaterial();
    }

    private Material GetMarkerMaterial()
    {
        if (_markerMaterial != null)
            return _markerMaterial;

        Material template = Resources.Load<Material>("Materials/OutlineFill");
        _markerMaterial = new Material(template)
        {
            name = "Quest Marker (Runtime)"
        };
        _markerMaterial.SetColor("_OutlineColor", markerColor);
        _markerMaterial.SetFloat("_OutlineWidth", 0f);
        _markerMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
        return _markerMaterial;
    }
}
