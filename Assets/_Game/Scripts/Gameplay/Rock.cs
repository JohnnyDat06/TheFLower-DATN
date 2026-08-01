using UnityEngine;
using UnityEngine.Serialization;

public class Rock : MonoBehaviour
{
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
    private AudioSource _rollingAudioSource;
    private Quaternion _startRotation;
    private int _timerPauseCollisionCount;
    private float _elapsedTime;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
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
        if (_timerPauseCollisionCount == 0)
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
            _timerPauseCollisionCount++;
            StopRollingAudio();
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        if (IsTimerPauseCollider(collision.collider))
        {
            _timerPauseCollisionCount = Mathf.Max(0, _timerPauseCollisionCount - 1);
            UpdateRollingAudio();
        }
    }

    private void OnDisable()
    {
        StopRollingAudio();
    }

    private void UpdateRollingAudio()
    {
        if (_rollingAudioSource != null || _rollingSfx == null || _timerPauseCollisionCount > 0)
        {
            return;
        }

        _rollingAudioSource = AudioManager.Instance.PlaySFXLoop(
            _rollingSfx,
            transform,
            _rollingAudioMinDistance,
            _rollingAudioMaxDistance);
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
        _timerPauseCollisionCount = 0;

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
