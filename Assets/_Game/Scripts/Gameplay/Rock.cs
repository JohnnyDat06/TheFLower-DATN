using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(Rigidbody), typeof(SphereCollider), typeof(NetworkObject))]
public class Rock : NetworkBehaviour
{
    private const float AudioListenerLookupInterval = 1f;
    private const float TimerPauseContactTolerance = 0.05f;
    private const float NetworkSyncInterval = 1f / 15f;
    private const float ClientInterpolationSpeed = 18f;
    private const float ClientSnapDistance = 4f;

    [FormerlySerializedAs("positionClone")]
    [SerializeField] private Transform _positionClone;

    [FormerlySerializedAs("endPoint")]
    [SerializeField] private Transform _endPoint;

    [FormerlySerializedAs("timeReset")]
    [SerializeField] private float _timeReset = 15f;

    [FormerlySerializedAs("distanceToEndPoint")]
    [SerializeField] private float _distanceToEndPoint = 15f;

    [SerializeField] private Collider[] _timerPauseColliders;
    [SerializeField] private SOAudioClip _rollingSfx;
    [SerializeField, Min(0.01f)] private float _rollingAudioMinDistance = 3f;
    [SerializeField, Min(0.01f)] private float _rollingAudioMaxDistance = 30f;

    private readonly NetworkVariable<Vector3> _networkPosition = new(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Quaternion> _networkRotation = new(
        Quaternion.identity,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _hasNetworkState = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Rigidbody _rigidbody;
    private SphereCollider _rockCollider;
    private AudioSource _rollingAudioSource;
    private AudioListener _audioListener;
    private Quaternion _startRotation;
    private bool _isTouchingTimerPauseCollider;
    private float _nextAudioListenerLookupTime;
    private float _nextNetworkSyncTime;
    private float _elapsedTime;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _rockCollider = GetComponent<SphereCollider>();
        _startRotation = transform.rotation;
    }

    private void Start()
    {
        if (_positionClone == null)
        {
            Debug.LogError("[Rock] Position Clone chưa được gán.", this);
            enabled = false;
            return;
        }

        if (!IsNetworkSessionActive())
        {
            ConfigurePhysicsForAuthority(true);
            ResetToStartPosition();
        }

        UpdateRollingAudio();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ConfigurePhysicsForAuthority(IsServer);
        if (IsServer)
        {
            ResetToStartPosition();
            PublishNetworkState(true);
            return;
        }

        ApplyNetworkState(true);
    }

    public override void OnNetworkDespawn()
    {
        ConfigurePhysicsForAuthority(true);
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!HasSimulationAuthority())
        {
            ApplyNetworkState(false);
            return;
        }

        if (!_isTouchingTimerPauseCollider)
        {
            _elapsedTime += Time.deltaTime;
        }

        bool reachedEndPoint = HasReachedEndPoint();
        bool reachedTimeLimit = _timeReset > 0f && _elapsedTime >= _timeReset;

        if (reachedEndPoint || reachedTimeLimit)
        {
            ResetToStartPosition();
        }
    }

    private void FixedUpdate()
    {
        SetTimerPauseState(IsRockTouchingTimerPauseCollider());

        if (HasSimulationAuthority() && IsSpawned && IsServer &&
            Time.unscaledTime >= _nextNetworkSyncTime)
        {
            PublishNetworkState(false);
        }
    }

    private void LateUpdate()
    {
        if (_rollingAudioSource == null)
        {
            return;
        }

        _rollingAudioSource.transform.position = transform.position;
        UpdateRollingAudioRange();
    }

    private bool HasReachedEndPoint()
    {
        if (_endPoint == null || _distanceToEndPoint < 0f)
        {
            return false;
        }

        float distanceSquared = (transform.position - _endPoint.position).sqrMagnitude;
        float resetDistanceSquared = _distanceToEndPoint * _distanceToEndPoint;
        return distanceSquared <= resetDistanceSquared;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!HasSimulationAuthority()) return;

        KillPlayer(collision.collider);

