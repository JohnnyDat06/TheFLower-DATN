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
    [SerializeField] private SandBoatHorizontalOffset _horizontalOffset;
    [SerializeField] private PlayerInputHandler _steeringPlayer;
    [SerializeField, Min(0.01f)] private float _steeringSpeed = 12f;
    [SerializeField, Min(0.01f)] private float _steeringAcceleration = 32f;
    [SerializeField, Min(0.01f)] private float _steeringSmoothing = 0.08f;
    [SerializeField, Min(0f)] private float _maxHorizontalOffset = 12f;

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
