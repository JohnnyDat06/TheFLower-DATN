using System.Collections;
using UnityEngine;
using MoreMountains.Feedbacks;

public class ErisTile : MonoBehaviour
{
    public Vector2Int GridPos;
    [SerializeField] private MeshRenderer _renderer;
    private Material _surfaceMaterial;
    private Material _pendingSurfaceMaterial;
    private Color _pendingSurfaceColor = Color.white;
    private bool _hasPendingSurfaceColor;
    private Color _surfaceBaseColor = Color.white;
    private bool _hasSurfaceMaterial;
    private Outline _outline;
    private bool _isRevealedCorrect = false;

    [Header("Feel Feedbacks")]
    [SerializeField] private MMF_Player _spawnFeedback;
    [SerializeField] private MMF_Player _despawnFeedback;
    [SerializeField] private MMF_Player _correctFeedback;
    [SerializeField] private MMF_Player _wrongFeedback;
    [SerializeField] private MMF_Player _highlightFeedback;
    [SerializeField] private MMF_Player _idleBounceFeedback; // Feedbacks cho việc nhúng nhảy chờ đợi

    private Vector3 _originalLocalScale;
    private Vector3 _originalLocalPosition;
    private bool _isInitialized = false;
    private Coroutine _entranceCoroutine;
    private Coroutine _subtleBounceCoroutine;
    private Collider _tileCollider;

    public bool IsEntrancePlaying => _entranceCoroutine != null;

    private void Awake()
    {
        if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
        _outline = GetComponent<Outline>();
        if (_outline == null) _outline = gameObject.AddComponent<Outline>();
        _outline.enabled = false;
        _outline.OutlineWidth = 4f; 
        _outline.OutlineColor = Color.cyan; 

        _originalLocalScale = transform.localScale;
        _originalLocalPosition = transform.localPosition;
        _tileCollider = GetComponent<Collider>();
        _isInitialized = true;
    }

    /// <summary>Copies the material of the surface sampled below this tile.</summary>
    public void ApplySurfaceMaterial(Material sourceMaterial)
    {
        if (_renderer == null || sourceMaterial == null) return;

        if (_surfaceMaterial != null)
        {
            Destroy(_surfaceMaterial);
            _surfaceMaterial = null;
        }

        // Do not copy the ground texture/shader: its UV layout belongs to the
        // environment mesh and creates black/repeated texture blocks on tiles.
        // Keep the tile's own material and copy only the ground's tint.
        Material tileMaterial = _renderer.material;
        _surfaceBaseColor = ReadMaterialColor(sourceMaterial);
        WriteMaterialColor(tileMaterial, _surfaceBaseColor);
        _hasSurfaceMaterial = false;
    }

    /// <summary>Queues the ground material so it is applied exactly when the tile lands.</summary>
    public void SetSurfaceMaterialOnLanding(Material sourceMaterial)
    {
        if (sourceMaterial == null) return;
        if (_entranceCoroutine == null)
            ApplySurfaceMaterial(sourceMaterial);
        else
            _pendingSurfaceMaterial = sourceMaterial;
    }

    /// <summary>Queues a sampled ground color so the tile blends at the landing point.</summary>
    public void SetSurfaceColorOnLanding(Color sourceColor)
    {
        if (_entranceCoroutine == null)
            ApplySurfaceColor(sourceColor);
        else
        {
            _pendingSurfaceColor = sourceColor;
            _hasPendingSurfaceColor = true;
        }
    }

    public void ApplySurfaceColor(Color sourceColor)
    {
        if (_renderer == null) return;
        if (_surfaceMaterial != null)
        {
            Destroy(_surfaceMaterial);
            _surfaceMaterial = null;
        }

        _surfaceBaseColor = sourceColor;
        Material tileMaterial = _renderer.material;
        WriteMaterialColor(tileMaterial, _surfaceBaseColor);
        tileMaterial.color = _surfaceBaseColor;
        _hasSurfaceMaterial = false;
    }

