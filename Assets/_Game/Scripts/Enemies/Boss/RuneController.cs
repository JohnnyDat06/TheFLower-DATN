using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>Owns one Rune's state, trigger volume and minimal Phase 6 visual feedback.</summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class RuneController : MonoBehaviour
{
    [Tooltip("Bán kính vùng Shockwave phải đi qua để Charge Rune.")]
    [SerializeField, Min(0.1f)] private float _shockwaveTriggerRadius = 0.9f;
    [Tooltip("Prefab kim cương hiển thị trạng thái của Rune.")]
    [SerializeField] private GameObject _diamondPrefab;
    [Tooltip("Vị trí local của kim cương so với marker Rune.")]
    [SerializeField] private Vector3 _diamondLocalPosition = new(0f, 0.55f, 0f);
    [Tooltip("Tỷ lệ local của model kim cương.")]
    [SerializeField] private Vector3 _diamondLocalScale = Vector3.one;
    [Tooltip("So giay Rune giu Charged neu chua duoc Seal consume; co the tinh chinh theo arena.")]
    [SerializeField, Min(0.1f)] private float _chargedDuration = 3f;

    private SphereCollider _shockwaveTrigger;
    private Renderer[] _stateVisuals;
    private Light[] _stateLights;
    private RuneManager _manager;
    private float _chargedUntil;

    /// <summary>Current state of this Rune.</summary>
    public RuneState State { get; private set; } = RuneState.Inactive;

    /// <summary>Raised once after this Rune successfully changes state.</summary>
    public event Action<RuneController, RuneState> StateChanged;

    private void Awake()
    {
        _shockwaveTrigger = GetComponent<SphereCollider>();
        _shockwaveTrigger.isTrigger = true;
        _shockwaveTrigger.radius = _shockwaveTriggerRadius;
        _manager = GetComponentInParent<RuneManager>();
        CreateStateVisual();
        ApplyVisualState();
    }

    private void Update()
    {
        if (!IsServerAuthority() || State != RuneState.Charged || Time.time < _chargedUntil) return;

        ResetRune();
        Debug.Log($"[RuneController] {name} charge expired after {_chargedDuration:0.0} seconds.", this);
    }

    /// <summary>Charges this Rune once. Only RuneManager may coordinate this change.</summary>
    public bool TryCharge()
    {
        if (State != RuneState.Inactive) return false;

        State = RuneState.Charged;
        _chargedUntil = Time.time + _chargedDuration;
        ApplyVisualState();
        StateChanged?.Invoke(this, State);
        return true;
    }

    /// <summary>Marks this charged Rune as consumed by its matching Seal.</summary>
    public bool TryConsume()
    {
        if (State != RuneState.Charged) return false;

        State = RuneState.Consumed;
        _chargedUntil = 0f;
        ApplyVisualState();
        StateChanged?.Invoke(this, State);
        return true;
    }

    /// <summary>Returns this Rune to the inactive state for the Phase 6 test cycle.</summary>
    public void ResetRune()
    {
        if (State == RuneState.Inactive) return;

        State = RuneState.Inactive;
        _chargedUntil = 0f;
        ApplyVisualState();
        StateChanged?.Invoke(this, State);
    }

    [ContextMenu("Debug/Charge Rune")]
    private void ChargeRuneForDebug()
    {
        TryCharge();
    }

    [ContextMenu("Debug/Reset Rune")]
    private void ResetRuneForDebug()
    {
        ResetRune();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;
        if (other.GetComponentInParent<ShockwaveHitbox>() == null) return;

        if (_manager == null) _manager = GetComponentInParent<RuneManager>();
        _manager?.TryChargeRune(this);
    }

    private void CreateStateVisual()
    {
        Transform existingVisual = transform.Find("Rune State Visual");
        if (existingVisual != null)
        {
            CacheVisualComponents(existingVisual.gameObject);
            return;
        }

        if (_diamondPrefab == null)
        {
            Debug.LogError($"[RuneController] Diamond prefab is missing for {name}.", this);
            return;
        }

        GameObject visual = Instantiate(_diamondPrefab, transform);
        visual.name = "Rune State Visual";
        visual.transform.localPosition = _diamondLocalPosition;
        visual.transform.localScale = _diamondLocalScale;
        CacheVisualComponents(visual);
    }

    private void ApplyVisualState()
    {
        Color stateColor = State switch
        {
            RuneState.Charged => new Color(1f, 0.08f, 0.03f, 0.9f),
            RuneState.Consumed => new Color(0.2f, 0.12f, 0.08f, 0.75f),
            _ => new Color(0.93f, 0.96f, 1f, 0.75f)
        };

        if (_stateVisuals != null)
        {
            foreach (Renderer visual in _stateVisuals)
            {
                Material material = visual.material;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", stateColor);
                if (material.HasProperty("_Color")) material.SetColor("_Color", stateColor);
                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", State == RuneState.Charged ? stateColor * 1.5f : stateColor * 0.2f);
            }
        }

        if (_stateLights == null) return;
        foreach (Light stateLight in _stateLights)
        {
            stateLight.color = stateColor;
            stateLight.intensity = State == RuneState.Charged ? 0.65f : 0.2f;
        }
    }

    private void CacheVisualComponents(GameObject visual)
    {
        _stateVisuals = visual.GetComponentsInChildren<Renderer>(true);
        _stateLights = visual.GetComponentsInChildren<Light>(true);
    }

    private static bool IsServerAuthority() =>
        NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
}
