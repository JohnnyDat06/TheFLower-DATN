using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>Owns one Seal's Rune prerequisite, player interaction and active-duration timer.</summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class SealController : MonoBehaviour, IInteractable
{
    [Tooltip("Rune phải ở trạng thái Charged trước khi Seal có thể tương tác.")]
    [SerializeField] private RuneController _requiredRune;
    [Tooltip("Bán kính player có thể tìm và tương tác Seal.")]
    [SerializeField, Min(0.1f)] private float _interactionRadius = 1.2f;
    [Tooltip("Số giây Seal giữ trạng thái Active trước khi Expired.")]
    [SerializeField, Range(10f, 15f)] private float _activeDuration = 12f;

    private SphereCollider _interactionTrigger;
    private Renderer _stateVisual;
    private SealManager _manager;
    private float _activeUntil;

    /// <summary>Current state of this Seal.</summary>
    public SealState State { get; private set; } = SealState.Inactive;

    /// <summary>Rune prerequisite assigned to this Seal.</summary>
    public RuneController RequiredRune => _requiredRune;

    /// <inheritdoc />
    public string InteractionPrompt => $"Activate {name}";

    /// <inheritdoc />
    public bool CanInteract => State == SealState.Ready;

    /// <inheritdoc />
    public bool IsActivated => State == SealState.Active;

    /// <summary>Raised whenever the Seal changes state.</summary>
    public event Action<SealController, SealState> StateChanged;

    private void Awake()
    {
        _interactionTrigger = GetComponent<SphereCollider>();
        _interactionTrigger.isTrigger = true;
        _interactionTrigger.radius = _interactionRadius;
        _manager = GetComponentInParent<SealManager>();
        CreateStateVisual();
        RefreshReadiness();
        ApplyVisualState();
    }

    private void Update()
    {
        if (!IsServerAuthority()) return;

        if (State == SealState.Active && Time.time >= _activeUntil)
            SetState(SealState.Expired);
        else if (State == SealState.Inactive)
            RefreshReadiness();
    }

    /// <inheritdoc />
    public void Interact(ulong playerId)
    {
        if (!CanInteract) return;
        if (_manager == null) _manager = GetComponentInParent<SealManager>();
        _manager?.TryActivateSeal(this, playerId);
    }

    /// <inheritdoc />
    public void OnHoverEnter() { }

    /// <inheritdoc />
    public void OnHoverExit() { }

    /// <inheritdoc />
    public Transform GetPromptTransform() => transform;

    /// <summary>Updates Ready state after the assigned Rune changes.</summary>
    public void RefreshReadiness()
    {
        if (State != SealState.Inactive || _requiredRune == null || _requiredRune.State != RuneState.Charged) return;
        SetState(SealState.Ready);
    }

    /// <summary>Activates this Seal once after the manager validates player and Rune conditions.</summary>
    public bool TryActivate()
    {
        if (State != SealState.Ready || _requiredRune == null || !_requiredRune.TryConsume()) return false;

        _activeUntil = Time.time + _activeDuration;
        SetState(SealState.Active);
        return true;
    }

    private void SetState(SealState nextState)
    {
        if (State == nextState) return;

        State = nextState;
        ApplyVisualState();
        Debug.Log($"[SealController] {name} is now {State}.", this);
        StateChanged?.Invoke(this, State);
    }

    private void CreateStateVisual()
    {
        Transform existingVisual = transform.Find("Seal State Visual");
        if (existingVisual != null)
        {
            _stateVisual = existingVisual.GetComponent<Renderer>();
            return;
        }

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = "Seal State Visual";
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        visual.transform.localScale = new Vector3(1.1f, 0.05f, 1.1f);
        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null) Destroy(visualCollider);
        _stateVisual = visual.GetComponent<Renderer>();
    }

    private void ApplyVisualState()
    {
        if (_stateVisual == null) return;

        Color color = State switch
        {
            SealState.Ready => Color.yellow,
            SealState.Active => Color.green,
            SealState.Expired => Color.red,
            _ => new Color(0.15f, 0.2f, 0.25f, 1f)
        };
        _stateVisual.material.color = color;
    }

    private static bool IsServerAuthority() =>
        NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
}
