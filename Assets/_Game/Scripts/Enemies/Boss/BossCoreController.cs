using System;
using UnityEngine;

/// <summary>Opens a non-interactable Core while the boss is Stunned, then resets the puzzle loop on timeout.</summary>
public sealed class BossCoreController : MonoBehaviour
{
    [Tooltip("Prefab visual duoc hien thi tai trung tam giua hai marker CorePoint.")]
    [SerializeField] private GameObject _coreVisualPrefab;
    [Tooltip("So giay Core mo truoc khi tu dong dong va reset Rune/Seal.")]
    [SerializeField, Range(6f, 8f)] private float _exposedDuration = 7f;
    [Tooltip("Ti le cua visual Core khi no duoc mo.")]
    [SerializeField] private Vector3 _coreVisualScale = Vector3.one * 1.4f;

    private BossArenaReferences _arenaReferences;
    private BossStunController _stunController;
    private SealManager _sealManager;
    private RuneManager _runeManager;
    private GameObject _coreVisual;
    private float _exposedUntil;

    /// <summary>Raised once when two valid Core points are activated within the Phase 10 sync window.</summary>
    public event Action CoreHit;

    /// <summary>Current Core state.</summary>
    public BossCoreState State { get; private set; } = BossCoreState.Locked;

    /// <summary>True only while the current exposed Core can receive one dual-player activation.</summary>
    public bool CanAcceptDualActivation => State == BossCoreState.Exposed;

    private void Awake()
    {
        CacheDependencies();
        CreateCoreVisual();
        SetCoreVisualVisible(false);
    }

    private void Update()
    {
        CacheDependencies();

        if (State == BossCoreState.Locked && _stunController != null && _stunController.IsStunned)
        {
            ExposeCore();
            return;
        }

        if (State != BossCoreState.Exposed) return;

        if (_stunController == null || !_stunController.IsStunned)
        {
            LockCore();
            return;
        }

        if (Time.time >= _exposedUntil) CloseAfterTimeout();
    }

    private void ExposeCore()
    {
        State = BossCoreState.Exposed;
        _exposedUntil = Time.time + _exposedDuration;
        SetCoreVisualVisible(true);
        Debug.Log($"[BossCoreController] Core exposed for {_exposedDuration:0.0} seconds.", this);
    }

    private void CloseAfterTimeout()
    {
        ResetPuzzleCycle();
        LockCore();
        Debug.Log("[BossCoreController] Core exposure expired. Rune and Seal cycle reset.", this);
    }

    /// <summary>Accepts one completed dual activation and closes this Core before a later phase consumes the hit.</summary>
    public bool TryRegisterCoreHit()
    {
        if (!CanAcceptDualActivation) return false;

        CoreHit?.Invoke();
        ResetPuzzleCycle();
        LockCore();
        Debug.Log("[BossCoreController] Core Hit registered.", this);
        return true;
    }

    private void ResetPuzzleCycle()
    {
        _sealManager?.ResetAllSealsForCycle();
        _runeManager?.ResetAllRunesForCycle();
        _stunController?.ReleaseStunAfterCoreTimeout();
    }

    private void LockCore()
    {
        State = BossCoreState.Locked;
        _exposedUntil = 0f;
        SetCoreVisualVisible(false);
    }

    private void CacheDependencies()
    {
        _arenaReferences ??= GetComponent<BossArenaReferences>();
        _stunController ??= GetComponent<BossStunController>();
        _sealManager ??= GetComponent<SealManager>();
        _runeManager ??= GetComponent<RuneManager>();
    }

    private void CreateCoreVisual()
    {
        if (_coreVisual != null || _coreVisualPrefab == null || _arenaReferences == null) return;

        _coreVisual = Instantiate(_coreVisualPrefab, _arenaReferences.CoreCenter, Quaternion.identity, transform);
        _coreVisual.name = "Boss Core Visual";
        _coreVisual.transform.localScale = _coreVisualScale;

        foreach (Collider visualCollider in _coreVisual.GetComponentsInChildren<Collider>(true))
            Destroy(visualCollider);

        ApplyExposedVisualColor();
    }

    private void SetCoreVisualVisible(bool isVisible)
    {
        if (_coreVisual == null) CreateCoreVisual();
        if (_coreVisual != null) _coreVisual.SetActive(isVisible);
    }

    private void ApplyExposedVisualColor()
    {
        if (_coreVisual == null) return;

        Color coreColor = new(0.1f, 0.9f, 1f, 1f);
        foreach (Renderer renderer in _coreVisual.GetComponentsInChildren<Renderer>(true))
        {
            Material material = renderer.material;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", coreColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", coreColor);
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", coreColor * 1.4f);
        }
    }
}
