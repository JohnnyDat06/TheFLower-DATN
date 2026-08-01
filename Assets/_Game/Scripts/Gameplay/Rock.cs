using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public class Rock : MonoBehaviour
{
    private const float TimerPauseContactTolerance = 0.05f;

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

    private Rigidbody _rigidbody;
    private SphereCollider _rockCollider;
    private AudioSource _rollingAudioSource;
    private Quaternion _startRotation;
    private bool _isTouchingTimerPauseCollider;
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

        ResetToStartPosition();
        UpdateRollingAudio();
    }

    private void Update()
    {
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
            _rollingAudioMaxDistance,
            AudioRolloffMode.Linear);
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
            return;
        }

        transform.SetPositionAndRotation(_positionClone.position, _startRotation);
    }
}
