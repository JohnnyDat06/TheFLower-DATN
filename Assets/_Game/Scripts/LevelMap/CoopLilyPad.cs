using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Trạng thái hiển thị được đồng bộ của một lá sen co-op.</summary>
public enum CoopLilyPadState : byte
{
    Submerged,
    Surfaced,
    Occupied,
    Sinking,
    Rising
}

/// <summary>
/// Lá sen co-op do server điều khiển. Người đứng trên lá sẽ làm nổi các lá liên kết;
/// một lá đang được liên kết giữ nổi sẽ không chìm dù đang có người đứng trên đó.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
public sealed class CoopLilyPad : NetworkBehaviour
{
    private const int MaxDetectedColliders = 16;
    private const float PositionEpsilon = 0.001f;

    [Header("Initial State")]
    [Tooltip("Bật nếu lá này nổi sẵn khi bắt đầu màn chơi.")]
    [SerializeField] private bool _startsSurfaced;

    [Tooltip("Khoảng cách lá di chuyển xuống dưới khi chìm hoàn toàn.")]
    [SerializeField, Min(0.01f)] private float _submergeDistance = 2f;

    [Header("Timing")]
    [Tooltip("Thời gian người chơi được đứng trước khi lá bắt đầu chìm.")]
    [SerializeField, Min(0f)] private float _sinkDelay = 0.5f;

    [Tooltip("Thời gian đi từ nổi hoàn toàn đến chìm hoàn toàn.")]
    [SerializeField, Min(0.01f)] private float _sinkDuration = 3f;

    [Tooltip("Thời gian đi từ chìm hoàn toàn đến nổi hoàn toàn.")]
    [SerializeField, Min(0.01f)] private float _riseDuration = 1f;

    [Tooltip("Thời gian giữ các lá liên kết nổi sau khi người kích hoạt rời lá.")]
    [SerializeField, Min(0f)] private float _linkedHoldDuration = 1.5f;

    [Header("Co-op Links")]
    [Tooltip("Các lá sẽ nổi lên khi có người đứng trên lá này.")]
    [SerializeField] private List<CoopLilyPad> _linkedLilyPads = new();

    [Header("Player Detection")]
    [Tooltip("Layer của collider Player. Thu hẹp mask này để giảm chi phí kiểm tra vật lý.")]
    [SerializeField] private LayerMask _playerLayerMask = ~0;

    [Tooltip("Tâm vùng phát hiện người đứng, tính theo local space của lá.")]
    [SerializeField] private Vector3 _standingAreaCenter = new(0f, 0.5f, 0f);

    [Tooltip("Kích thước vùng phát hiện người đứng trên mặt lá.")]
    [SerializeField] private Vector3 _standingAreaSize = new(2f, 1f, 2f);

    [Header("Client Smoothing")]
    [Tooltip("Tốc độ client làm mượt về vị trí do server đồng bộ.")]
    [SerializeField, Min(0f)] private float _clientSmoothing = 20f;

    private readonly NetworkVariable<CoopLilyPadState> _networkState = new(
        CoopLilyPadState.Submerged,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> _networkHeight = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<byte> _networkOccupantCount = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly HashSet<ulong> _playersOnPad = new();
    private readonly HashSet<CoopLilyPad> _activationSources = new();
    private readonly Collider[] _detectedColliders = new Collider[MaxDetectedColliders];

    private Rigidbody _rigidbody;
    private Vector3 _surfacePosition;
    private Vector3 _submergedPosition;
    private float _renderedHeight;
    private float _occupiedDuration;
    private float _linkedHoldRemaining;
    private bool _linksAreActive;

    /// <summary>Trạng thái hiện tại đã được server đồng bộ.</summary>
    public CoopLilyPadState State => _networkState.Value;

    /// <summary>True khi server đang phát hiện ít nhất một người chơi trên lá.</summary>
    public bool HasPlayer => _networkOccupantCount.Value > 0;

    /// <summary>Độ nổi đã đồng bộ, từ 0 (chìm) đến 1 (nổi).</summary>
    public float NormalizedHeight => _networkHeight.Value;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        _surfacePosition = transform.position;
        _submergedPosition = _surfacePosition + Vector3.down * _submergeDistance;
        _renderedHeight = _startsSurfaced ? 1f : 0f;
        ApplyPosition(_renderedHeight, false);

        if (GetComponentInChildren<Collider>() == null)
        {
            Debug.LogError($"[{nameof(CoopLilyPad)}] '{name}' cần ít nhất một Collider để người chơi đứng lên.", this);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            float initialHeight = _startsSurfaced ? 1f : 0f;
            _networkHeight.Value = initialHeight;
            _networkState.Value = _startsSurfaced
                ? CoopLilyPadState.Surfaced
                : CoopLilyPadState.Submerged;
            _networkOccupantCount.Value = 0;
        }

        _renderedHeight = _networkHeight.Value;
        ApplyPosition(_renderedHeight, false);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            SetLinkedPadsActive(false);
            _activationSources.Clear();
            _playersOnPad.Clear();
        }

