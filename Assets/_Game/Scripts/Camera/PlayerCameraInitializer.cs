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
        if (!IsOwner) return;

        SceneManager.sceneLoaded += HandleSceneLoaded;
        StartCameraInitialization();
    }


    public override void OnNetworkDespawn()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        StopAllCoroutines();
        base.OnNetworkDespawn();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsOwner)
        {
            StartCameraInitialization();
        }
    }

    private void StartCameraInitialization()
    {
        StopAllCoroutines();
        StartCoroutine(InitializeCameraRoutine());
    }


    private IEnumerator InitializeCameraRoutine()
    {
        // Wait until NGO has finished attaching this owned object to the new scene.
        while (!IsSpawned || !IsOwner)
        {
            yield return null;
        }

        // Let the new scene finish activating its persistent camera rig and brain.
        yield return null;
        yield return new WaitForEndOfFrame();

        float timeout = 5f;
        while (CameraManager.Instance == null && timeout > 0f)
        {
            timeout -= 0.1f;
            yield return new WaitForSeconds(0.1f);
        }

        CameraManager manager = CameraManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("[PlayerCameraInitializer] CameraManager is not available after scene load; will retry on the next load.");
            yield break;
        }

        if (_cameraLookTarget == null || !_cameraLookTarget.IsChildOf(transform))
        {
            _cameraLookTarget = FindLookTargetRecursive(transform);
        }

        Transform targetToUse = _cameraLookTarget != null ? _cameraLookTarget : transform;
        manager.SetPlayerTarget(targetToUse, targetToUse);
        manager.RefreshLocalCameraInput();

        Debug.Log($"[PlayerCameraInitializer] Camera rebound after scene load for {(IsHost ? "Host" : "Client")} player.");
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
