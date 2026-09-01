using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Temporarily retargets the existing third-person camera to the Sand Boat.
/// The project camera keeps its familiar orbit controls; this component only
/// widens the field of view for the chase.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandBoatChaseCamera : MonoBehaviour
{
    [Header("Sand Boat Chase Framing (live in Play Mode)")]
    [SerializeField, Tooltip("Boarding cung cấp trạng thái chase để camera chuyển target sang thuyền.")]
    private SandBoatBoarding _boarding;
    [SerializeField, Tooltip("Speed Controller cung cấp tốc độ tối thiểu/tối đa cho hiệu ứng High Speed FOV.")]
    private SandBoatSpeedController _speedController;
    [SerializeField, Tooltip("Storm Logic cung cấp trạng thái Critical cho rung camera liên tục nhẹ.")]
    private SandstormChaseController _stormLogic;
    [SerializeField, Tooltip("State mạng phát sự kiện collision đúng một lần để camera rung va chạm.")]
    private SandBoatNetworkSynchronizer _networkSynchronizer;
    [SerializeField, Tooltip("Screen Shake dùng chung của project, điều khiển Cinemachine Perlin và accessibility setting.")]
    private ScreenShakeController _screenShakeController;
    [SerializeField, Tooltip("Cấu hình rung ngắn khi Sand Boat va vào đá.")]
    private SOScreenShakeConfig _collisionShakeConfig;
    [SerializeField, Tooltip("Offset realtime của target camera so với tâm thuyền.")]
    private Vector3 _cameraTargetOffset = new(0f, 1.25f, 0f);
    [SerializeField, Min(0.1f), Tooltip("Khoảng cách realtime từ camera tới thuyền.")]
    private float _cameraDistance = 10f;
    [SerializeField, Range(30f, 100f), Tooltip("FOV bình thường trong Sand Boat Chase.")]
    private float _chaseFov = 72f;
    [SerializeField, Range(30f, 110f), Tooltip("FOV mục tiêu khi thuyền đạt tốc độ tối đa.")]
    private float _highSpeedFov = 80f;
    [SerializeField, Min(0.01f), Tooltip("Tốc độ chuyển mượt giữa FOV bình thường và High Speed FOV.")]
    private float _fovSmoothing = 6f;
    [SerializeField, Range(-10f, 25f), Tooltip("Góc nhìn xuống thấp nhất để camera không xuyên terrain.")]
    private float _minimumViewPitch = -5f;

    [Header("Critical Storm Camera Shake")]
    [SerializeField, Min(0f), Tooltip("Cường độ rung liên tục nhẹ khi bão ở trạng thái Critical.")]
    private float _criticalShakeAmplitude = 0.28f;
    [SerializeField, Min(0f), Tooltip("Tần số rung liên tục khi bão ở trạng thái Critical.")]
    private float _criticalShakeFrequency = 0.9f;

    private CinemachineCamera _thirdPersonCamera;
    private CinemachineOrbitalFollow _thirdPersonOrbit;
    private CinemachineBrain _thirdPersonBrain;
    private CinemachineBrain.UpdateMethods _previousBrainUpdateMethod;
    private Transform _previousTrackingTarget;
    private Transform _previousLookAtTarget;
    private bool _previousCustomLookAt;
    private float _previousFov;
    private Vector3 _previousTargetOffset;
    private float _previousCameraDistance;
    private Vector2 _previousVerticalPitchRange;
    private bool _isApplied;
    private bool _didOverrideBrainUpdateMethod;

    private void OnEnable()
    {
        if (_networkSynchronizer != null)
        {
            _networkSynchronizer.CollisionConfirmedLocally += PlayCollisionShake;
        }
    }

    private void LateUpdate()
    {
        bool shouldApply = Application.isPlaying && _boarding != null && _boarding.ChaseStarted;
        if (shouldApply)
        {
            ApplyThirdPersonCamera();
            ApplyLiveFraming();
        }
        else
        {
            RestoreThirdPersonCamera();
        }
    }

    private void OnDisable()
    {
        if (_networkSynchronizer != null)
        {
            _networkSynchronizer.CollisionConfirmedLocally -= PlayCollisionShake;
        }

        _screenShakeController?.SetPersistentShake(0f, 0f);
        RestoreThirdPersonCamera();
    }

    private void ApplyThirdPersonCamera()
    {
        if (_isApplied || CameraManager.Instance == null)
        {
            return;
        }

        _thirdPersonCamera = CameraManager.Instance.VcamThirdPerson;
        if (_thirdPersonCamera == null)
        {
            return;
        }

        _previousTrackingTarget = _thirdPersonCamera.Target.TrackingTarget;
        _previousLookAtTarget = _thirdPersonCamera.Target.LookAtTarget;
        _previousCustomLookAt = _thirdPersonCamera.Target.CustomLookAtTarget;
        _previousFov = _thirdPersonCamera.Lens.FieldOfView;
        _thirdPersonOrbit = _thirdPersonCamera.GetComponent<CinemachineOrbitalFollow>();
        if (_thirdPersonOrbit != null)
        {
            _previousTargetOffset = _thirdPersonOrbit.TargetOffset;
            _previousCameraDistance = _thirdPersonOrbit.Radius;
            _previousVerticalPitchRange = _thirdPersonOrbit.VerticalAxis.Range;
        }

        _thirdPersonCamera.Target.TrackingTarget = transform;
        _thirdPersonCamera.Target.LookAtTarget = transform;
        _thirdPersonCamera.Target.CustomLookAtTarget = false;
        _thirdPersonBrain = Object.FindFirstObjectByType<CinemachineBrain>(FindObjectsInactive.Include);
        if (_thirdPersonBrain != null)
        {
            _previousBrainUpdateMethod = _thirdPersonBrain.UpdateMethod;
            _thirdPersonBrain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            _didOverrideBrainUpdateMethod = true;
        }
        _screenShakeController?.SetPersistentShake(0f, 0f);
        _isApplied = true;
    }

    private void ApplyLiveFraming()
    {
        if (!_isApplied || _thirdPersonCamera == null)
        {
            return;
        }

        float speedRatio = _speedController != null
            ? Mathf.InverseLerp(
                _speedController.BaseForwardSpeed,
                _speedController.MaxForwardSpeed,
                _speedController.CurrentForwardSpeed)
            : 0f;
        float targetFov = Mathf.Lerp(_chaseFov, _highSpeedFov, speedRatio);
        float fovFactor = 1f - Mathf.Exp(-_fovSmoothing * Time.deltaTime);
        _thirdPersonCamera.Lens.FieldOfView = Mathf.Lerp(
            _thirdPersonCamera.Lens.FieldOfView,
            targetFov,
            fovFactor);
        if (_thirdPersonOrbit != null)
        {
            _thirdPersonOrbit.TargetOffset = _cameraTargetOffset;
            _thirdPersonOrbit.Radius = _cameraDistance;

            InputAxis verticalAxis = _thirdPersonOrbit.VerticalAxis;
            verticalAxis.Range.x = _minimumViewPitch;
            verticalAxis.Value = Mathf.Max(verticalAxis.Value, _minimumViewPitch);
            _thirdPersonOrbit.VerticalAxis = verticalAxis;
        }
    }

    private void RestoreThirdPersonCamera()
    {
        if (!_isApplied)
        {
            return;
        }

        if (_thirdPersonCamera != null)
        {
            Transform playerTarget = ResolvePlayerCameraTarget();
            Transform trackingTarget = IsUsablePreviousTarget(_previousTrackingTarget)
                ? _previousTrackingTarget
                : playerTarget;
            Transform lookAtTarget = IsUsablePreviousTarget(_previousLookAtTarget)
                ? _previousLookAtTarget
                : trackingTarget;
            _thirdPersonCamera.Target.TrackingTarget = trackingTarget;
            _thirdPersonCamera.Target.LookAtTarget = lookAtTarget;
            _thirdPersonCamera.Target.CustomLookAtTarget = trackingTarget != null && _previousCustomLookAt;
            _thirdPersonCamera.Lens.FieldOfView = _previousFov;
            if (_thirdPersonOrbit != null)
            {
                _thirdPersonOrbit.TargetOffset = _previousTargetOffset;
                _thirdPersonOrbit.Radius = _previousCameraDistance;
                InputAxis verticalAxis = _thirdPersonOrbit.VerticalAxis;
                verticalAxis.Range = _previousVerticalPitchRange;
                _thirdPersonOrbit.VerticalAxis = verticalAxis;
            }
        }

        if (_didOverrideBrainUpdateMethod && _thirdPersonBrain != null)
        {
            _thirdPersonBrain.UpdateMethod = _previousBrainUpdateMethod;
        }

        _thirdPersonBrain = null;
        _didOverrideBrainUpdateMethod = false;
        _isApplied = false;
    }

    private void PlayCollisionShake()
    {
        _screenShakeController?.Shake(_collisionShakeConfig);
    }

    private bool IsUsablePreviousTarget(Transform target)
    {
        return target != null && target != transform;
    }

    private static Transform ResolvePlayerCameraTarget()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
        {
            return null;
        }

        NetworkObject localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localPlayer == null)
        {
            return null;
        }

        foreach (Transform child in localPlayer.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "CameraLookTarget")
            {
                return child;
            }
        }

        return localPlayer.transform;
    }
}
