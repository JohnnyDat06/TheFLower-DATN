using System.Collections;

using UnityEngine.Rendering;
using UnityEngine;

/// <summary>Local presentation for the replicated state of a two-lever door.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CoopInteractable))]
public sealed class CoopLeverVisualFeedback : MonoBehaviour
{
    private static readonly Color HostColor = new(0.15f, 0.95f, 1f, 1f);
    private static readonly Color ClientColor = new(1f, 0.58f, 0.16f, 1f);
    private static readonly Color WaitingColor = new(0.08f, 0.18f, 0.18f, 0.55f);

    [SerializeField] private float _beamWidth = 0.085f;
    [SerializeField] private float _readyLightIntensity = 4.5f;
    [SerializeField] private float _waitingLightIntensity = 0.35f;
    [SerializeField] private float _pulseSpeed = 3.4f;

    private CoopInteractable _interactable;
    private Transform _leverA;
    private Transform _leverB;
    private Transform _nodeA;
    private Transform _nodeB;
    private Transform _core;
    private LineRenderer _beamA;
    private LineRenderer _beamB;
    private Light _lightA;
    private Light _lightB;
    private Light _coreLight;
    private ParticleSystem _completionBurst;
    private Material _glowMaterial;
    private bool _lastA;
    private bool _lastB;
    private bool _completionPlayed;
    private bool _presentationHidden;

