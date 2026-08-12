using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>Uses one local top-down Cinemachine view during the boss fight and restores the owned player camera after Defeat.</summary>
public sealed class BossCameraFeedback : MonoBehaviour
{
    [Tooltip("Khoang trong them quanh toan bo FloorTile khi tinh khung hinh top-down.")]
    [SerializeField, Min(0f)] private float _arenaPadding = 4f;
    [Tooltip("Do cao toi thieu cua camera top-down so voi tam mat san.")]
    [SerializeField, Min(1f)] private float _minimumHeight = 18f;
    [Tooltip("Goc nhin doc cua camera top-down. Camera van dung render camera va mau sac cua player.")]
    [SerializeField, Range(35f, 80f)] private float _fieldOfView = 55f;
    [Tooltip("Goc nhin tu tren xuong. Giu nho hon 90 do de camera van co huong tien tren mat san cho player.")]
    [SerializeField, Range(70f, 88f)] private float _topDownPitch = 82f;
    [Tooltip("Goc xoay ngang cua khung hinh boss room khi nhin tu tren xuong.")]
    [SerializeField, Range(-180f, 180f)] private float _yaw;
    [Tooltip("Dich chuyen khung hinh sau khi tu can giua arena. X la trai/phai, Y la cao/thap, Z la tien/lui tren mat san.")]
    [SerializeField] private Vector3 _cameraPositionOffset;

    private BossDefeatController _defeatController;
    private BossEncounterManager _encounterManager;
    private FloorTileManager _floorTileManager;
    private CinemachineCamera _topDownCamera;
    private CameraManager _cameraManager;
    private Coroutine _initializeRoutine;
    private bool _restoredPlayerCamera;
    private float _appliedArenaPadding;
    private float _appliedMinimumHeight;
    private float _appliedFieldOfView;
    private float _appliedTopDownPitch;
    private float _appliedYaw;
    private Vector3 _appliedPositionOffset;

    private void Awake()
    {
        _defeatController = GetComponent<BossDefeatController>();
        ResolveEncounterManager();
        _floorTileManager = GetComponent<FloorTileManager>();
    }

    private void OnEnable()
    {
        _initializeRoutine = StartCoroutine(InitializeLocalCamera());
    }

    private void LateUpdate()
    {
        if (_cameraManager == null || _topDownCamera == null) return;

        ResolveEncounterManager();

        ApplyCameraSettingsIfChanged();

        if (_defeatController != null && _defeatController.IsDefeated)
        {
            if (_restoredPlayerCamera) return;

            _restoredPlayerCamera = true;
            _cameraManager.SwitchCamera(CameraPreset.ThirdPerson);
            return;
        }

        bool shouldUseTopDown = _encounterManager != null && _encounterManager.HasEncounterStarted;
        if (!shouldUseTopDown)
        {
            if (_cameraManager.CurrentPreset == CameraPreset.BossTopDown)
                _cameraManager.SwitchCamera(CameraPreset.ThirdPerson);
            return;
        }

        // Player spawning, intro completion and shared camera flows may request
        // ThirdPerson after the boss scene has already initialized. The boss-room
        // view owns the local camera until the synchronized Defeat state releases it.
        if (_cameraManager.CurrentPreset != CameraPreset.BossTopDown)
        {
            _cameraManager.SwitchCamera(CameraPreset.BossTopDown);
            Debug.Log("[BossCameraFeedback] Restored BossTopDown after an external camera request.", this);
        }
    }

    private void OnDisable()
    {
        if (_initializeRoutine != null) StopCoroutine(_initializeRoutine);
        _initializeRoutine = null;

        if (_cameraManager != null)
        {
            if (_cameraManager.CurrentPreset == CameraPreset.BossTopDown)
                _cameraManager.SwitchCamera(CameraPreset.ThirdPerson);
            _cameraManager.UnregisterSceneCamera(CameraPreset.BossTopDown, _topDownCamera);
        }
    }

    private IEnumerator InitializeLocalCamera()
    {
        while (CameraManager.Instance == null) yield return null;

        _cameraManager = CameraManager.Instance;
        CreateTopDownCamera();
        _cameraManager.RegisterSceneCamera(CameraPreset.BossTopDown, _topDownCamera);

        if (_defeatController != null && _defeatController.IsDefeated)
        {
            _restoredPlayerCamera = true;
            _cameraManager.SwitchCamera(CameraPreset.ThirdPerson);
        }
        else if (_encounterManager != null && _encounterManager.HasEncounterStarted)
        {
            _cameraManager.SwitchCamera(CameraPreset.BossTopDown);
        }
        else
        {
            _cameraManager.SwitchCamera(CameraPreset.ThirdPerson);
        }

        _initializeRoutine = null;
    }

