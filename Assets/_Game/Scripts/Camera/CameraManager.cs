using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// CameraManager - Quản lý việc chuyển đổi giữa các Preset Camera và thiết lập Target cho Cinemachine.
/// Hoạt động như một Singleton cục bộ trên mỗi Client/Host.
/// </summary>
public class CameraManager : MonoBehaviour
{
    public static CameraManager Instance { get; private set; }

    [Header("Virtual Cameras - Cinemachine 3.x")]
    [SerializeField] private CinemachineCamera _vcamThirdPerson;
    [SerializeField] private CinemachineCamera _vcamSandSlide;
    [SerializeField] private CinemachineCamera _vcamPlatformer;
    [SerializeField] private CinemachineCamera _vcamFlyDown;
    [SerializeField] private CinemachineCamera _vcamGateFocus;
    [SerializeField] private CinemachineCamera _vcamWarpAscent;
    [SerializeField] private CinemachineCamera _vcamStarfallSoft;
    [SerializeField] private CinemachineCamera _vcamTerrainRevealWide;
    [SerializeField] private CinemachineCamera _vcamTopDownController; // Mới
    [SerializeField] private CinemachineCamera _vcamTopDownObserver;   // Mới

    [Header("Configuration Assets (SO)")]
    [SerializeField] private SOCameraConfig _configThirdPerson;
    [SerializeField] private SOCameraConfig _configSandSlide;
    [SerializeField] private SOCameraConfig _configPlatformer;
    [SerializeField] private SOCameraConfig _configFlyDown;
    [SerializeField] private SOCameraConfig _configGateFocus;
    [SerializeField] private SOCameraConfig _configWarpAscent;
    [SerializeField] private SOCameraConfig _configStarfallSoft;
    [SerializeField] private SOCameraConfig _configTerrainRevealWide;
    [SerializeField] private SOCameraConfig _configCutscene;
    [SerializeField] private SOCameraConfig _configTopDown; // Mới (Dùng chung)

    private PlayerInputHandler _inputHandler;
    private CameraPreset _currentPreset = CameraPreset.ThirdPerson;
    
    private Dictionary<CameraPreset, CinemachineCamera> _vcamMap;
    private Dictionary<CameraPreset, SOCameraConfig> _configMap;
    private Dictionary<CameraPreset, Vector3> _flightCameraBaseOffsets;
    private readonly Dictionary<CinemachineInputAxisController, bool> _menuAxisStates = new();
    private bool _menuCameraLocked;

    [Header("Flight Camera Chase")]
    [SerializeField, Min(0f)] private float _flightLookSensitivity = 0.08f;
    [SerializeField] private float _flightMinimumSteeringPitch = -50f;
    [SerializeField] private float _flightMaximumSteeringPitch = 50f;
    [SerializeField, Min(0f)] private float _flightCameraPositionDamping = 0.05f;
    [SerializeField, Min(0f)] private float _flightCameraAimDamping = 0.03f;

    [Header("Level 04 Single Camera States")]
    [SerializeField] private Vector3 _gateFocusOffset = new(0f, -5f, -16f);
    [SerializeField] private Vector3 _warpAscentOffset = new(0f, -8f, -20f);
    [SerializeField] private Vector3 _starfallOffset = new(0f, 8f, -15f);
    [SerializeField] private Vector3 _terrainRevealOffset = new(0f, 10f, -18f);
    [SerializeField, Range(30f, 100f)] private float _normalFlightFov = 60f;
    [SerializeField, Range(30f, 100f)] private float _gateFocusFov = 65f;
    [SerializeField, Range(30f, 100f)] private float _warpAscentFov = 70f;
    [SerializeField, Range(30f, 100f)] private float _starfallFov = 62f;
    [SerializeField, Range(30f, 100f)] private float _terrainRevealFov = 72f;

