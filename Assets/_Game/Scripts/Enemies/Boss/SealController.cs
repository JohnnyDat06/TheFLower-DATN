using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>Owns one Seal's Rune prerequisite, player interaction and active-duration timer.</summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class SealController : MonoBehaviour, IInteractable
{
    private const string StateMaterialResourcePath = "Materials/BossMarkerState_URP";

    [Tooltip("Rune phải ở trạng thái Charged trước khi Seal có thể tương tác.")]
    [SerializeField] private RuneController _requiredRune;
    [Tooltip("Bán kính player có thể tìm và tương tác Seal.")]
    [SerializeField, Min(0.1f)] private float _interactionRadius = 1.2f;
    [Tooltip("So giay Seal giu trang thai Active truoc khi tu tat va reset Rune tuong ung.")]
    [SerializeField, Range(10f, 15f)] private float _activeDuration = 12f;

    private SphereCollider _interactionTrigger;
    private Renderer _stateVisual;
    private Material _stateMaterialInstance;
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

    private void OnDestroy()
    {
        if (_stateMaterialInstance != null)
            Destroy(_stateMaterialInstance);
    }

    private void Update()
    {
        if (!IsServerAuthority()) return;

        if (State == SealState.Active && Time.time >= _activeUntil)
            ResetAfterActiveTimeout();
        else if (State == SealState.Ready && (_requiredRune == null || _requiredRune.State != RuneState.Charged))
            SetState(SealState.Inactive);
        else if (State == SealState.Inactive)
            RefreshReadiness();
    }

    /// <inheritdoc />
    public void Interact(ulong playerId)
    {
        if (!CanInteract) return;
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
        {
            BossNetworkState.Instance?.RequestSealInteraction(this);
            return;
        }

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

    /// <summary>Clears this Seal's active timer so the next Rune-and-Seal cycle can begin.</summary>
    public void ResetSealForCycle()
    {
        _activeUntil = 0f;
        SetState(SealState.Inactive);
    }

    /// <summary>Applies the Host-owned Seal state and refreshes Client visuals.</summary>
    public void ApplyNetworkState(SealState state)
    {
        if (State == state) return;

        _activeUntil = 0f;
        SetState(state);
    }

    private void ResetAfterActiveTimeout()
    {
        _activeUntil = 0f;
        _requiredRune?.ResetRune();
        SetState(SealState.Inactive);
        Debug.Log($"[SealController] {name} timed out and reset with its Rune.", this);
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
            AssignBuildSafeMaterial();
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
        AssignBuildSafeMaterial();
    }

    private void AssignBuildSafeMaterial()
    {
        if (_stateVisual == null) return;

        Material template = Resources.Load<Material>(StateMaterialResourcePath);
        if (template != null)
        {
            _stateMaterialInstance = new Material(template)
            {
                name = $"{name}_SealStateMaterial"
            };
            _stateVisual.sharedMaterial = _stateMaterialInstance;
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
            ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            _stateMaterialInstance = new Material(shader)
            {
                name = $"{name}_SealStateMaterialFallback"
            };
            _stateVisual.sharedMaterial = _stateMaterialInstance;
            Debug.LogWarning($"[SealController] Missing Resources material '{StateMaterialResourcePath}', using shader fallback.", this);
            return;
        }

        Debug.LogError($"[SealController] URP visual material is missing for {name}.", this);
    }

    private void ApplyVisualState()
    {
        if (_stateVisual == null) return;

        Color color = State switch
        {
            SealState.Ready => Color.yellow,
            SealState.Active => Color.green,
            _ => new Color(0.15f, 0.2f, 0.25f, 1f)
        };
        Material material = _stateVisual.sharedMaterial;
        if (material == null) return;

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", State == SealState.Active ? color * 1.25f : color * 0.15f);
    }

    private static bool IsServerAuthority() =>
        NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
}