    private Material GetWritableMaterial()
    {
        if (_renderer == null) return null;
        return _hasSurfaceMaterial ? _renderer.sharedMaterial : _renderer.material;
    }

    private static Color ReadMaterialColor(Material material)
    {
        if (material == null) return Color.white;
        if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color")) return material.GetColor("_Color");
        if (material.HasProperty("_TintColor")) return material.GetColor("_TintColor");
        if (material.HasProperty("_MainColor")) return material.GetColor("_MainColor");
        return material.color;
    }

    private static void WriteMaterialColor(Material material, Color color)
    {
        if (material == null) return;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", color);
        if (material.HasProperty("_MainColor")) material.SetColor("_MainColor", color);
    }

    private void OnDestroy()
    {
        if (_surfaceMaterial != null)
            Destroy(_surfaceMaterial);
    }

    private void LateUpdate()
    {
        if (!_isInitialized || _entranceCoroutine != null || _subtleBounceCoroutine != null) return;
        if ((transform.localScale - _originalLocalScale).sqrMagnitude > 0.000001f)
            transform.localScale = _originalLocalScale;
    }

    public void Init(Vector2Int pos)
    {
        GridPos = pos;
        ResetTile();
        PlaySpawnFeedback();
    }

    /// <summary>Animates the tile from above the sampled surface into place.</summary>
    public void InitEntrance(Vector2Int pos, Vector3 landingPosition, Quaternion landingRotation, float dropHeight, float duration)
    {
        GridPos = pos;
        ResetTile();
        _pendingSurfaceMaterial = null;
        _hasPendingSurfaceColor = false;
        if (_entranceCoroutine != null) StopCoroutine(_entranceCoroutine);
        _entranceCoroutine = StartCoroutine(EntranceRoutine(landingPosition, landingRotation, Mathf.Max(0f, dropHeight), Mathf.Max(0.01f, duration)));
    }

    private IEnumerator EntranceRoutine(Vector3 landingPosition, Quaternion landingRotation, float dropHeight, float duration)
    {
        if (_tileCollider != null) _tileCollider.enabled = false;
        Vector3 dropDirection = transform.parent != null ? transform.parent.up : Vector3.up;
        Vector3 startPosition = landingPosition + dropDirection * dropHeight;
        Quaternion startRotation = landingRotation * Quaternion.Euler(0f, 0f, 2.5f);
        transform.SetPositionAndRotation(startPosition, startRotation);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            transform.position = Vector3.LerpUnclamped(startPosition, landingPosition, eased);
            transform.rotation = Quaternion.Slerp(startRotation, landingRotation, eased);
            yield return null;
        }