    /// <summary>Finds the network encounter object, which is authored separately from BossArena_Architecture.</summary>
    private void ResolveEncounterManager()
    {
        if (_encounterManager != null) return;

        _encounterManager = BossEncounterManager.Instance;
        if (_encounterManager == null)
            _encounterManager = FindFirstObjectByType<BossEncounterManager>();
    }

    private void CreateTopDownCamera()
    {
        if (_topDownCamera != null) return;

        GameObject cameraObject = new("Cat Sphinx Boss Top Down Camera");
        cameraObject.transform.SetParent(transform, true);
        _topDownCamera = cameraObject.AddComponent<CinemachineCamera>();

        ApplyCameraSettingsIfChanged(true);
        _topDownCamera.Priority.Value = 0;
    }

    /// <summary>Updates the runtime virtual camera only when a designer changes a framing value in the Inspector.</summary>
    private void ApplyCameraSettingsIfChanged(bool force = false)
    {
        if (_topDownCamera == null) return;

        bool settingsChanged = force
            || !Mathf.Approximately(_appliedArenaPadding, _arenaPadding)
            || !Mathf.Approximately(_appliedMinimumHeight, _minimumHeight)
            || !Mathf.Approximately(_appliedFieldOfView, _fieldOfView)
            || !Mathf.Approximately(_appliedTopDownPitch, _topDownPitch)
            || !Mathf.Approximately(_appliedYaw, _yaw)
            || _appliedPositionOffset != _cameraPositionOffset;

        if (!settingsChanged) return;

        CalculateArenaFraming(out Vector3 arenaCenter, out float requiredHeight);
        _topDownCamera.transform.SetPositionAndRotation(
            arenaCenter + Vector3.up * requiredHeight + _cameraPositionOffset,
            Quaternion.Euler(_topDownPitch, _yaw, 0f));

        LensSettings lens = _topDownCamera.Lens;
        lens.FieldOfView = _fieldOfView;
        lens.NearClipPlane = 0.1f;
        lens.FarClipPlane = Mathf.Max(200f, requiredHeight + 100f);
        _topDownCamera.Lens = lens;

        _appliedArenaPadding = _arenaPadding;
        _appliedMinimumHeight = _minimumHeight;
        _appliedFieldOfView = _fieldOfView;
        _appliedTopDownPitch = _topDownPitch;
        _appliedYaw = _yaw;
        _appliedPositionOffset = _cameraPositionOffset;
    }

    private void CalculateArenaFraming(out Vector3 center, out float height)
    {
        FloorTile[] tiles = _floorTileManager != null ? _floorTileManager.Tiles : null;
        if (tiles == null || tiles.Length == 0)
        {
            center = transform.position;
            height = _minimumHeight;
            return;
        }

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        float averageY = 0f;
        int validCount = 0;
        foreach (FloorTile tile in tiles)
        {
            if (tile == null) continue;
            Vector3 tileCenter = tile.WorldCenter;
            minX = Mathf.Min(minX, tileCenter.x);
            maxX = Mathf.Max(maxX, tileCenter.x);
            minZ = Mathf.Min(minZ, tileCenter.z);
            maxZ = Mathf.Max(maxZ, tileCenter.z);
            averageY += tileCenter.y;
            validCount++;
        }

        if (validCount == 0)
        {
            center = transform.position;
            height = _minimumHeight;
            return;
        }

        center = new Vector3((minX + maxX) * 0.5f, averageY / validCount, (minZ + maxZ) * 0.5f);
        float width = maxX - minX + _arenaPadding * 2f;
        float depth = maxZ - minZ + _arenaPadding * 2f;
        float aspect = Camera.main != null ? Mathf.Max(0.1f, Camera.main.aspect) : 16f / 9f;
        float halfVerticalRadians = _fieldOfView * 0.5f * Mathf.Deg2Rad;
        float heightForDepth = depth * 0.5f / Mathf.Tan(halfVerticalRadians);
        float heightForWidth = width * 0.5f / (aspect * Mathf.Tan(halfVerticalRadians));
        height = Mathf.Max(_minimumHeight, heightForDepth, heightForWidth);
    }
}
