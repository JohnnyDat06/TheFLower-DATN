using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class MagicMirrorController : NetworkBehaviour
{
    [Header("Applied to the effects at start")]
    [SerializeField] private Color mirrorEffectColor;

    [Header("Changing these might `break` the effects")]
    [Space(20)]
    [SerializeField] private Renderer mirrorRenderer;
    [SerializeField] private ParticleSystem[] effectsPartSystems;
    [SerializeField] private Light mirrorLight;
    [SerializeField] private Transform symbolTF;
    [SerializeField] private AudioSource mirrorAudio, flashAudio;

    // Network variable to sync state across all clients
    private NetworkVariable<bool> isMirrorActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float transitionF, lightF;
    private Material mirrorMat;
    private Renderer _symbolRenderer;
    private MaterialPropertyBlock _mirrorProperties;
    private MaterialPropertyBlock _effectProperties;
    private MaterialPropertyBlock _symbolProperties;

    private Vector3 symbolStartPos;

    private Coroutine transitionCor, symbolMovementCor;

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[MagicMirror] OnNetworkSpawn called. IsServer: {IsServer}");

        if (mirrorRenderer == null || symbolTF == null || mirrorLight == null)
        {
            Debug.LogError("[MagicMirror] Missing references in Inspector! Please assign all fields.");
            return;
        }

        // Never use Renderer.materials here: it creates unique material instances and
        // duplicates every referenced texture for each network peer.
        Material[] sharedMats = mirrorRenderer.sharedMaterials;
        if (sharedMats.Length < 2 || sharedMats[0] == null || sharedMats[1] == null)
        {
            Debug.LogError("[MagicMirror] Renderer must have at least 2 shared materials!");
            return;
        }

        mirrorMat = sharedMats[0];
        _mirrorProperties = new MaterialPropertyBlock();
        _effectProperties = new MaterialPropertyBlock();
        _symbolProperties = new MaterialPropertyBlock();

        _mirrorProperties.SetColor("_EmissionColor", mirrorEffectColor);
        _mirrorProperties.SetFloat("_EmissionStrength", 0f);
        _effectProperties.SetColor("_ColorMain", mirrorEffectColor);
        _effectProperties.SetFloat("_PortalFade", 0f);
        mirrorRenderer.SetPropertyBlock(_mirrorProperties, 0);
        mirrorRenderer.SetPropertyBlock(_effectProperties, 1);

        symbolStartPos = symbolTF.localPosition;
        _symbolRenderer = symbolTF.GetComponent<Renderer>();
        if (_symbolRenderer != null)
        {
            _symbolRenderer.sharedMaterial = mirrorMat;
            _symbolProperties.SetColor("_EmissionColor", mirrorEffectColor);
            _symbolProperties.SetFloat("_EmissionStrength", 0f);
            _symbolRenderer.SetPropertyBlock(_symbolProperties);
        }

        mirrorLight.color = mirrorEffectColor;
        lightF = mirrorLight.intensity;
        mirrorLight.intensity = 0;

        foreach (ParticleSystem part in effectsPartSystems)
        {
            if (part == null) continue;
            ParticleSystem.MainModule mod = part.main;
            mod.startColor = mirrorEffectColor;
        }

        isMirrorActive.OnValueChanged += OnMirrorStateChanged;
        Debug.Log("[MagicMirror] Subscribed to NetworkVariable changes.");

        if (isMirrorActive.Value)
        {
            ApplyMirrorState(true, true);
        }
    }

    public override void OnNetworkDespawn()
    {
        isMirrorActive.OnValueChanged -= OnMirrorStateChanged;
    }

    private void OnMirrorStateChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[MagicMirror] State changed from {previousValue} to {newValue}");
        ApplyMirrorState(newValue);
    }

    // Call this from Server/Host via the Trigger
    public void ToggleMirror(bool _activate)
    {
        if (!IsServer) return;
        Debug.Log($"[MagicMirror] Server setting state to: {_activate}");
        isMirrorActive.Value = _activate;
    }

    private void ApplyMirrorState(bool _activate, bool immediate = false)
    {
        Debug.Log($"[MagicMirror] Applying state: {_activate} (Immediate: {immediate})");
        
        if (_activate)
        {
            foreach (ParticleSystem part in effectsPartSystems)
            {
                if (part != null) part.Play();
            }

            if (mirrorAudio != null) mirrorAudio.Play();
            if (flashAudio != null) flashAudio.Play();

            if (symbolMovementCor != null) StopCoroutine(symbolMovementCor);
            symbolMovementCor = StartCoroutine(SymbolMovement());
        }
        else
        {
            foreach (ParticleSystem part in effectsPartSystems)
            {
                if (part != null) part.Stop();
            }
        }

        if (transitionCor != null) StopCoroutine(transitionCor);
        transitionCor = StartCoroutine(MirrorTransition(_activate, immediate));
    }

    IEnumerator MirrorTransition(bool _active, bool immediate)
    {
        if (immediate)
        {
            transitionF = _active ? 1f : 0f;
            UpdateVisuals(transitionF);
        }
        else
        {
            if (_active)
            {
                while (transitionF < 1f)
                {
                    transitionF = Mathf.MoveTowards(transitionF, 1, Time.deltaTime * 0.5f);
                    UpdateVisuals(transitionF);
                    yield return null;
                }
            }
            else
            {
                while (transitionF > 0f)
                {
                    transitionF = Mathf.MoveTowards(transitionF, 0f, Time.deltaTime * 0.8f);
                    UpdateVisuals(transitionF);
                    yield return null;
                }
                if (mirrorAudio != null) mirrorAudio.Stop();
                if (symbolMovementCor != null) StopCoroutine(symbolMovementCor);
            }
        }

    }

    private void UpdateVisuals(float value)
    {
        if (mirrorRenderer != null && _mirrorProperties != null)
        {
            _mirrorProperties.SetFloat("_EmissionStrength", value);
            mirrorRenderer.SetPropertyBlock(_mirrorProperties, 0);
            _effectProperties.SetFloat("_PortalFade", value * 0.4f);
            mirrorRenderer.SetPropertyBlock(_effectProperties, 1);
        }

        if (_symbolRenderer != null && _symbolProperties != null)
        {
            _symbolProperties.SetFloat("_EmissionStrength", value);
            _symbolRenderer.SetPropertyBlock(_symbolProperties);
        }

        if (mirrorLight != null) mirrorLight.intensity = lightF * value;
        if (mirrorAudio != null) mirrorAudio.volume = value * 0.8f;
    }

    private IEnumerator SymbolMovement()
    {
        Vector3 randomPos = symbolStartPos;
        float lerpF = 0;

        while (true)
        {
            if (Vector3.Distance(symbolTF.localPosition, randomPos) < 0.01f)
            {
                randomPos = symbolStartPos + new Vector3(0, Random.Range(-0.08f, 0.08f), Random.Range(-0.08f, 0.08f));
                lerpF = 0f;
            }
            else
            {
                symbolTF.localPosition = Vector3.Slerp(symbolTF.localPosition, randomPos, lerpF);
                lerpF += 0.01f;
            }

            yield return new WaitForSeconds(0.04f);
        }
    }
}