        base.OnNetworkDespawn();
    }

    private void FixedUpdate()
    {
        if (!IsSpawned) return;

        if (IsServer)
        {
            TickServer(Time.fixedDeltaTime);
            _renderedHeight = _networkHeight.Value;
        }
        else
        {
            float smoothingFactor = 1f - Mathf.Exp(-_clientSmoothing * Time.fixedDeltaTime);
            _renderedHeight = Mathf.Lerp(_renderedHeight, _networkHeight.Value, smoothingFactor);
        }

        ApplyPosition(_renderedHeight, true);
    }

    private void TickServer(float deltaTime)
    {
        RefreshPlayersOnPad();
        bool hasPlayer = _playersOnPad.Count > 0;
        UpdateLinkedPads(hasPlayer, deltaTime);

        bool isExternallyHeld = _activationSources.Count > 0;
        float targetHeight = ResolveTargetHeight(hasPlayer, isExternallyHeld, deltaTime);
        MoveTowardsTarget(targetHeight, deltaTime);
        UpdateNetworkState(hasPlayer, isExternallyHeld, targetHeight);
    }

    private void RefreshPlayersOnPad()
    {
        _playersOnPad.Clear();

        Vector3 center = transform.TransformPoint(_standingAreaCenter);
        Vector3 scale = transform.lossyScale;
        Vector3 halfExtents = new(
            Mathf.Abs(_standingAreaSize.x * scale.x) * 0.5f,
            Mathf.Abs(_standingAreaSize.y * scale.y) * 0.5f,
            Mathf.Abs(_standingAreaSize.z * scale.z) * 0.5f);

        int hitCount = Physics.OverlapBoxNonAlloc(
            center,
            halfExtents,
            _detectedColliders,
            transform.rotation,
            _playerLayerMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider detectedCollider = _detectedColliders[i];
            _detectedColliders[i] = null;
            if (detectedCollider == null) continue;

            NetworkObject playerObject = detectedCollider.GetComponentInParent<NetworkObject>();
            if (playerObject == null || !playerObject.IsSpawned || !playerObject.IsPlayerObject) continue;
            if (!NetworkManager.ConnectedClients.ContainsKey(playerObject.OwnerClientId)) continue;

            _playersOnPad.Add(playerObject.OwnerClientId);
        }

        byte occupantCount = (byte)Mathf.Min(_playersOnPad.Count, byte.MaxValue);
        if (_networkOccupantCount.Value != occupantCount)
        {
            _networkOccupantCount.Value = occupantCount;
        }
    }

    private void UpdateLinkedPads(bool hasPlayer, float deltaTime)
    {
        if (hasPlayer)
        {
            _linksAreActive = true;
            _linkedHoldRemaining = _linkedHoldDuration;
            SetLinkedPadsActive(true);
            return;
        }

        if (!_linksAreActive) return;

        _linkedHoldRemaining = Mathf.Max(0f, _linkedHoldRemaining - deltaTime);
        if (_linkedHoldRemaining > 0f) return;

        _linksAreActive = false;
        SetLinkedPadsActive(false);
    }

    private float ResolveTargetHeight(bool hasPlayer, bool isExternallyHeld, float deltaTime)
    {
        if (isExternallyHeld)
        {
            _occupiedDuration = 0f;
            return 1f;
        }

        if (!hasPlayer)
        {
            _occupiedDuration = 0f;
            return _startsSurfaced ? 1f : 0f;
        }

        _occupiedDuration += deltaTime;

        // Giữ nguyên độ cao hiện tại trong khoảng trễ; việc có người đứng không tự làm
        // một lá vốn đang chìm nổi lên nếu lá đó không được lá khác kích hoạt.
        return _occupiedDuration < _sinkDelay ? _networkHeight.Value : 0f;
    }

    private void MoveTowardsTarget(float targetHeight, float deltaTime)
    {
        float currentHeight = _networkHeight.Value;
        float duration = targetHeight > currentHeight ? _riseDuration : _sinkDuration;
        float nextHeight = Mathf.MoveTowards(currentHeight, targetHeight, deltaTime / duration);

        if (!Mathf.Approximately(currentHeight, nextHeight))
        {
            _networkHeight.Value = nextHeight;
        }
    }

    private void UpdateNetworkState(bool hasPlayer, bool isExternallyHeld, float targetHeight)
    {
        float currentHeight = _networkHeight.Value;
        CoopLilyPadState nextState;

        if (currentHeight < targetHeight - PositionEpsilon)
        {
            nextState = CoopLilyPadState.Rising;
        }
        else if (currentHeight > targetHeight + PositionEpsilon)
        {
            nextState = CoopLilyPadState.Sinking;
        }
        else if (hasPlayer && (isExternallyHeld || _occupiedDuration < _sinkDelay))
        {
            nextState = CoopLilyPadState.Occupied;
        }
        else if (currentHeight <= PositionEpsilon)
        {
            nextState = CoopLilyPadState.Submerged;
        }
        else
        {
            nextState = CoopLilyPadState.Surfaced;
        }

        if (_networkState.Value != nextState)
        {
            _networkState.Value = nextState;
        }
    }

    private void SetLinkedPadsActive(bool active)
    {
        for (int i = 0; i < _linkedLilyPads.Count; i++)
        {
            CoopLilyPad linkedPad = _linkedLilyPads[i];
            if (linkedPad == null || linkedPad == this || !linkedPad.IsSpawned) continue;

            linkedPad.SetActivationSource(this, active);
        }
    }

    private void SetActivationSource(CoopLilyPad source, bool active)
    {
        if (!IsServer || source == null || source == this) return;

        if (active)
        {
            _activationSources.Add(source);
        }
        else
        {
            _activationSources.Remove(source);
        }
    }

    private void ApplyPosition(float normalizedHeight, bool usePhysics)
    {
        Vector3 targetPosition = Vector3.Lerp(
            _submergedPosition,
            _surfacePosition,
            Mathf.Clamp01(normalizedHeight));

        if (usePhysics && _rigidbody != null)
        {
            _rigidbody.MovePosition(targetPosition);
        }
        else
        {
            transform.position = targetPosition;
        }
    }

    private void OnValidate()
    {
        _submergeDistance = Mathf.Max(0.01f, _submergeDistance);
        _sinkDelay = Mathf.Max(0f, _sinkDelay);
        _sinkDuration = Mathf.Max(0.01f, _sinkDuration);
        _riseDuration = Mathf.Max(0.01f, _riseDuration);
        _linkedHoldDuration = Mathf.Max(0f, _linkedHoldDuration);
        _clientSmoothing = Mathf.Max(0f, _clientSmoothing);
        _standingAreaSize = new Vector3(
            Mathf.Max(0.01f, _standingAreaSize.x),
            Mathf.Max(0.01f, _standingAreaSize.y),
            Mathf.Max(0.01f, _standingAreaSize.z));
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(
            transform.TransformPoint(_standingAreaCenter),
            transform.rotation,
            transform.lossyScale);
        Gizmos.DrawCube(Vector3.zero, _standingAreaSize);
        Gizmos.DrawWireCube(Vector3.zero, _standingAreaSize);
        Gizmos.matrix = previousMatrix;
    }
}
