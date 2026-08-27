using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Phase 14 deterministic storm-rock attack. A red telegraph is shown at a
/// route point ahead of the boat, then a rock flies to that point and becomes a
/// regular SandBoatObstacle only after landing.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class SandstormRockAttackController : NetworkBehaviour
{
    private const float SafeLaneRatio = 0.65f;

    [Header("Sand Boat References")]
    [SerializeField, Tooltip("Nguồn chuyển động Route dùng để chọn deterministic điểm rơi phía trước thuyền.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Nguồn trạng thái bão logic; đợt đá chỉ bắt đầu ở trạng thái kích hoạt đã cấu hình.")]
    private SandstormChaseController _stormLogic;
    [SerializeField, Tooltip("Terrain dùng để đặt vòng cảnh báo và đá rơi trên đúng mặt đất đã thiết kế.")]
    private Terrain _terrain;
    [SerializeField, Tooltip("Prefab đá sa mạc có sẵn dùng cho mọi đá bão. Prefab không được chứa logic gameplay authoritative.")]
    private GameObject _rockPrefab;
    [SerializeField, Tooltip("Transform nằm bên trong hiệu ứng bão, dùng làm vị trí xuất phát thật của đá.")]
    private Transform _stormLaunchSource;

    [Header("Storm Rock Timing")]
    [SerializeField, Min(0.1f), Tooltip("Số giây giữa các đợt đá bão deterministic khi bão ở Critical.")]
    private float _rockSpawnInterval = 3.5f;
    [SerializeField, Min(0.1f), Tooltip("Số giây vòng cảnh báo đỏ hiển thị trước khi đá được phóng.")]
    private float _telegraphLeadTime = 1.5f;
    [SerializeField, Min(0.05f), Tooltip("Số giây đá bay từ phía sau thuyền đến điểm rơi đã cảnh báo.")]
    private float _rockFlightDuration = 1.25f;
    [SerializeField, Min(0.1f), Tooltip("Khoảng cách Route tối thiểu phía trước thuyền mà đá bão có thể rơi.")]
    private float _minLandingDistanceAhead = 18f;
    [SerializeField, Min(0.1f), Tooltip("Khoảng cách Route tối đa phía trước thuyền mà đá bão có thể rơi.")]
    private float _maxLandingDistanceAhead = 34f;
    [SerializeField, Min(1), Tooltip("Số đợt đá bão đang cảnh báo hoặc đang bay tối đa cùng lúc; đá đã rơi không tính vào giới hạn này.")]
    private int _maxActiveStormRocks = 3;
    [SerializeField, Min(0.1f), Tooltip("Số giây đá bão được giữ lại trên mặt đất sau khi rơi trước khi tự biến mất.")]
    private float _landedRockLifetime = 3f;
    [SerializeField, Tooltip("Trạng thái bão cho phép bắt đầu các đợt đá deterministic.")]
    private SandstormChaseState _stormRockTriggerState = SandstormChaseState.Critical;

    [Header("Landing Visuals")]
    [SerializeField, Min(0.1f), Tooltip("Bán kính vòng cảnh báo điểm rơi màu đỏ, tính theo đơn vị world.")]
    private float _telegraphRadius = 2.2f;
    [SerializeField, Min(0.005f), Tooltip("Độ dày vòng cảnh báo điểm rơi màu đỏ, tính theo đơn vị world.")]
    private float _telegraphLineWidth = 0.12f;
    [SerializeField, Min(0f), Tooltip("Độ cao tối đa cộng thêm cho đường bay vòng cung của đá.")]
    private float _rockFlightHeight = 14f;
    [SerializeField, Min(0f), Tooltip("Độ lệch chiều cao của vị trí xuất phát bên trong bão.")]
    private float _stormLaunchHeightOffset = 2f;
    [FormerlySerializedAs("_rockLaunchTravelRatio")]
    [SerializeField, Range(0.1f, 0.9f), Tooltip("Vị trí đỉnh đường vòng cung theo tiến độ bay; 0.5 là chính giữa, giá trị lớn hơn làm đá bắt đầu rơi muộn hơn.")]
    private float _rockArcPeakProgress = 0.65f;
    [SerializeField, Min(0.1f), Tooltip("Scale đồng nhất áp dụng cho prefab đá bão được tạo runtime.")]
    private float _rockScale = 1f;
    [SerializeField, Min(0f), Tooltip("Độ lệch chiều cao áp dụng cho vị trí cuối của đá sau khi lấy độ cao terrain.")]
    private float _landingGroundOffset = 0.05f;

    private readonly List<StormRockSequence> _sequences = new();
    private double _nextSpawnTime;
    private int _spawnSequence;
    private bool _loggedMissingPrefab;

    private sealed class StormRockSequence
    {
        public SandstormRockTelegraph Telegraph;
        public GameObject Rock;
        public SandBoatObstacle Obstacle;
        public Vector3 LandingPosition;
        public Vector3 FlightStartPosition;
        public Quaternion FlightRotation;
        public double TelegraphCreatedTime;
        public double LaunchTime;
        public double LandedTime;
        public bool IsFlying;
    }

    private void OnEnable()
    {
        if (_stormLogic != null)
        {
            _stormLogic.StateChanged += OnStormStateChanged;
        }

        ResetAttackState();
    }

    private void OnDisable()
    {
        if (_stormLogic != null)
        {
            _stormLogic.StateChanged -= OnStormStateChanged;
        }

        ClearSequences();
    }

    private void OnValidate()
    {
        _rockSpawnInterval = Mathf.Max(0.1f, _rockSpawnInterval);
        _telegraphLeadTime = Mathf.Max(0.1f, _telegraphLeadTime);
        _rockFlightDuration = Mathf.Max(0.05f, _rockFlightDuration);
        _minLandingDistanceAhead = Mathf.Max(0.1f, _minLandingDistanceAhead);
        _maxLandingDistanceAhead = Mathf.Max(_minLandingDistanceAhead, _maxLandingDistanceAhead);
        _maxActiveStormRocks = Mathf.Max(1, _maxActiveStormRocks);
        _landedRockLifetime = Mathf.Max(0.1f, _landedRockLifetime);
        _telegraphRadius = Mathf.Max(0.1f, _telegraphRadius);
        _telegraphLineWidth = Mathf.Max(0.005f, _telegraphLineWidth);
        _rockFlightHeight = Mathf.Max(0f, _rockFlightHeight);
        _stormLaunchHeightOffset = Mathf.Max(0f, _stormLaunchHeightOffset);
        _rockArcPeakProgress = Mathf.Clamp(_rockArcPeakProgress, 0.1f, 0.9f);
        _rockScale = Mathf.Max(0.1f, _rockScale);
        _landingGroundOffset = Mathf.Max(0f, _landingGroundOffset);
    }

    private void Update()
    {
        if (!Application.isPlaying || _movement == null || _stormLogic == null)
        {
            return;
        }

        UpdateFlyingRocks();
        RemoveExpiredLandedRocks();

        if (!_movement.IsRouteMovementEnabled
            || _stormLogic.State != _stormRockTriggerState
            || (IsSpawned && !IsServer))
        {
            return;
        }

        double synchronizedTime = GetSynchronizedTime();
        if (_nextSpawnTime <= 0f)
        {
            _nextSpawnTime = synchronizedTime + _rockSpawnInterval;
        }

        if (synchronizedTime < _nextSpawnTime || GetActiveSequenceCount() >= _maxActiveStormRocks)
        {
            return;
        }

        if (TryScheduleStormRock())
        {
            _nextSpawnTime = synchronizedTime + _rockSpawnInterval;
        }
        else
        {
            _nextSpawnTime = synchronizedTime + 0.5f;
        }
    }

    /// <summary>Clears all pending warnings and landed/flying rocks for a retry.</summary>
    public void ResetAttackState()
    {
        ResetAttackStateLocal();
    }

    /// <summary>Sends one server-authoritative cleanup transaction to every peer.</summary>
    public void ResetAttackStateNetworked()
    {
        if (IsSpawned)
        {
            if (IsServer)
            {
                ResetAttackStateRpc();
            }

            return;
        }

        ResetAttackStateLocal();
    }

    [Rpc(SendTo.Everyone)]
    private void ResetAttackStateRpc()
    {
        ResetAttackStateLocal();
    }

    private void ResetAttackStateLocal()
    {
        ClearSequences();
        _spawnSequence = 0;
        _nextSpawnTime = 0f;
        _loggedMissingPrefab = false;
    }

    private void OnStormStateChanged(SandstormChaseState state)
    {
        if (state == SandstormChaseState.Caught)
        {
            ClearSequences();
        }
    }

    private bool TryScheduleStormRock()
    {
        if (_rockPrefab == null)
        {
            if (!_loggedMissingPrefab)
            {
                Debug.LogWarning("[SandstormRockAttack] Assign an existing desert-rock prefab before testing Phase 14.", this);
                _loggedMissingPrefab = true;
            }

            return false;
        }

        float routeLength = _movement.RouteLength;
        if (routeLength <= 0f || _movement.Progress + _minLandingDistanceAhead / routeLength >= 0.98f)
        {
            return false;
        }

        float distancePattern = (_spawnSequence % 3) / 2f;
        float distanceAhead = Mathf.Lerp(_minLandingDistanceAhead, _maxLandingDistanceAhead, distancePattern);
        distanceAhead = Mathf.Min(distanceAhead, (0.98f - _movement.Progress) * routeLength);

        SandBoatRouteSample routeSample = _movement.EvaluateRouteAhead(distanceAhead);
        float lanePattern = (_spawnSequence % 3) switch
        {
            0 => -SafeLaneRatio,
            1 => SafeLaneRatio,
            _ => 0f
        };
        float laneOffset = lanePattern * GetMaxHorizontalOffset();
        Vector3 landingPosition = routeSample.Position + routeSample.Right * laneOffset;
        landingPosition.y = SampleGroundHeight(landingPosition) + _landingGroundOffset;

        double telegraphCreatedTime = GetSynchronizedTime();
        if (IsSpawned)
        {
            ScheduleStormRockRpc(landingPosition, routeSample.Forward, telegraphCreatedTime);
        }
        else
        {
            ScheduleStormRockLocal(landingPosition, routeSample.Forward, telegraphCreatedTime);
        }

        _spawnSequence++;
        return true;
    }

    [Rpc(SendTo.Everyone)]
    private void ScheduleStormRockRpc(
        Vector3 landingPosition,
        Vector3 routeForward,
        double telegraphCreatedTime)
    {
        ScheduleStormRockLocal(landingPosition, routeForward, telegraphCreatedTime);
    }

    private void ScheduleStormRockLocal(
        Vector3 landingPosition,
        Vector3 routeForward,
        double telegraphCreatedTime)
    {
        SandstormRockTelegraph telegraph = SandstormRockTelegraph.Create(
            landingPosition,
            _telegraphRadius,
            _telegraphLineWidth);
        Vector3 safeForward = routeForward.sqrMagnitude > 0.0001f
            ? routeForward.normalized
            : transform.forward;

        _sequences.Add(new StormRockSequence
        {
            Telegraph = telegraph,
            LandingPosition = landingPosition,
            FlightRotation = Quaternion.LookRotation(safeForward, Vector3.up),
            TelegraphCreatedTime = telegraphCreatedTime
        });
    }

    private void UpdateFlyingRocks()
    {
        double synchronizedTime = GetSynchronizedTime();
        for (int index = _sequences.Count - 1; index >= 0; index--)
        {
            StormRockSequence sequence = _sequences[index];
            if (!sequence.IsFlying)
            {
                if (sequence.Rock == null && sequence.Telegraph != null
                    && synchronizedTime >= sequence.TelegraphCreatedTime + _telegraphLeadTime)
                {
                    LaunchRock(sequence);
                }

                continue;
            }

            float flightProgress = Mathf.Clamp01((float)((synchronizedTime - sequence.LaunchTime) / _rockFlightDuration));
            float smoothedProgress = Mathf.SmoothStep(0f, 1f, flightProgress);
            sequence.Rock.transform.position = EvaluateFlightArc(sequence, smoothedProgress);
            sequence.Rock.transform.Rotate(Vector3.right, 540f * Time.deltaTime, Space.Self);

            if (flightProgress >= 1f)
            {
                LandRock(sequence);
            }
        }
    }

    private void LaunchRock(StormRockSequence sequence)
    {
        if (sequence.Telegraph != null)
        {
            // Giữ vòng cảnh báo trong lúc đá bay và dùng chính tâm vòng
            // làm tọa độ đích chung cho cả visual lẫn gameplay.
            sequence.LandingPosition = sequence.Telegraph.transform.position;
        }

        SandBoatRouteSample currentSample = _movement.EvaluateRouteAhead(0f);
        Vector3 stormSourcePosition = _stormLaunchSource != null
            ? _stormLaunchSource.position
            : transform.position - currentSample.Forward * 12f;
        sequence.FlightStartPosition = stormSourcePosition + Vector3.up * _stormLaunchHeightOffset;
        sequence.Rock = Instantiate(_rockPrefab, sequence.FlightStartPosition, sequence.FlightRotation);
        sequence.Rock.name = "StormRock_Active";
        sequence.Rock.transform.localScale *= _rockScale;
        PrepareRock(sequence);
        sequence.LaunchTime = GetSynchronizedTime();
        sequence.IsFlying = true;
    }

    private Vector3 EvaluateFlightArc(StormRockSequence sequence, float progress)
    {
        float arcHeightFactor = progress <= _rockArcPeakProgress
            ? Mathf.Sin(progress / _rockArcPeakProgress * Mathf.PI * 0.5f)
            : Mathf.Sin((1f - progress) / (1f - _rockArcPeakProgress) * Mathf.PI * 0.5f);
        Vector3 directPosition = Vector3.Lerp(
            sequence.FlightStartPosition,
            sequence.LandingPosition,
            progress);
        return directPosition + Vector3.up * (_rockFlightHeight * arcHeightFactor);
    }

    private void PrepareRock(StormRockSequence sequence)
    {
        foreach (Collider collider in sequence.Rock.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (Rigidbody rigidbody in sequence.Rock.GetComponentsInChildren<Rigidbody>(true))
        {
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
        }

        sequence.Obstacle = sequence.Rock.GetComponent<SandBoatObstacle>() ?? sequence.Rock.AddComponent<SandBoatObstacle>();
        sequence.Obstacle.MarkAsStormRock();
        if (sequence.Obstacle.ObstacleCollider != null)
        {
            sequence.Obstacle.ObstacleCollider.enabled = false;
        }
    }

    private void LandRock(StormRockSequence sequence)
    {
        if (sequence.Telegraph != null)
        {
            sequence.LandingPosition = sequence.Telegraph.transform.position;
        }

        sequence.Rock.transform.SetPositionAndRotation(sequence.LandingPosition, sequence.FlightRotation);
        if ((!IsSpawned || IsServer) && sequence.Obstacle?.ObstacleCollider != null)
        {
            sequence.Obstacle.ObstacleCollider.enabled = true;
        }

        if (sequence.Telegraph != null)
        {
            Destroy(sequence.Telegraph.gameObject);
            sequence.Telegraph = null;
        }

        sequence.LandedTime = GetSynchronizedTime();
        sequence.IsFlying = false;
    }

    private void RemoveExpiredLandedRocks()
    {
        double synchronizedTime = GetSynchronizedTime();
        for (int index = _sequences.Count - 1; index >= 0; index--)
        {
            StormRockSequence sequence = _sequences[index];
            if (sequence.IsFlying || sequence.Telegraph != null)
            {
                continue;
            }

            if (sequence.Rock == null)
            {
                _sequences.RemoveAt(index);
                continue;
            }

            if (synchronizedTime < sequence.LandedTime + _landedRockLifetime)
            {
                continue;
            }

            Destroy(sequence.Rock);
            _sequences.RemoveAt(index);
        }
    }

    private int GetActiveSequenceCount()
    {
        int activeSequenceCount = 0;

        foreach (StormRockSequence sequence in _sequences)
        {
            if (sequence.Telegraph != null || sequence.IsFlying)
            {
                activeSequenceCount++;
            }
        }

        return activeSequenceCount;
    }

    private float GetMaxHorizontalOffset()
    {
        SandBoatHorizontalOffset offset = GetComponent<SandBoatHorizontalOffset>();
        return offset != null ? offset.MaxHorizontalOffset : 0f;
    }

    private float SampleGroundHeight(Vector3 position)
    {
        return _terrain != null
            ? _terrain.SampleHeight(position) + _terrain.transform.position.y
            : position.y;
    }

    private double GetSynchronizedTime()
    {
        return IsSpawned && NetworkManager != null
            ? NetworkManager.ServerTime.Time
            : Time.timeAsDouble;
    }

    private void ClearSequences()
    {
        foreach (StormRockSequence sequence in _sequences)
        {
            if (sequence.Telegraph != null)
            {
                Destroy(sequence.Telegraph.gameObject);
            }

            if (sequence.Rock != null)
            {
                Destroy(sequence.Rock);
            }
        }

        _sequences.Clear();
    }
}
