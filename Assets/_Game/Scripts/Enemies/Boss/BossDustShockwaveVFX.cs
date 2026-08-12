using UnityEngine;

/// <summary>
/// Visual-only travelling sand shockwave that reuses the project's stylized smoke-puff assets.
/// It owns no collision, damage, gameplay state or networking.
/// </summary>
public sealed class BossDustShockwaveVFX : MonoBehaviour
{
    [Header("Shockwave Shape")]
    [Tooltip("Quang duong toi da cua vet bui chay tren mat san.")]
    [SerializeField, Min(0.1f)] private float _radius = 28f;
    [Tooltip("Toc do vet bui chay tren mat san khi khong nhan toc do tu Shockwave gameplay.")]
    [SerializeField, Min(0.1f)] private float _travelSpeed = 16f;
    [Tooltip("Do rong cua dai bui phu theo chieu ngang Shockwave.")]
    [SerializeField, Min(0.1f)] private float _width = 4f;
    [Tooltip("Thoi gian toi da mot puff bui ton tai sau khi duoc phun ra.")]
    [SerializeField, Range(0.1f, 3f)] private float _duration = 1.35f;
    [Tooltip("Mat do puff bui tren duong di. Gia tri cao tao vet bui day hon.")]
    [SerializeField, Min(1)] private int _particleAmount = 28;
    [Tooltip("Chieu cao puff bui so voi mat san. Giu thap de khong che tam nhin.")]
    [SerializeField, Min(0.01f)] private float _dustHeight = 0.38f;
    [Tooltip("Mau tint cat ap dung len cac puff khoi stylized cua project.")]
    [SerializeField] private Color _color = new(0.95f, 0.57f, 0.24f, 0.8f);

    [Header("Project VFX Sources")]
    [Tooltip("Puff khoi phun tai diem paw impact.")]
    [SerializeField] private GameObject _impactPuffPrefab;
    [Tooltip("Puff khoi be, nam sat san va lap lai tren duong Shockwave.")]
    [SerializeField] private GameObject _groundPuffPrefab;

    private float _activeSpeed;
    private float _activeRange;
    private float _travelledDistance;
    private float _nextPuffDistance;
    private bool _isPlaying;

    private void OnEnable()
    {
        Play();
    }

    private void Update()
    {
        if (!_isPlaying) return;

        float step = _activeSpeed * Time.deltaTime;
        transform.position += transform.forward * step;
        _travelledDistance += step;

        while (_travelledDistance >= _nextPuffDistance && _nextPuffDistance <= _activeRange)
        {
            SpawnGroundPuff();
            _nextPuffDistance += ResolvePuffSpacing();
        }

        if (_travelledDistance < _activeRange) return;

        _isPlaying = false;
        Destroy(gameObject);
    }

    /// <summary>Configures the local visual to match an already-spawned gameplay Shockwave.</summary>
    public void ConfigureTravel(Vector3 direction, float speed, float width, float maxRange)
    {
        Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (planarDirection.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(planarDirection.normalized, Vector3.up);

        _activeSpeed = Mathf.Max(0.1f, speed);
        _width = Mathf.Max(0.1f, width);
        _activeRange = Mathf.Min(_radius, Mathf.Max(0.1f, maxRange));
    }

    /// <summary>Starts the visual effect without applying collision, damage or gameplay events.</summary>
    public void Play()
    {
        _activeSpeed = _activeSpeed > 0f ? _activeSpeed : _travelSpeed;
        _activeRange = _activeRange > 0f ? _activeRange : _radius;
        _travelledDistance = 0f;
        _nextPuffDistance = 0f;
        _isPlaying = true;
        SpawnImpactPuff();
    }

    private void SpawnImpactPuff()
    {
        Vector3 scale = new(_width * 0.4f, _dustHeight * 2.2f, _width * 0.4f);
        SpawnPuff(_impactPuffPrefab, transform.position, scale);
    }

    private void SpawnGroundPuff()
    {
        Vector3 position = transform.position + Vector3.up * (_dustHeight * 0.16f);
        Vector3 scale = new(_width * 0.62f, _dustHeight * 1.35f, _width * 0.78f);
        SpawnPuff(_groundPuffPrefab, position, scale);
    }

    private void SpawnPuff(GameObject sourcePrefab, Vector3 position, Vector3 scale)
    {
        if (sourcePrefab == null) return;

        // Circle Flat has an authored 90-degree local rotation so its smoke plane lies on the floor.
        // Preserve that rotation, then apply the Shockwave heading around the vertical axis.
        Quaternion puffRotation = transform.rotation * sourcePrefab.transform.rotation;
        GameObject puff = Instantiate(sourcePrefab, position, puffRotation);
        puff.transform.localScale = scale;
        ApplySandTint(puff);
        Destroy(puff, _duration);
    }

    private void ApplySandTint(GameObject puff)
    {
        foreach (ParticleSystem particles in puff.GetComponentsInChildren<ParticleSystem>())
        {
            ParticleSystem.MainModule main = particles.main;
            main.startColor = _color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
        }
    }

    private float ResolvePuffSpacing()
    {
        float desiredPuffs = Mathf.Max(1f, _particleAmount * 0.7f);
        return Mathf.Max(_width * 0.3f, _activeRange / desiredPuffs);
    }
}