    private void Awake()
    {
        _interactable = GetComponent<CoopInteractable>();
        _leverA = transform.Find("Lever_A");
        _leverB = transform.Find("Lever_B");
        if (_leverA == null || _leverB == null)
        {
            Debug.LogWarning($"[CoopLeverVisualFeedback] Lever_A/Lever_B missing on {name}.", this);
            enabled = false;
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        _glowMaterial = new Material(shader) { name = $"{name}_CoopGlow_Runtime", hideFlags = HideFlags.DontSave };
        _glowMaterial.SetColor("_BaseColor", Color.white);
        _glowMaterial.SetColor("_Color", Color.white);
        _glowMaterial.SetFloat("_Surface", 1f);
        _glowMaterial.SetFloat("_Blend", 1f);
        _glowMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        _glowMaterial.SetInt("_DstBlend", (int)BlendMode.One);
        _glowMaterial.SetInt("_ZWrite", 0);
        _glowMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _glowMaterial.renderQueue = (int)RenderQueue.Transparent;
        BuildPresentation();
        ApplyState(true);
    }

    private void OnDestroy()
    {
        if (_glowMaterial != null) Destroy(_glowMaterial);
    }

    private void Update()
    {
        if (_interactable == null) return;
        if (_interactable.IsActivated)
        {
            if (!_completionPlayed)
            {
                _completionPlayed = true;
                StartCoroutine(PlayCompletion());
            }
            return;
        }

        if (_presentationHidden)
        {
            _presentationHidden = false;
            SetPresentationVisible(true);
        }

        bool readyA = _interactable.PlayerAReady;
        bool readyB = _interactable.PlayerBReady;
        if (readyA != _lastA || readyB != _lastB) ApplyState(false);
        AnimatePulse(readyA, readyB);
        _completionPlayed = false;
    }

    private void BuildPresentation()
    {
        Transform visualRoot = new GameObject("CoopEnergyFeedback").transform;
        visualRoot.SetParent(transform, false);
        Vector3 localA = transform.InverseTransformPoint(_leverA.position) + Vector3.up * 0.75f;
        Vector3 localB = transform.InverseTransformPoint(_leverB.position) + Vector3.up * 0.75f;
        Vector3 center = (localA + localB) * 0.5f;
        center.y = Mathf.Max(localA.y, localB.y) + 1.15f;

        _nodeA = CreateGlowOrb(visualRoot, "PlayerA_Node", localA, HostColor, 0.22f, out _lightA);
        _nodeB = CreateGlowOrb(visualRoot, "PlayerB_Node", localB, ClientColor, 0.22f, out _lightB);
        _core = CreateGlowOrb(visualRoot, "Coop_Core", center, Color.white, 0.34f, out _coreLight);
        _beamA = CreateBeam(visualRoot, "EnergyBeam_A", HostColor, localA, center, -0.25f);
        _beamB = CreateBeam(visualRoot, "EnergyBeam_B", ClientColor, localB, center, 0.25f);
        _completionBurst = CreateCompletionBurst(visualRoot, center);
    }

    private Transform CreateGlowOrb(Transform parent, string objectName, Vector3 localPosition, Color color, float scale, out Light pointLight)
    {
        GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = objectName;
        orb.transform.SetParent(parent, false);
        orb.transform.localPosition = localPosition;
        orb.transform.localScale = Vector3.one * scale;
        Collider collider = orb.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        Renderer renderer = orb.GetComponent<Renderer>();
        renderer.sharedMaterial = _glowMaterial;
        ApplyRendererColor(renderer, color);

        pointLight = orb.AddComponent<Light>();
        pointLight.type = LightType.Point;
        pointLight.color = color;
        pointLight.range = 3.5f;
        pointLight.intensity = _waitingLightIntensity;
        pointLight.shadows = LightShadows.None;
        return orb.transform;
    }

    private LineRenderer CreateBeam(Transform parent, string objectName, Color color, Vector3 start, Vector3 end, float sideOffset)
    {
        GameObject beamObject = new(objectName);
        beamObject.transform.SetParent(parent, false);
        LineRenderer beam = beamObject.AddComponent<LineRenderer>();
        beam.useWorldSpace = false;
        beam.sharedMaterial = _glowMaterial;
        beam.positionCount = 3;
        beam.startWidth = _beamWidth;
        beam.endWidth = _beamWidth * 0.55f;
        beam.numCapVertices = 6;
        beam.numCornerVertices = 4;
        Vector3 middle = Vector3.Lerp(start, end, 0.52f) + Vector3.up * 0.42f + Vector3.right * sideOffset;
        beam.SetPosition(0, start);
        beam.SetPosition(1, middle);
        beam.SetPosition(2, end);
        SetBeamColor(beam, color, false);
        return beam;
    }

    private ParticleSystem CreateCompletionBurst(Transform parent, Vector3 localPosition)
    {
        GameObject burstObject = new("CoopCompletionBurst");
        burstObject.SetActive(false);
        burstObject.transform.SetParent(parent, false);
        burstObject.transform.localPosition = localPosition;
        ParticleSystem particles = burstObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.8f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.startColor = new ParticleSystem.MinMaxGradient(HostColor, ClientColor);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 48;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.4f;
        ParticleSystem.ColorOverLifetimeModule colorLife = particles.colorOverLifetime;
        colorLife.enabled = true;
        Gradient gradient = new();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.45f, 1f, 0.75f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorLife.color = gradient;
        particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = _glowMaterial;
        burstObject.SetActive(true);
        return particles;
    }

    private void ApplyState(bool force)
    {
        bool readyA = _interactable.PlayerAReady;
        bool readyB = _interactable.PlayerBReady;
        if (!force && readyA == _lastA && readyB == _lastB) return;
        _lastA = readyA;
        _lastB = readyB;
        _lightA.intensity = readyA ? _readyLightIntensity : _waitingLightIntensity;
        _lightB.intensity = readyB ? _readyLightIntensity : _waitingLightIntensity;
        SetOrbColor(_nodeA, readyA ? HostColor : WaitingColor);
        SetOrbColor(_nodeB, readyB ? ClientColor : WaitingColor);
        SetBeamColor(_beamA, HostColor, readyA);
        SetBeamColor(_beamB, ClientColor, readyB);
        bool bothReady = readyA && readyB;
        _coreLight.color = bothReady ? Color.white : new Color(0.18f, 0.38f, 0.34f);
        _coreLight.intensity = bothReady ? _readyLightIntensity * 1.35f : _waitingLightIntensity * 0.6f;
        SetOrbColor(_core, bothReady ? Color.white : WaitingColor);
    }

    private void AnimatePulse(bool readyA, bool readyB)
    {
        float pulse = 1f + Mathf.Sin(Time.time * _pulseSpeed) * 0.12f;
        _nodeA.localScale = Vector3.one * 0.22f * (readyA ? pulse : 1f);
        _nodeB.localScale = Vector3.one * 0.22f * (readyB ? pulse : 1f);
        float corePulse = readyA && readyB ? 1f + Mathf.Sin(Time.time * (_pulseSpeed + 1.3f)) * 0.2f : 1f;
        _core.localScale = Vector3.one * 0.34f * corePulse;
    }

    private IEnumerator PlayCompletion()
    {
        _completionBurst.Emit(36);
        _beamA.enabled = false;
        _beamB.enabled = false;
        _presentationHidden = true;
        float elapsed = 0f;
        while (elapsed < 0.55f)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / 0.55f);
            _lightA.intensity = Mathf.Lerp(_lightA.intensity, 0f, t);
            _lightB.intensity = Mathf.Lerp(_lightB.intensity, 0f, t);
            _coreLight.intensity = Mathf.Lerp(_coreLight.intensity, 0f, t);
            _nodeA.localScale = Vector3.one * Mathf.Lerp(0.22f, 0f, t);
            _nodeB.localScale = Vector3.one * Mathf.Lerp(0.22f, 0f, t);
            _core.localScale = Vector3.one * Mathf.Lerp(0.34f, 0f, t);
            yield return null;
        }
        SetPresentationVisible(false);
    }

    private void SetPresentationVisible(bool visible)
    {
        _beamA.enabled = visible;
        _beamB.enabled = visible;
        _lightA.enabled = visible;
        _lightB.enabled = visible;
        _coreLight.enabled = visible;
        _nodeA.gameObject.SetActive(visible);
        _nodeB.gameObject.SetActive(visible);
        _core.gameObject.SetActive(visible);
    }

    private static void SetBeamColor(LineRenderer beam, Color color, bool ready)
    {
        Color visible = ready ? color : new Color(color.r, color.g, color.b, 0.12f);
        beam.startColor = visible;
        beam.endColor = new Color(visible.r, visible.g, visible.b, ready ? 0.85f : 0.06f);
    }

    private static void SetOrbColor(Transform orb, Color color) => ApplyRendererColor(orb.GetComponent<Renderer>(), color);

    private static void ApplyRendererColor(Renderer renderer, Color color)
    {
        MaterialPropertyBlock block = new();
        renderer.GetPropertyBlock(block);
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
    }
}