    private float _flightHeadingYaw;
    private float _flightOrbitYaw;
    private float _flightOrbitPitch;
    private Camera _renderCamera;

    private const int PRIORITY_ACTIVE = 20;
    private const int PRIORITY_INACTIVE = 0;

    public CameraPreset CurrentPreset => _currentPreset;

    public CinemachineCamera VcamThirdPerson => _vcamThirdPerson;

    public Vector3 FlightSteeringDirection
    {
        get
        {
            if (_renderCamera == null || !_renderCamera.isActiveAndEnabled)
            {
                _renderCamera = Camera.main;
            }

            if (_renderCamera != null)
            {
                return _renderCamera.ViewportPointToRay(
                    new Vector3(0.5f, 0.5f, 0f)).direction.normalized;
            }

            float yaw = _flightHeadingYaw + _flightOrbitYaw;
            return Quaternion.Euler(_flightOrbitPitch, yaw, 0f) * Vector3.forward;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        PersistentSceneRoot.MarkDontDestroyOnLoad(transform);
        _renderCamera = Camera.main;

        // Auto-add để tránh trường hợp quên gắn service trong scene.
        if (GetComponent<CameraSettingsService>() == null)
        {
            gameObject.AddComponent<CameraSettingsService>();
        }

        InitializeMaps();
        CacheFlightCameraOffsets();
        SetAllPriorities(PRIORITY_INACTIVE);

        if (_vcamThirdPerson != null)
        {
            _vcamThirdPerson.Priority.Value = PRIORITY_ACTIVE;
        }
    }

    private void OnEnable()
    {
        EventBus.OnGamePaused += HandleGamePaused;
        EventBus.OnGameResumed += HandleGameResumed;
        EventBus.OnCutSceneStarted += HandleCutSceneStarted;
        EventBus.OnCutSceneEnded += HandleCutSceneEnded;
        EventBus.OnPlayerRespawned += HandlePlayerRespawned;
    }

    private void OnDisable()
    {
        EventBus.OnGamePaused -= HandleGamePaused;
        EventBus.OnGameResumed -= HandleGameResumed;
        EventBus.OnCutSceneStarted -= HandleCutSceneStarted;
        EventBus.OnCutSceneEnded -= HandleCutSceneEnded;
        EventBus.OnPlayerRespawned -= HandlePlayerRespawned;
    }

    private void InitializeMaps()
    {
        _vcamMap = new Dictionary<CameraPreset, CinemachineCamera>
        {
            { CameraPreset.ThirdPerson, _vcamThirdPerson },
            { CameraPreset.SandSlide, _vcamSandSlide },
            { CameraPreset.Platformer, _vcamPlatformer },
            { CameraPreset.FlyDown, _vcamFlyDown },
            { CameraPreset.GateFocus, _vcamFlyDown },
            { CameraPreset.WarpAscent, _vcamFlyDown },
            { CameraPreset.StarfallSoft, _vcamFlyDown },
            { CameraPreset.TerrainRevealWide, _vcamFlyDown },
            { CameraPreset.TopDownController, _vcamTopDownController },
            { CameraPreset.TopDownObserver, _vcamTopDownObserver }
        };

        _configMap = new Dictionary<CameraPreset, SOCameraConfig>
        {
            { CameraPreset.ThirdPerson, _configThirdPerson },
            { CameraPreset.SandSlide, _configSandSlide },
            { CameraPreset.Platformer, _configPlatformer },
            { CameraPreset.FlyDown, _configFlyDown },
            { CameraPreset.GateFocus, _configGateFocus },
            { CameraPreset.WarpAscent, _configWarpAscent },
            { CameraPreset.StarfallSoft, _configStarfallSoft },
            { CameraPreset.TerrainRevealWide, _configTerrainRevealWide },
            { CameraPreset.Cutscene, _configCutscene },
            { CameraPreset.TopDownController, _configTopDown },
            { CameraPreset.TopDownObserver, _configTopDown }
        };
    }

    private void CacheFlightCameraOffsets()
    {
        _flightCameraBaseOffsets = new Dictionary<CameraPreset, Vector3>();
        if (_vcamFlyDown == null) return;

        CinemachineFollow follow = _vcamFlyDown.GetComponent<CinemachineFollow>();
        if (follow == null) return;

        _flightCameraBaseOffsets[CameraPreset.FlyDown] = follow.FollowOffset;
        _flightCameraBaseOffsets[CameraPreset.GateFocus] = _gateFocusOffset;
        _flightCameraBaseOffsets[CameraPreset.WarpAscent] = _warpAscentOffset;
        _flightCameraBaseOffsets[CameraPreset.StarfallSoft] = _starfallOffset;
        _flightCameraBaseOffsets[CameraPreset.TerrainRevealWide] = _terrainRevealOffset;

        TrackerSettings settings = follow.TrackerSettings;
        settings.BindingMode = BindingMode.WorldSpace;
        settings.PositionDamping = Vector3.one * _flightCameraPositionDamping;
        follow.TrackerSettings = settings;

        CinemachineRotationComposer composer =
            _vcamFlyDown.GetComponent<CinemachineRotationComposer>();
        if (composer != null)
        {
            composer.Damping = Vector2.one * _flightCameraAimDamping;
        }

        DisableLegacyLevel04Cameras();
    }

    private void Update()
    {
        if (!IsFlightPreset(_currentPreset)) return;

        ResolvePlayerInputIfNeeded();
        if (_inputHandler == null || !_inputHandler.CameraLookEnabled) return;

        Vector2 lookDelta = _inputHandler.CameraLookDelta;
        if (lookDelta.sqrMagnitude > 0f)
        {
            _flightOrbitYaw += lookDelta.x * _flightLookSensitivity;
            _flightOrbitPitch = Mathf.Clamp(
                _flightOrbitPitch - lookDelta.y * _flightLookSensitivity,
                -_flightMaximumSteeringPitch,
                -_flightMinimumSteeringPitch);
        }

        ApplyFlightCameraOrbit();
    }

    private void ApplyFlightCameraOrbit()
    {
        if (!_vcamMap.TryGetValue(_currentPreset, out CinemachineCamera camera)
            || camera == null
            || !_flightCameraBaseOffsets.TryGetValue(_currentPreset, out Vector3 baseOffset))
        {
            return;
        }

        CinemachineFollow follow = camera.GetComponent<CinemachineFollow>();
        if (follow == null) return;

        float yaw = _flightHeadingYaw + _flightOrbitYaw;

        // Rotate the original camera rig around the player. The resulting view
        // direction is also exposed to flight movement, so the player gradually
        // follows the direction selected by the camera.
        follow.FollowOffset =
            Quaternion.Euler(_flightOrbitPitch, yaw, 0f) * baseOffset;
    }

    public void SetTarget(Transform target)
    {
        SetPlayerTarget(target, target);
    }

    public void SetPlayerTarget(Transform followTarget, Transform lookAtTarget)
    {
        if (followTarget == null)
        {
            Debug.LogWarning("[CameraManager] SetPlayerTarget được gọi với followTarget null!");
            return;
        }

        foreach (var kvp in _vcamMap)
        {
            var vcam = kvp.Value;
            if (vcam == null) continue;
            
            vcam.Target.TrackingTarget = followTarget;
            
            if (lookAtTarget != null)
            {
                vcam.Target.LookAtTarget = lookAtTarget;
            }
            
            Debug.Log($"[CameraManager] Đã gán Target cho {kvp.Key}: Follow={followTarget.name}");
        }
    }

    public void SwitchCamera(CameraPreset preset)
    {
        if (preset == _currentPreset) return;
        
        // Cho phép Cutscene ngay cả khi không có trong Map (để khóa Input)
        if (!_vcamMap.ContainsKey(preset) && preset != CameraPreset.Cutscene) return;

        bool enteringFlight = IsFlightPreset(preset) && !IsFlightPreset(_currentPreset);
        _currentPreset = preset;
        if (enteringFlight)
        {
            _vcamMap.TryGetValue(preset, out CinemachineCamera flightCamera);
            _flightHeadingYaw = ResolveFlightHeading(flightCamera);
            _flightOrbitYaw = 0f;
            _flightOrbitPitch = 0f;
        }
        SetAllPriorities(PRIORITY_INACTIVE);

        if (_vcamMap.TryGetValue(preset, out CinemachineCamera target) && target != null)
        {
            target.Priority.Value = PRIORITY_ACTIVE;
            if (IsFlightPreset(preset))
            {
                ApplyFlightCameraState(preset);
                ApplyFlightCameraOrbit();
            }

            // ÉP THÔNG SỐ TỰ ĐỘNG (Sửa lỗi cái to cái nhỏ)
            if (preset == CameraPreset.TopDownController || preset == CameraPreset.TopDownObserver)
            {
                // 1. Ép FOV giống hệt nhau
                var lens = target.Lens;
                lens.FieldOfView = 60f; 
                target.Lens = lens;

                // 2. Ép góc nhìn thẳng xuống 90 độ
                target.transform.rotation = Quaternion.Euler(90f, target.transform.eulerAngles.y, 0f);

                // 3. Chỉnh độ cao (Distance) tự động
                // Tìm component Follow của Cinemachine để chỉnh khoảng cách
                var follow = target.GetComponent<CinemachineFollow>();
                if (follow != null)
                {
                    float distance = (preset == CameraPreset.TopDownController) ? 12f : 22f;
                    follow.FollowOffset = new Vector3(0, distance, 0);
                }
            }
        }

        ApplyBlendConfig(preset);
        UpdateInputState(preset);
        UpdateCursorState(preset);

        EventBus.RaiseCameraPresetChanged(preset);
    }

    private void SetAllPriorities(int priority)
    {
        foreach (var vcam in _vcamMap.Values)
        {
            if (vcam != null) vcam.Priority.Value = priority;
        }
    }

    private void ApplyBlendConfig(CameraPreset preset)
    {
        if (!_configMap.TryGetValue(preset, out SOCameraConfig config) || config == null) return;

        CinemachineBrain brain = CinemachineBrain.GetActiveBrain(0);
        if (brain != null)
        {
            brain.DefaultBlend = new CinemachineBlendDefinition(config.BlendStyle, config.BlendTime);
        }
    }

    private void UpdateInputState(CameraPreset preset)
    {
        bool lockMouse = ShouldLockCameraLook(preset);
        
        ResolvePlayerInputIfNeeded();

        if (_inputHandler != null)
        {
            if (lockMouse || _menuCameraLocked) _inputHandler.DisableCameraLook();
            else _inputHandler.EnableCameraLook();
        }
    }

    private void UpdateCursorState(CameraPreset preset)
    {
        bool showCursor = preset == CameraPreset.Cutscene || preset == CameraPreset.TopDownController || preset == CameraPreset.TopDownObserver;
        Cursor.lockState = showCursor ? CursorLockMode.Confined : CursorLockMode.Locked;
        Cursor.visible = showCursor;
    }

    private void HandleGamePaused()
    {
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;
        SetGameplayCameraLocked(true);
    }

    private void HandleGameResumed()
    {
        SetGameplayCameraLocked(false);
        if (ShouldLockCameraLook(_currentPreset)) return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        _inputHandler?.EnableCameraLook();
    }

    /// <summary>
    /// Blocks Cinemachine axis input while a gameplay menu is open. PlayerInputHandler
    /// alone cannot stop CinemachineInputAxisController from consuming device input.
    /// </summary>
    public void SetGameplayCameraLocked(bool locked)
    {
        ResolvePlayerInputIfNeeded();
        _menuCameraLocked = locked;

        if (locked)
        {
            _inputHandler?.DisableCameraLook();
            _menuAxisStates.Clear();
            foreach (CinemachineInputAxisController controller in
                     FindObjectsByType<CinemachineInputAxisController>(FindObjectsSortMode.None))
            {
                if (controller == null) continue;
                _menuAxisStates[controller] = controller.enabled;
                controller.enabled = false;
            }
            return;
        }

        foreach (var pair in _menuAxisStates)
        {
            if (pair.Key != null)
                pair.Key.enabled = pair.Value;
        }
        _menuAxisStates.Clear();
        UpdateInputState(_currentPreset);
    }

    private void HandleCutSceneStarted() => SwitchCamera(CameraPreset.Cutscene);
    private void HandleCutSceneEnded() => SwitchCamera(CameraPreset.ThirdPerson);

    private void HandlePlayerRespawned(ulong clientId, Vector3 spawnPosition)
    {
        // Khi respawn có thể cần cập nhật lại input handler nếu nó bị thay đổi
        ResolvePlayerInputIfNeeded();
    }

    private void ResolvePlayerInputIfNeeded()
    {
        if (_inputHandler != null)
        {
            NetworkObject netObj = _inputHandler.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsOwner) return;
        }

        foreach (var handler in FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None))
        {
            var netObj = handler.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                _inputHandler = handler;
                return;
            }
        }
    }

    private static bool IsFlightPreset(CameraPreset preset)
    {
        return preset is CameraPreset.FlyDown
            or CameraPreset.GateFocus
            or CameraPreset.WarpAscent
            or CameraPreset.StarfallSoft
            or CameraPreset.TerrainRevealWide;
    }

    private void ApplyFlightCameraState(CameraPreset preset)
    {
        if (_vcamFlyDown == null) return;

        float fieldOfView = preset switch
        {
            CameraPreset.GateFocus => _gateFocusFov,
            CameraPreset.WarpAscent => _warpAscentFov,
            CameraPreset.StarfallSoft => _starfallFov,
            CameraPreset.TerrainRevealWide => _terrainRevealFov,
            _ => _normalFlightFov
        };

        LensSettings lens = _vcamFlyDown.Lens;
        lens.FieldOfView = fieldOfView;
        _vcamFlyDown.Lens = lens;
    }

    private void DisableLegacyLevel04Cameras()
    {
        CinemachineCamera[] legacyCameras =
        {
            _vcamGateFocus,
            _vcamWarpAscent,
            _vcamStarfallSoft,
            _vcamTerrainRevealWide
        };

        foreach (CinemachineCamera legacyCamera in legacyCameras)
        {
            if (legacyCamera != null && legacyCamera != _vcamFlyDown)
            {
                legacyCamera.Priority.Value = PRIORITY_INACTIVE;
                legacyCamera.enabled = false;
            }
        }
    }

    private static bool ShouldLockCameraLook(CameraPreset preset)
    {
        return preset is CameraPreset.SandSlide
            or CameraPreset.Platformer
            or CameraPreset.Cutscene
            or CameraPreset.TopDownController
            or CameraPreset.TopDownObserver;
    }

    private static float ResolveFlightHeading(CinemachineCamera camera)
    {
        Transform target = camera != null ? camera.Target.TrackingTarget : null;
        if (target == null) return 0f;

        Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up);
        return forward.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(forward.normalized, Vector3.up).eulerAngles.y
            : target.eulerAngles.y;
    }

#if UNITY_EDITOR
    public void SetDebugFlightView(float yawOffset, float pitch)
    {
        _flightOrbitYaw = yawOffset;
        _flightOrbitPitch = Mathf.Clamp(
            pitch,
            -_flightMaximumSteeringPitch,
            -_flightMinimumSteeringPitch);
        ApplyFlightCameraOrbit();
    }
#endif
}
