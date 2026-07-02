using System.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ResonanceRing : NetworkBehaviour
{
    [SerializeField] private SOResonanceRingConfig _config;
    [SerializeField] private Transform _boostDirection;
    [SerializeField] private Renderer[] _visuals;
    [SerializeField] private Color _idleColor = new(0.3f, 0.7f, 1f);
    [SerializeField] private Color _halfColor = new(1f, 0.65f, 0.1f);
    [SerializeField] private Color _completeColor = Color.white;
    [SerializeField] private bool _completeLevelOnSuccess;

    private readonly NetworkVariable<byte> _visualState = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private ulong _firstPlayerId;
    private float _firstPassTime;
    private bool _completed;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
        if (_visuals == null || _visuals.Length == 0)
        {
            _visuals = GetComponentsInChildren<Renderer>(true);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _visualState.OnValueChanged += HandleVisualStateChanged;
        ApplyVisual(_visualState.Value);
    }

    public override void OnNetworkDespawn()
    {
        _visualState.OnValueChanged -= HandleVisualStateChanged;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsServer || _visualState.Value != 1) return;
        float window = _config != null ? _config.ActivationWindow : 2.5f;
        if (Time.time - _firstPassTime > window) ResetWindow();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || _completed) return;

        var flight = other.GetComponentInParent<Level04FlightController>();
        if (flight == null || !flight.IsSpawned || !flight.FlightEnabled) return;

        ulong clientId = flight.OwnerClientId;
        if (Level04FlowManager.Instance != null
            && Level04FlowManager.Instance.CanUseHostSoloDebug(clientId))
        {
            _firstPlayerId = clientId;
            EventBus.RaiseLevel04RingActivated(name, clientId, true);
            ActivateTeamBoost();
            return;
        }

        if (_visualState.Value == 0)
        {
            _firstPlayerId = clientId;
            _firstPassTime = Time.time;
            _visualState.Value = 1;
            EventBus.RaiseLevel04RingActivated(name, clientId, true);
            return;
        }

        if (clientId == _firstPlayerId) return;

        float activationWindow = _config != null ? _config.ActivationWindow : 2.5f;
        if (Time.time - _firstPassTime > activationWindow)
        {
            _firstPlayerId = clientId;
            _firstPassTime = Time.time;
            return;
        }

        ActivateTeamBoost();
    }

    private void ActivateTeamBoost()
    {
        _completed = true;
        _visualState.Value = 2;

        Vector3 direction = _boostDirection != null ? _boostDirection.forward : transform.forward;
        float force = _config != null ? _config.TeamBoostForce : 14f;
        float lift = _config != null ? _config.TeamLiftForce : 7f;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            client.PlayerObject.GetComponent<Level04FlightController>()
                ?.ApplyBoostServer(direction, force, lift);
        }

        EventBus.RaiseLevel04RingActivated(name, _firstPlayerId, true);
        if (_completeLevelOnSuccess)
        {
            Level04FlowManager.Instance?.CompleteFlightServer();
        }
        else if (_config == null || !_config.OneShot)
        {
            StartCoroutine(ResetAfterDelay());
        }
    }

    private IEnumerator ResetAfterDelay()
    {
        yield return new WaitForSeconds(1.5f);
        _completed = false;
        ResetWindow();
    }

    private void ResetWindow()
    {
        _firstPlayerId = 0;
        _firstPassTime = 0f;
        _visualState.Value = 0;
    }

    private void HandleVisualStateChanged(byte previous, byte current)
    {
        ApplyVisual(current);
    }

    private void ApplyVisual(byte state)
    {
        Color color = state switch
        {
            1 => _halfColor,
            2 => _completeColor,
            _ => _idleColor
        };

        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        foreach (var visual in _visuals)
        {
            if (visual != null) visual.SetPropertyBlock(block);
        }
    }
}
