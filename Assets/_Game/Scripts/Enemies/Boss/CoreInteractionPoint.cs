using Unity.Netcode;
using UnityEngine;

/// <summary>One of the two player interaction points required to create a single Core Hit.</summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class CoreInteractionPoint : MonoBehaviour, IInteractable
{
    [Tooltip("Dinh danh diem Core nay. Hai player khac nhau co the kich hoat cung mot diem hoac hai diem khac nhau.")]
    [SerializeField] private CorePointId _pointId;
    [Tooltip("Ban kinh player co the tim thay diem tuong tac Core.")]
    [SerializeField, Min(0.1f)] private float _interactionRadius = 1.2f;
    [Tooltip("Controller kiem tra hai diem Core duoc kich hoat dong bo.")]
    [SerializeField] private DualCoreInteractionController _dualController;
    [Tooltip("Kich thuoc world-space marker hien thi khi Core mo.")]
    [SerializeField, Min(0.1f)] private float _visualDiameter = 0.55f;

    private SphereCollider _interactionTrigger;
    private Renderer _stateVisual;

    /// <summary>Stable identity used by the dual-activation validation.</summary>
    public CorePointId PointId => _pointId;

    /// <summary>Maximum server-authoritative distance accepted for a Client interaction request.</summary>
    public float ServerInteractionDistance => _interactionRadius + 2.2f;

    /// <inheritdoc />
    public string InteractionPrompt => $"Activate Core Point {_pointId}";

    /// <inheritdoc />
    public bool CanInteract => _dualController != null && _dualController.CanActivatePoint(this);

    /// <inheritdoc />
    public bool IsActivated => _dualController != null && _dualController.IsPointPending(this);

    private void Awake()
    {
        _interactionTrigger = GetComponent<SphereCollider>();
        _interactionTrigger.isTrigger = true;
        _interactionTrigger.radius = _interactionRadius;
        if (_dualController == null) _dualController = GetComponentInParent<DualCoreInteractionController>();
        CreateStateVisual();
        RefreshStateVisual();
    }

    private void Update()
    {
        if (_dualController == null) _dualController = GetComponentInParent<DualCoreInteractionController>();
        RefreshStateVisual();
    }

    /// <inheritdoc />
    public void Interact(ulong playerId)
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
        {
            BossNetworkState.Instance?.RequestCoreInteraction(this);
            return;
        }

        _dualController?.TryActivatePoint(this, playerId);
    }

    /// <inheritdoc />
    public void OnHoverEnter() { }

    /// <inheritdoc />
    public void OnHoverExit() { }

    /// <inheritdoc />
    public Transform GetPromptTransform() => transform;

    private void CreateStateVisual()
    {
        Transform existingVisual = transform.Find("Core Point Visual");
        if (existingVisual != null)
        {
            _stateVisual = existingVisual.GetComponent<Renderer>();
            return;
        }

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Core Point Visual";
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = Vector3.one * _visualDiameter;
        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null) Destroy(visualCollider);
        _stateVisual = visual.GetComponent<Renderer>();
    }

    private void RefreshStateVisual()
    {
        if (_stateVisual == null) return;

        bool isCoreExposed = _dualController != null && _dualController.IsCoreExposed;
        _stateVisual.gameObject.SetActive(isCoreExposed);
        if (!isCoreExposed) return;

        Color color = IsActivated ? Color.yellow : new Color(0.1f, 0.9f, 1f, 1f);
        Material material = _stateVisual.material;
        material.color = color;
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 1.25f);
    }
}

/// <summary>Identifiers for the authored Core interaction markers.</summary>
public enum CorePointId
{
    A,
    B
}
