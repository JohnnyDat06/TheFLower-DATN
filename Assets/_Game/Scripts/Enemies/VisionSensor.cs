using UnityEngine;
using Unity.Behavior;
using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// Cảm biến tầm nhìn cho Enemy, hỗ trợ đa người chơi online.
/// Chỉ hoạt động trên Server để đảm bảo tính đồng bộ.
/// </summary>
[RequireComponent(typeof(BehaviorGraphAgent))]
public class VisionSensor : MonoBehaviour
{
    #region Configuration
    [Header("Vision Settings")]
    [Tooltip("Vị trí mắt của quái.")]
    public Transform[] eyes;

    [Tooltip("Góc nhìn (FOV).")]
    [Range(0, 360)] public float viewAngle = 110f;

    [Tooltip("Tầm nhìn xa.")]
    public float viewRadius = 15f;

    [Tooltip("Tầm nhìn gần (phát hiện ngay cả khi sau lưng).")]
    public float closeRange = 3.0f;

    [Tooltip("Thời gian duy trì trạng thái phát hiện sau khi mất dấu.")]
    public float detectionHoldTime = 3.0f;

    [Header("Targeting Logic")]
    [Tooltip("Khoảng cách ưu tiên để đổi mục tiêu (người mới phải gần hơn người cũ ít nhất bấy nhiêu mét mới đổi).")]
    [SerializeField] private float _targetSwitchHysteresis = 2.0f;

    [Tooltip("Thời gian chờ tối thiểu giữa 2 lần đổi mục tiêu.")]
    [SerializeField] private float _targetSwitchCooldown = 2.0f;

    [Header("Layers")]
    public LayerMask targetMask;
    public LayerMask obstacleMask;

    [Header("Blackboard Keys")]
    public string playerVariableName = "Player";
    public string detectedVariableName = "IsDetected";
    #endregion

    #region Internal State
    private BehaviorGraphAgent _behaviorAgent;
    private EnemyMovement _movement;
    
    private GameObject _currentTarget;
    private float _lastTargetSwitchTime;
    private float _memoryEndTime = -100f;
    private bool _canSeeAnyPlayer;

    private Collider[] _overlapResults = new Collider[10];
    private int _combinedMask;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        _behaviorAgent = GetComponent<BehaviorGraphAgent>();
        _movement = GetComponent<EnemyMovement>();
        _combinedMask = obstacleMask | targetMask;
    }

    private void Update()
    {
        // Chỉ xử lý logic tầm nhìn trên Server
        if (_movement != null && !_movement.IsServer) return;

        ScanForPlayers();
        UpdateBlackboard();
    }
    #endregion

    #region Core Logic
    private void ScanForPlayers()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, viewRadius, _overlapResults, targetMask);
        
        GameObject bestCandidate = null;
        float minDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider col = _overlapResults[i];
            if (col == null || col.gameObject == gameObject) continue;

            if (IsTargetVisible(col, out float distance))
            {
                if (distance < minDistance)
                {
                    minDistance = distance;
                    bestCandidate = col.gameObject;
                }
            }
        }

        // Logic đổi mục tiêu (Target Switching)
        if (bestCandidate != null)
        {
            _memoryEndTime = Time.time + detectionHoldTime;
            _canSeeAnyPlayer = true;

            if (_currentTarget == null)
            {
                SwitchTarget(bestCandidate);
            }
            else if (bestCandidate != _currentTarget)
            {
                // Chỉ đổi mục tiêu nếu người mới gần hơn đáng kể VÀ hết cooldown
                float currentTargetDist = Vector3.Distance(transform.position, _currentTarget.transform.position);
                bool isMuchCloser = minDistance < (currentTargetDist - _targetSwitchHysteresis);
                bool cooldownOver = Time.time >= _lastTargetSwitchTime + _targetSwitchCooldown;

                if (isMuchCloser && cooldownOver)
                {
                    SwitchTarget(bestCandidate);
                }
            }
        }
        else
        {
            // Nếu không thấy ai, kiểm tra bộ nhớ
            _canSeeAnyPlayer = Time.time < _memoryEndTime;
            
            if (!_canSeeAnyPlayer)
            {
                _currentTarget = null;
            }
        }
    }

    private void SwitchTarget(GameObject newTarget)
    {
        _currentTarget = newTarget;
        _lastTargetSwitchTime = Time.time;
        
        if (_behaviorAgent != null)
        {
            _behaviorAgent.SetVariableValue(playerVariableName, _currentTarget);
        }
    }

    private bool IsTargetVisible(Collider targetCollider, out float distance)
    {
        distance = Vector3.Distance(transform.position, targetCollider.transform.position);
        
        // 1. Check mắt nào nhìn thấy
        Transform startPoint = (eyes != null && eyes.Length > 0) ? eyes[0] : transform;
        foreach (var eye in eyes)
        {
            if (eye == null) continue;
            
            Vector3 dirToTarget = (targetCollider.bounds.center - eye.position).normalized;
            float angle = Vector3.Angle(eye.forward, dirToTarget);

            // Check FOV hoặc tầm gần
            if (distance <= closeRange || angle <= viewAngle * 0.5f)
            {
                // Check vật cản (Line of Sight)
                if (CheckLineOfSight(eye.position, targetCollider))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool CheckLineOfSight(Vector3 eyePos, Collider targetCollider)
    {
        Vector3 targetPos = targetCollider.bounds.center;
        Vector3 dir = targetPos - eyePos;
        float dist = dir.magnitude;

        if (Physics.Raycast(eyePos, dir.normalized, out RaycastHit hit, dist, _combinedMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == targetCollider || hit.transform.IsChildOf(targetCollider.transform))
            {
                return true;
            }
        }
        return false;
    }

    private void UpdateBlackboard()
    {
        if (_behaviorAgent != null)
        {
            _behaviorAgent.SetVariableValue(detectedVariableName, _canSeeAnyPlayer);
            // Player target đã được update trong SwitchTarget hoặc Scan
        }
    }
    #endregion

    #region Public API
    public void TriggerAlert(GameObject source, float duration = 5f)
    {
        if (_movement != null && !_movement.IsServer) return;

        _memoryEndTime = Mathf.Max(_memoryEndTime, Time.time + duration);
        _canSeeAnyPlayer = true;
        
        if (source != null && _currentTarget == null)
        {
            SwitchTarget(source);
        }
    }
    #endregion

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = _canSeeAnyPlayer ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, viewRadius);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, closeRange);
    }
}
