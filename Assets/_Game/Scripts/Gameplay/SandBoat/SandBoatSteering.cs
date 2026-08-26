using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Converts the P1 movement axis into a smoothed, inverted horizontal offset for the Sand Boat.
/// P1 is the host player in the current project role convention. P2 has no steering path.
/// </summary>
[DefaultExecutionOrder(-75)]
[DisallowMultipleComponent]
public sealed class SandBoatSteering : MonoBehaviour
{
    [SerializeField, Tooltip("Bộ điều khiển khoảng lệch ngang của thuyền so với tâm đường spline.")]
    private SandBoatHorizontalOffset _horizontalOffset;
    [SerializeField, Tooltip("Nguồn input của người chơi P1 dùng để điều khiển hướng trái/phải.")]
    private PlayerInputHandler _steeringPlayer;
    [SerializeField, Min(0.01f), Tooltip("Tốc độ di chuyển ngang tối đa của thuyền khi P1 giữ phím lái.")]
    private float _steeringSpeed = 12f;
    [SerializeField, Min(0.01f), Tooltip("Gia tốc đạt tới tốc độ lái ngang khi P1 đang giữ phím.")]
    private float _steeringAcceleration = 32f;
    [SerializeField, Min(0.01f), Tooltip("Thời gian làm mượt input trong lúc P1 đang giữ phím lái.")]
    private float _steeringSmoothing = 0.08f;
    [SerializeField, Min(0f), Tooltip("Khoảng lệch ngang tối đa của thuyền so với tâm đường spline.")]
    private float _maxHorizontalOffset = 12f;
    [SerializeField, Range(0f, 0.5f), Tooltip("Ngưỡng input được xem là đã thả phím; thuyền dừng dịch ngang ngay khi input nằm trong ngưỡng này.")]
    private float _steeringReleaseDeadZone = 0.05f;

    private float _targetOffset;
    private float _steeringVelocity;
    private float _smoothedInput;
    private float _inputSmoothingVelocity;

    /// <summary>Current desired offset after P1 steering and clamping.</summary>
    public float TargetOffset => _targetOffset;

    private void OnValidate()
    {
        _steeringSpeed = Mathf.Max(0.01f, _steeringSpeed);
        _steeringAcceleration = Mathf.Max(0.01f, _steeringAcceleration);
        _steeringSmoothing = Mathf.Max(0.01f, _steeringSmoothing);
        _maxHorizontalOffset = Mathf.Max(0f, _maxHorizontalOffset);
        _steeringReleaseDeadZone = Mathf.Clamp(_steeringReleaseDeadZone, 0f, 0.5f);
        _targetOffset = Mathf.Clamp(_targetOffset, -_maxHorizontalOffset, _maxHorizontalOffset);
    }

    private void Awake()
    {
        _targetOffset = _horizontalOffset != null ? _horizontalOffset.CurrentOffset : 0f;
    }

    private void Update()
    {
        if (!Application.isPlaying || _horizontalOffset == null)
        {
            return;
        }

        TryResolveP1Input();
        float rawSteeringInput = GetInvertedP1SteeringInput();
        if (Mathf.Abs(rawSteeringInput) <= _steeringReleaseDeadZone)
        {
            _targetOffset = _horizontalOffset.CurrentOffset;
            _smoothedInput = 0f;
            _steeringVelocity = 0f;
            _inputSmoothingVelocity = 0f;
            _horizontalOffset.SetTargetOffset(_targetOffset);
            return;
        }

        _smoothedInput = Mathf.SmoothDamp(
            _smoothedInput,
            rawSteeringInput,
            ref _inputSmoothingVelocity,
            _steeringSmoothing);
        _steeringVelocity = Mathf.MoveTowards(
            _steeringVelocity,
            _smoothedInput * _steeringSpeed,
            _steeringAcceleration * Time.deltaTime);
        _targetOffset = Mathf.Clamp(
            _targetOffset + _steeringVelocity * Time.deltaTime,
            -_maxHorizontalOffset,
            _maxHorizontalOffset);
        _horizontalOffset.SetTargetOffset(_targetOffset);
    }

    /// <summary>Assigns P1 explicitly; Phase 6 will call this after boarding.</summary>
    public void AssignSteeringPlayer(PlayerInputHandler steeringPlayer)
    {
        _steeringPlayer = steeringPlayer;
    }

    /// <summary>Clears P1 steering inertia before a checkpoint retry.</summary>
    public void ResetSteeringState()
    {
        _targetOffset = 0f;
        _steeringVelocity = 0f;
        _smoothedInput = 0f;
        _inputSmoothingVelocity = 0f;
        _horizontalOffset?.SetTargetOffset(0f);
    }

    private void TryResolveP1Input()
    {
        if (IsP1(_steeringPlayer))
        {
            return;
        }

        foreach (PlayerInputHandler inputHandler in FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None))
        {
            if (!IsP1(inputHandler))
            {
                continue;
            }

            _steeringPlayer = inputHandler;
            return;
        }
    }

    private float GetInvertedP1SteeringInput()
    {
        if (!IsP1(_steeringPlayer) || !_steeringPlayer.IsOwner)
        {
            return 0f;
        }

        // PlayerInputHandler reports A as -X and D as +X; invert to enforce A=right, D=left.
        return -Mathf.Clamp(_steeringPlayer.MoveInput.x, -1f, 1f);
    }

    private static bool IsP1(PlayerInputHandler inputHandler)
    {
        if (inputHandler == null || !inputHandler.IsSpawned)
        {
            return false;
        }

        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && inputHandler.OwnerClientId == NetworkManager.ServerClientId;
    }
}