        transform.SetPositionAndRotation(landingPosition, landingRotation);
        if (_tileCollider != null) _tileCollider.enabled = true;
        if (_hasPendingSurfaceColor)
        {
            ApplySurfaceColor(_pendingSurfaceColor);
            _hasPendingSurfaceColor = false;
        }
        else if (_pendingSurfaceMaterial != null)
        {
            ApplySurfaceMaterial(_pendingSurfaceMaterial);
            _pendingSurfaceMaterial = null;
        }
        _originalLocalPosition = transform.localPosition;
        _entranceCoroutine = null;
        // The descent animation is intentionally the only entrance motion.
        // Playing the original punch feedback here made the whole board visibly jump.
    }

    /// <summary>
    /// Applies the board scale before Init so Feel feedbacks restore the scaled
    /// size instead of returning the tile to its original prefab size.
    /// </summary>
    public void SetBoardScale(float scale)
    {
        if (!_isInitialized) return;

        float safeScale = Mathf.Max(0.01f, scale);
        _originalLocalScale *= safeScale;
        transform.localScale = _originalLocalScale;
    }

    private void ResetTransformImmediate()
    {
        if (!_isInitialized) return;

        if (_entranceCoroutine != null)
        {
            StopCoroutine(_entranceCoroutine);
            _entranceCoroutine = null;
            if (_tileCollider != null) _tileCollider.enabled = true;
        }
        if (_subtleBounceCoroutine != null)
        {
            StopCoroutine(_subtleBounceCoroutine);
            _subtleBounceCoroutine = null;
        }
        
        try {
            if (_spawnFeedback != null && _spawnFeedback.IsPlaying) _spawnFeedback.StopFeedbacks();
            if (_correctFeedback != null && _correctFeedback.IsPlaying) _correctFeedback.StopFeedbacks();
            if (_wrongFeedback != null && _wrongFeedback.IsPlaying) _wrongFeedback.StopFeedbacks();
            if (_highlightFeedback != null && _highlightFeedback.IsPlaying) _highlightFeedback.StopFeedbacks();
            if (_idleBounceFeedback != null && _idleBounceFeedback.IsPlaying) _idleBounceFeedback.StopFeedbacks();
        } catch { }

        transform.localScale = _originalLocalScale;
        transform.localPosition = _originalLocalPosition;
    }

    public void PlayIdleBounce()
    {
        // KHÔNG nhúng nhảy nếu đã đi qua (màu xanh) hoặc đang biến mất
        if (_isRevealedCorrect || _subtleBounceCoroutine != null) return;
        _subtleBounceCoroutine = StartCoroutine(SubtleBounceRoutine());
    }

    private IEnumerator SubtleBounceRoutine()
    {
        const float duration = 0.22f;
        const float height = 0.035f;
        Vector3 basePosition = _originalLocalPosition;
        Vector3 localUp = Vector3.up;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.localPosition = basePosition + localUp * (Mathf.Sin(t * Mathf.PI) * height);
            yield return null;
        }

        transform.localPosition = basePosition;
        _subtleBounceCoroutine = null;
    }

    public void SetHighlight(bool active) 
    {
        if (active)
        {
            // Highlight is deliberately outline-only. The original Feel scale
            // feedback made adjacent tiles grow into large white blocks.
            try { if (_highlightFeedback != null && _highlightFeedback.IsPlaying) _highlightFeedback.StopFeedbacks(); } catch { }
            transform.localScale = _originalLocalScale;
            transform.localPosition = _originalLocalPosition;
            _outline.enabled = true;
        }
        else
        {
            try { if (_highlightFeedback != null) _highlightFeedback.StopFeedbacks(); } catch {}
            _outline.enabled = false;
        }
    }

    public void SetColor(Color color, bool isCorrectStep = false)
    {
        if (_renderer == null) return;
        if (_isRevealedCorrect && color == Color.white) return; 
        
        WriteMaterialColor(GetWritableMaterial(), color);
        
        if (isCorrectStep) 
        {
            _isRevealedCorrect = true;
            if (_correctFeedback != null) 
            {
                ResetTransformImmediate();
                try { _correctFeedback.PlayFeedbacks(); } catch {}
            }
        }
    }

    public void ApplyTemporaryRed()
    {
        WriteMaterialColor(GetWritableMaterial(), Color.red);
        if (_wrongFeedback != null) 
        {
            ResetTransformImmediate();
            try { _wrongFeedback.PlayFeedbacks(); } catch {}
        }
        SetHighlight(false);
    }

    public void RestoreColor()
    {
        if (_renderer == null) return;
        WriteMaterialColor(GetWritableMaterial(), _isRevealedCorrect ? Color.green : _surfaceBaseColor);
    }

    public void ResetTile()
    {
        _isRevealedCorrect = false;
        WriteMaterialColor(GetWritableMaterial(), _surfaceBaseColor);
        SetHighlight(false);
        ResetTransformImmediate();
    }

    public void PlaySpawnFeedback()
    {
        if (_spawnFeedback != null) 
        {
            ResetTransformImmediate();
            try { _spawnFeedback.PlayFeedbacks(); } catch {}
        }
    }

    public void PlayDespawnEffect()
    {
        ResetTransformImmediate();
        if (_despawnFeedback != null) 
        {
            try { _despawnFeedback.PlayFeedbacks(); } catch {}
        }
        else
        {
            Destroy(gameObject, 0.2f);
        }
    }
}
