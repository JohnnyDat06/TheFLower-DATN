using System;
using System.Collections;
using UnityEngine;

/// <summary>Owns one arena tile's independent Normal, Cracked, Warning and Fall lifecycle.</summary>
public sealed class FloorTile : MonoBehaviour
{
    [Tooltip("So giay Tile o trang thai Cracked truoc khi canh bao Warning.")]
    [SerializeField, Min(0f)] private float _crackedToWarningDelay = 1.5f;
    [Tooltip("So giay Tile canh bao Warning truoc khi roi.")]
    [SerializeField, Min(0f)] private float _warningToFallDelay = 0.5f;
    [Tooltip("Thoi gian Tile di chuyen xuong khi roi.")]
    [SerializeField, Min(0.05f)] private float _fallDuration = 0.45f;
    [Tooltip("Khoang cach Tile roi xuong truoc khi bi disable.")]
    [SerializeField, Min(0.1f)] private float _fallDistance = 5f;

    private Renderer[] _renderers;
    private Collider[] _colliders;
    private Color[] _normalColors;
    private Vector3 _initialLocalPosition;
    private Coroutine _stateRoutine;
    private bool _hasInitialState;

    /// <summary>Current tile lifecycle state for Inspector and manager queries.</summary>
    public FloorTileState State { get; private set; } = FloorTileState.Normal;

    /// <summary>Raised whenever the tile enters a new lifecycle state.</summary>
    public event Action<FloorTile, FloorTileState> StateChanged;

    private void Awake()
    {
        CacheComponents();
        CaptureInitialState();
        ApplyStateVisual();
    }

    /// <summary>Starts this tile's lifecycle once. Repeated damage cannot create duplicate routines.</summary>
    public bool TryDamage()
    {
        if (State != FloorTileState.Normal || _stateRoutine != null) return false;

        _stateRoutine = StartCoroutine(RunDamageLifecycle());
        return true;
    }

    /// <summary>Restores this tile for the Phase 12 standalone debug cycle.</summary>
    public void ResetTile()
    {
        if (_stateRoutine != null) StopCoroutine(_stateRoutine);
        _stateRoutine = null;
        gameObject.SetActive(true);
        CacheComponents();
        CaptureInitialState();
        transform.localPosition = _initialLocalPosition;
        foreach (Collider tileCollider in _colliders) tileCollider.enabled = true;
        SetState(FloorTileState.Normal);
    }

    [ContextMenu("Debug/Damage Tile")]
    private void DamageTileForDebug()
    {
        TryDamage();
    }

    private IEnumerator RunDamageLifecycle()
    {
        SetState(FloorTileState.Cracked);
        if (_crackedToWarningDelay > 0f) yield return new WaitForSeconds(_crackedToWarningDelay);

        SetState(FloorTileState.Warning);
        if (_warningToFallDelay > 0f) yield return new WaitForSeconds(_warningToFallDelay);

        SetState(FloorTileState.Fall);
        foreach (Collider tileCollider in _colliders) tileCollider.enabled = false;

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
        _stateRoutine = null;
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
                FloorTileState.Cracked => new Color(0.45f, 0.25f, 0.12f, 1f),
                FloorTileState.Warning => new Color(1f, 0.42f, 0.05f, 1f),
                FloorTileState.Fall => new Color(0.16f, 0.04f, 0.02f, 1f),
                _ => _normalColors != null && index < _normalColors.Length ? _normalColors[index] : Color.white
            };
            SetRendererColor(_renderers[index], color, State == FloorTileState.Warning ? color * 0.7f : Color.black);
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
        Material material = renderer.material;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", emission);
    }
}
