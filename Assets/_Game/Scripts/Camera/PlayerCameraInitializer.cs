using Unity.Netcode;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

/// <summary>
/// Gắn trên Player prefab. Thiết lập camera cho local player khi spawn.
/// Đảm bảo mỗi máy khách chỉ điều khiển camera bám theo player của chính nó.
/// </summary>
public class PlayerCameraInitializer : NetworkBehaviour
{
    [Header("Look At Target")]
    [SerializeField] private Transform _cameraLookTarget;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Chỉ xử lý nếu đây là local player (máy khách sở hữu player này)
        if (IsOwner)
        {
            StartCoroutine(InitializeCameraRoutine());
        }
    }

    private IEnumerator InitializeCameraRoutine()
    {
        // Chờ tối đa 5 giây để CameraManager xuất hiện (tăng từ 2s)
        while (CameraManager.Instance == null && IsLobbyScene())
        {
            yield return new WaitForSeconds(0.1f);
        }

        float timeout = 5.0f;
        while (CameraManager.Instance == null && timeout > 0)
        {
            yield return new WaitForSeconds(0.1f);
            timeout -= 0.1f;
        }

        if (CameraManager.Instance == null)
        {
            // Kiểm tra xem có đối tượng nào trong scene có script này không nhưng chưa set Instance
            var foundManager = Object.FindFirstObjectByType<CameraManager>();
            Debug.LogError($"[PlayerCameraInitializer] Không tìm thấy CameraManager Instance! Found in scene: {foundManager != null}");
            yield break;
        }

        // Tự động tìm LookTarget nếu chưa được gán
        if (_cameraLookTarget == null)
        {
            _cameraLookTarget = FindLookTargetRecursive(transform);
        }

        // Gán target cho CameraManager (Ưu tiên dùng _cameraLookTarget cho cả Follow và LookAt nếu có)
        Transform targetToUse = _cameraLookTarget != null ? _cameraLookTarget : transform;
        CameraManager.Instance.SetPlayerTarget(targetToUse, targetToUse);

        // Setup cursor mặc định
        if (!UICursorLockService.IsCursorReleased)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Debug.Log($"[PlayerCameraInitializer] Camera targets initialized for {(IsHost ? "Host" : "Client")} player.");
    }

    private bool IsLobbyScene()
    {
        return SceneManager.GetActiveScene().name == Constants.Scenes.LOBBY;
    }

    private void InitializeCamera() { }

    private Transform FindLookTargetRecursive(Transform parent)
    {
        if (parent.name == "CameraLookTarget") return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform result = FindLookTargetRecursive(parent.GetChild(i));
            if (result != null) return result;
        }

        return parent; // Trả về chính nó nếu không tìm thấy con nào tên CameraLookTarget
    }
}