        if (IsTimerPauseCollider(collision.collider))
        {
            SetTimerPauseState(true);
        }
    }

    private void OnDisable()
    {
        StopRollingAudio();
    }

    private void UpdateRollingAudio()
    {
        if (_rollingAudioSource != null || _rollingSfx == null || _isTouchingTimerPauseCollider)
        {
            return;
        }

        _rollingAudioSource = AudioManager.Instance.PlaySFXLoop(
            _rollingSfx,
            transform,
            _rollingAudioMinDistance,
            _rollingAudioMaxDistance);

        UpdateRollingAudioRange();
    }

    private void StopRollingAudio()
    {
        if (_rollingAudioSource == null)
        {
            return;
        }

        AudioManager.Instance.StopSFX(_rollingAudioSource);
        _rollingAudioSource = null;
    }

    private bool IsTimerPauseCollider(Collider other)
    {
        if (other == null || _timerPauseColliders == null)
        {
            return false;
        }

        foreach (Collider timerPauseCollider in _timerPauseColliders)
        {
            if (other == timerPauseCollider)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsRockTouchingTimerPauseCollider()
    {
        if (_rockCollider == null || !_rockCollider.enabled || _timerPauseColliders == null)
        {
            return false;
        }

        Vector3 sphereCenter = _rockCollider.transform.TransformPoint(_rockCollider.center);
        Vector3 lossyScale = _rockCollider.transform.lossyScale;
        float largestScale = Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z));
        float sphereRadius = (_rockCollider.radius * largestScale) + TimerPauseContactTolerance;
        float sphereRadiusSquared = sphereRadius * sphereRadius;

        foreach (Collider timerPauseCollider in _timerPauseColliders)
        {
            if (timerPauseCollider == null || !timerPauseCollider.enabled || !timerPauseCollider.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 closestPoint = timerPauseCollider.ClosestPoint(sphereCenter);
            if ((closestPoint - sphereCenter).sqrMagnitude <= sphereRadiusSquared)
            {
                return true;
            }
        }

        return false;
    }

    private void SetTimerPauseState(bool isTouchingDoor)
    {
        if (_isTouchingTimerPauseCollider == isTouchingDoor)
        {
            return;
        }

        _isTouchingTimerPauseCollider = isTouchingDoor;
        if (isTouchingDoor)
        {
            StopRollingAudio();
            return;
        }

        UpdateRollingAudio();
    }

    private void UpdateRollingAudioRange()
    {
        if (_rollingAudioSource == null)
        {
            return;
        }

        if (_audioListener != null && !_audioListener.isActiveAndEnabled)
        {
            _audioListener = null;
        }

        if (_audioListener == null && Time.unscaledTime >= _nextAudioListenerLookupTime)
        {
            _audioListener = FindFirstObjectByType<AudioListener>();
            _nextAudioListenerLookupTime = Time.unscaledTime + AudioListenerLookupInterval;
        }

        if (_audioListener == null)
        {
            return;
        }

        float maxDistance = Mathf.Max(_rollingAudioMinDistance, _rollingAudioMaxDistance);
        float maxDistanceSquared = maxDistance * maxDistance;
        float listenerDistanceSquared = (_audioListener.transform.position - transform.position).sqrMagnitude;
        _rollingAudioSource.mute = listenerDistanceSquared > maxDistanceSquared;
    }

    private void KillPlayer(Collider other)
    {
        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.InstantKill();
        }
    }

    private void ResetToStartPosition()
    {
        if (_positionClone == null)
        {
            return;
        }

        _elapsedTime = 0f;
        _isTouchingTimerPauseCollider = false;

        if (_rigidbody != null)
        {
            _rigidbody.position = _positionClone.position;
            _rigidbody.rotation = _startRotation;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.WakeUp();
            PublishNetworkState(true);
            return;
        }

        transform.SetPositionAndRotation(_positionClone.position, _startRotation);
        PublishNetworkState(true);
    }

    private bool HasSimulationAuthority()
    {
        return !IsNetworkSessionActive() || (IsSpawned && IsServer);
    }

    private static bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private void ConfigurePhysicsForAuthority(bool hasAuthority)
    {
        if (_rigidbody == null) return;

        if (!_rigidbody.isKinematic)
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        _rigidbody.isKinematic = !hasAuthority;
        _rigidbody.useGravity = hasAuthority;
        _rigidbody.detectCollisions = hasAuthority;
    }

    private void PublishNetworkState(bool force)
    {
        if (!IsSpawned || !IsServer) return;
        if (!force && Time.unscaledTime < _nextNetworkSyncTime) return;

        Vector3 position = _rigidbody != null ? _rigidbody.position : transform.position;
        Quaternion rotation = _rigidbody != null ? _rigidbody.rotation : transform.rotation;
        _networkPosition.Value = position;
        _networkRotation.Value = rotation;
        _hasNetworkState.Value = true;
        _nextNetworkSyncTime = Time.unscaledTime + NetworkSyncInterval;
    }

    private void ApplyNetworkState(bool snapImmediately)
    {
        if (!_hasNetworkState.Value) return;

        Vector3 targetPosition = _networkPosition.Value;
        Quaternion targetRotation = _networkRotation.Value;
        float snapDistanceSquared = ClientSnapDistance * ClientSnapDistance;
        bool shouldSnap = snapImmediately ||
                          (transform.position - targetPosition).sqrMagnitude > snapDistanceSquared;

        if (shouldSnap)
        {
            transform.SetPositionAndRotation(targetPosition, targetRotation);
            return;
        }

        float interpolation = 1f - Mathf.Exp(-ClientInterpolationSpeed * Time.deltaTime);
        transform.SetPositionAndRotation(
            Vector3.Lerp(transform.position, targetPosition, interpolation),
            Quaternion.Slerp(transform.rotation, targetRotation, interpolation));
    }
}
