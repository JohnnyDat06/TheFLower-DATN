using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Trigger zone doi camera theo preset. Chi player local owner moi duoc trigger.
/// </summary>
public class CameraZoneTrigger : MonoBehaviour
{
    [Tooltip("Che do camera khi player vao zone")]
    [SerializeField] private CameraPreset _cameraPreset = CameraPreset.ThirdPerson;

    [SerializeField] private CameraManager _cameraManager;

    [Tooltip("Bat WallClimb khi vao zone nay (man Platformer)")]
    [SerializeField] private bool _enableWallClimb;

    [Tooltip("Đổi về ThirdPerson khi rời trigger. Tắt cho chuỗi camera nối tiếp như Level 04.")]
    [SerializeField] private bool _restoreThirdPersonOnExit = true;

    private void Awake()
    {
        if (_cameraManager == null)
        {
            _cameraManager = FindFirstObjectByType<CameraManager>();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(Constants.Tags.PLAYER))
        {
            return;
        }

        if (!other.TryGetComponent(out NetworkObject networkObject) || !networkObject.IsOwner)
        {
            return;
        }

        if (_cameraManager != null)
        {
            _cameraManager.SwitchCamera(_cameraPreset);
        }

        if (_enableWallClimb && other.TryGetComponent(out PlayerStateMachine fsm))
        {
            fsm.WallClimbEnabled = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(Constants.Tags.PLAYER))
        {
            return;
        }

        if (!other.TryGetComponent(out NetworkObject networkObject) || !networkObject.IsOwner)
        {
            return;
        }

        if (_restoreThirdPersonOnExit && _cameraManager != null)
        {
            _cameraManager.SwitchCamera(CameraPreset.ThirdPerson);
        }

        if (_enableWallClimb && other.TryGetComponent(out PlayerStateMachine fsm))
        {
            fsm.WallClimbEnabled = false;
        }
    }
}
