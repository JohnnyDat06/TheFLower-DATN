using Unity.Netcode;
using UnityEngine;

/// <summary>
/// An interactable puzzle item that can be activated either by player contact or
/// by the regular interaction input. The server owns collection state.
/// </summary>
[DisallowMultipleComponent]
public sealed class TouchCollectibleInteractable : InteractableBase
{
    [Header("Touch Collection")]
    [Tooltip("Trigger on this GameObject used to detect player contact.")]
    [SerializeField] private Collider _touchTrigger;

    [Tooltip("Renderers and particles below this root disappear after collection. Defaults to this object.")]
    [SerializeField] private Transform _visualRoot;

    private Renderer[] _renderers;
    private bool[] _rendererEnabledStates;
    private Collider[] _colliders;
    private bool[] _colliderEnabledStates;
    private ParticleSystem[] _particles;
    private bool[] _particlePlayingStates;

    protected override void Awake()
    {
        base.Awake();

        if (_touchTrigger == null)
            _touchTrigger = GetComponent<Collider>();

        Transform presentationRoot = _visualRoot != null ? _visualRoot : transform;
        _renderers = presentationRoot.GetComponentsInChildren<Renderer>(true);
        _colliders = GetComponentsInChildren<Collider>(true);
        _particles = presentationRoot.GetComponentsInChildren<ParticleSystem>(true);

        _rendererEnabledStates = CaptureEnabledStates(_renderers);
        _colliderEnabledStates = CaptureEnabledStates(_colliders);
        _particlePlayingStates = CapturePlayingStates(_particles);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ApplyCollectedPresentation(IsActivated);

        if (_touchTrigger != null && !_touchTrigger.isTrigger)
        {
            Debug.LogWarning(
                $"[TouchCollectibleInteractable] Touch collider on '{name}' must have Is Trigger enabled. " +
                "Regular button interaction will still work.",
                this);
        }
    }

    public override void Interact(ulong playerId)
    {
        if (!CanInteract) return;

        if (IsServer)
        {
            TryCollectForPlayer(playerId);
            return;
        }

        RequestCollectionServerRpc(playerId);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || !IsSpawned || !CanInteract) return;
        if (!TryResolvePlayer(other, out ulong playerId)) return;

        TryCollectForPlayer(playerId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestCollectionServerRpc(ulong playerId, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != playerId) return;
        TryCollectForPlayer(playerId);
    }

    private void TryCollectForPlayer(ulong playerId)
    {
        if (!IsServer || !CanInteract || !CanPlayerInteract(playerId)) return;
        ServerActivate();
    }

    private bool TryResolvePlayer(Collider other, out ulong playerId)
    {
        playerId = default;
        if (other == null || NetworkManager == null) return false;

        foreach (var pair in NetworkManager.ConnectedClients)
        {
            NetworkObject playerObject = pair.Value.PlayerObject;
            if (playerObject == null) continue;

            Transform playerTransform = playerObject.transform;
            if (other.transform != playerTransform && !other.transform.IsChildOf(playerTransform)) continue;

            playerId = pair.Key;
            return true;
        }

        return false;
    }

    protected override void OnActivatedValueChanged(bool previousValue, bool newValue)
    {
        base.OnActivatedValueChanged(previousValue, newValue);
        ApplyCollectedPresentation(newValue);
    }

    private void ApplyCollectedPresentation(bool collected)
    {
        SetRendererVisibility(!collected);
        SetColliderAvailability(!collected);
        SetParticleVisibility(!collected);
    }

    private void SetRendererVisibility(bool visible)
    {
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].enabled = visible && _rendererEnabledStates[i];
        }
    }

    private void SetColliderAvailability(bool available)
    {
        for (int i = 0; i < _colliders.Length; i++)
        {
            if (_colliders[i] != null)
                _colliders[i].enabled = available && _colliderEnabledStates[i];
        }
    }

    private void SetParticleVisibility(bool visible)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ParticleSystem particle = _particles[i];
            if (particle == null) continue;

            if (visible && _particlePlayingStates[i])
                particle.Play(true);
            else if (!visible)
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private static bool[] CaptureEnabledStates(Renderer[] renderers)
    {
        bool[] states = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            states[i] = renderers[i] != null && renderers[i].enabled;
        return states;
    }

    private static bool[] CaptureEnabledStates(Collider[] colliders)
    {
        bool[] states = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
            states[i] = colliders[i] != null && colliders[i].enabled;
        return states;
    }

    private static bool[] CapturePlayingStates(ParticleSystem[] particles)
    {
        bool[] states = new bool[particles.Length];
        for (int i = 0; i < particles.Length; i++)
            states[i] = particles[i] != null && (particles[i].isPlaying || particles[i].main.playOnAwake);
        return states;
    }

    private void OnValidate()
    {
        if (_touchTrigger == null)
            _touchTrigger = GetComponent<Collider>();
    }
}
