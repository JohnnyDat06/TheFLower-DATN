using System;
using System.Collections;
using UnityEngine;

/// <summary>Owns one arena tile's three-hit Normal, Cracked, Warning and Fall lifecycle.</summary>
public sealed class FloorTile : MonoBehaviour
{
    [Tooltip("Thoi gian Tile di chuyen xuong sau lan Shockwave thu ba.")]
    [SerializeField, Min(0.05f)] private float _fallDuration = 0.45f;
    [Tooltip("Khoang cach Tile roi xuong sau lan Shockwave thu ba.")]
    [SerializeField, Min(0.1f)] private float _fallDistance = 5f;
    [Tooltip("Bien do rung local de Warning state de nhan biet truoc lan Shockwave thu ba.")]
    [SerializeField, Min(0f)] private float _warningShakeAmount = 0.06f;
    [Tooltip("Mau sàn sau khi trung Shockwave lan dau (trang thai Cracked).")]
    [SerializeField] private Color _firstShockwaveColor = new(0.42f, 0.16f, 0.045f, 1f);
    [Tooltip("Warning tile uses a muted earthen orange so the brick texture remains readable.")]
    [SerializeField] private Color _warningColor = new(0.72f, 0.32f, 0.075f, 1f);
    [Tooltip("Mau sàn trong luc roi xuong o trang thai Fall.")]
    [SerializeField] private Color _fallColor = new(0.82f, 0.5f, 0.12f, 1f);
    [Tooltip("Very small Warning emission. Set to zero to show only the brick color.")]
    [SerializeField, Range(0f, 0.15f)] private float _warningEmissionIntensity = 0.025f;

    private Renderer[] _renderers;
    private Collider[] _colliders;
    private Color[] _normalColors;
    private Vector3 _initialLocalPosition;
    private Coroutine _fallRoutine;
    private bool _hasInitialState;

    /// <summary>Current tile lifecycle state for Inspector and manager queries.</summary>
    public FloorTileState State { get; private set; } = FloorTileState.Normal;

    /// <summary>World-space visual centre used by the straight Shockwave pattern query.</summary>
    public Vector3 WorldCenter
    {
        get
        {
            if (_renderers == null || _renderers.Length == 0) return transform.position;

            Bounds combinedBounds = _renderers[0].bounds;
            for (int index = 1; index < _renderers.Length; index++)
                combinedBounds.Encapsulate(_renderers[index].bounds);
            return combinedBounds.center;
        }
    }

    /// <summary>Raised whenever the tile changes state after a valid Shockwave hit.</summary>
    public event Action<FloorTile, FloorTileState> StateChanged;

    private void Awake()
    {
        CacheComponents();
        CaptureInitialState();
        ApplyStateVisual();
    }

    private void Update()
    {
        if (State != FloorTileState.Warning) return;

        float shakeX = Mathf.Sin(Time.time * 55f) * _warningShakeAmount;
        transform.localPosition = _initialLocalPosition + new Vector3(shakeX, 0f, 0f);
    }

    /// <summary>Advances exactly one state per Shockwave hit; the third hit starts the Fall.</summary>
    public bool TryDamage()
    {
        if (_fallRoutine != null || State == FloorTileState.Fall) return false;

        switch (State)
        {
            case FloorTileState.Normal:
                SetState(FloorTileState.Cracked);
                return true;
            case FloorTileState.Cracked:
                SetState(FloorTileState.Warning);
                return true;
            case FloorTileState.Warning:
                transform.localPosition = _initialLocalPosition;
                SetState(FloorTileState.Fall);
                foreach (Collider tileCollider in _colliders) tileCollider.enabled = false;
                _fallRoutine = StartCoroutine(RunFall());
                return true;
            default:
                return false;
        }
    }

    /// <summary>Restores this tile for the Phase 12 and Phase 13 debug cycle.</summary>
    public void ResetTile()
    {
        if (_fallRoutine != null) StopCoroutine(_fallRoutine);
        _fallRoutine = null;
        gameObject.SetActive(true);
        CacheComponents();
        CaptureInitialState();
        transform.localPosition = _initialLocalPosition;
        foreach (Collider tileCollider in _colliders) tileCollider.enabled = true;
        SetState(FloorTileState.Normal);
    }

    /// <summary>Advances or resets this Client tile to the Host-owned critical state.</summary>
    public void ApplyNetworkState(FloorTileState state)
    {
        if (State == state) return;
        if (state == FloorTileState.Normal || (int)state < (int)State) ResetTile();

        while ((int)State < (int)state)
        {
            if (!TryDamage()) break;
        }
    }

    [ContextMenu("Debug/Damage Tile")]
    private void DamageTileForDebug()
    {
        TryDamage();
    }

    private IEnumerator RunFall()
    {
        Vector3 fallStartPosition = transform.localPosition;
        Vector3 fallEndPosition = fallStartPosition + Vector3.down * _fallDistance;
        float elapsed = 0f;
        while (elapsed < _fallDuration)
        {
            elapsed += Time.deltaTime;
            transform.localPosition = Vector3.Lerp(fallStartPosition, fallEndPosition, elapsed / _fallDuration);
            yield return null;
        }

        transform.localPosition = fallEndPosition;
        _fallRoutine = null;
        gameObject.SetActive(false);
    }

    private void SetState(FloorTileState nextState)
    {
        if (State == nextState) return;

        State = nextState;
        ApplyStateVisual();
        Debug.Log($"[FloorTile] {name} is now {State}.", this);
        StateChanged?.Invoke(this, State);
    }

    private void CacheComponents()
    {
        _renderers ??= GetComponentsInChildren<Renderer>(true);
        _colliders ??= GetComponentsInChildren<Collider>(true);
    }

    private void CaptureInitialState()
    {
        if (_hasInitialState) return;

        _initialLocalPosition = transform.localPosition;
        _normalColors = new Color[_renderers.Length];
        for (int index = 0; index < _renderers.Length; index++)
            _normalColors[index] = GetRendererColor(_renderers[index]);
        _hasInitialState = true;
    }

    private void ApplyStateVisual()
    {
        if (_renderers == null) return;

        for (int index = 0; index < _renderers.Length; index++)
        {
            Color color = State switch
            {
                FloorTileState.Cracked => _firstShockwaveColor,
                FloorTileState.Warning => _warningColor,
                FloorTileState.Fall => _fallColor,
                _ => _normalColors != null && index < _normalColors.Length ? _normalColors[index] : Color.white
            };
            Color emission = State == FloorTileState.Warning
                ? color * _warningEmissionIntensity
                : Color.black;
            SetRendererColor(_renderers[index], color, emission);
        }
    }

    private static Color GetRendererColor(Renderer renderer)
    {
        Material material = renderer.material;
        if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
        return material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
    }

    private static void SetRendererColor(Renderer renderer, Color color, Color emission)
    {
        foreach (Material material in renderer.materials)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (!material.HasProperty("_EmissionColor")) continue;

            material.SetColor("_EmissionColor", emission);
            if (emission.maxColorComponent > 0f) material.EnableKeyword("_EMISSION");
            else material.DisableKeyword("_EMISSION");
        }
    }
}
