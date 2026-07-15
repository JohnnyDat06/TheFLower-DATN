using Unity.Netcode;
using UnityEngine;
using System.Linq;
using UnityEngine.SceneManagement;

/// <summary>
/// PlayerModel — VIP Update: Tự động kích hoạt bản thân và xếp chỗ đứng.
/// </summary>
public class PlayerModel : NetworkBehaviour
{
    [Header("Mesh")]
    [SerializeField] private GameObject _meshMale;
    [SerializeField] private GameObject _meshFemale;

    [Header("Animator Controllers")]
    [SerializeField] private RuntimeAnimatorController _hostAnimatorController;
    [SerializeField] private RuntimeAnimatorController _clientAnimatorController;
    
    [Header("Avatar Animator")]
    [SerializeField] private Avatar _avatarMaleAnimator;
    [SerializeField] private Avatar _avatarFemaleAnimator;
    
    [SerializeField] private Animator _animator;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;

        // 1. CƯỠNG ÉP BẬT ACTIVE CỦA CHÍNH NÓ (Fix lỗi tàng hình do Prefab bị tắt)
        gameObject.SetActive(true);

        // Đã xóa phần gọi AutoPosition() để ngăn chặn việc tự động giật lùi về LobbyAnchor

        // 3. Hiển thị mô hình
        bool isHostPlayer = OwnerClientId == NetworkManager.ServerClientId;
        ApplyModel(isHostPlayer);
    }

    public override void OnNetworkDespawn()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        base.OnNetworkDespawn();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        bool isHostPlayer = NetworkManager != null && OwnerClientId == NetworkManager.ServerClientId;
        ApplyModel(isHostPlayer);
    }

    // Đã XÓA HOÀN TOÀN hàm AutoPosition() vì việc đặt vị trí đã do LobbyPlayerState quản lý (chỉ 1 lần), 
    // và Spawner sẽ lo phần còn lại khi chuyển map.

    private void ApplyModel(bool isHost)
    {
        // Player objects remain spawned for NGO state, but the remade lobby uses 2D portraits.
        if (SceneManager.GetActiveScene().name.Contains("Lobby", System.StringComparison.OrdinalIgnoreCase))
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            return;
        }

        // Bật mesh tương ứng
        if (_meshMale != null) _meshMale.SetActive(true);
        if (_meshFemale != null) _meshFemale.SetActive(true);

        SetRenderersVisible(_meshMale, isHost);
        SetRenderersVisible(_meshFemale, !isHost);

        if (_animator != null)
        {
            var controller = isHost ? _hostAnimatorController : _clientAnimatorController;
            var avatar = isHost ? _avatarMaleAnimator : _avatarFemaleAnimator;

            if (controller == null)
            {
                controller = _hostAnimatorController;
                Debug.LogWarning($"[PlayerModel] Missing {(isHost ? "host" : "client")} controller on {name}. Falling back to host controller.");
            }

            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                avatar = _avatarMaleAnimator;
                Debug.LogWarning($"[PlayerModel] Invalid {(isHost ? "male" : "female")} avatar on {name}. Falling back to male avatar.");
            }

            _animator.runtimeAnimatorController = controller;
            _animator.avatar = avatar;
            _animator.Rebind();
            _animator.Update(0f);

            if (!isHost)
            {
                BindSkinnedMeshesToDriverSkeleton(_meshFemale, _meshMale);
                _animator.Update(0f);
            }
        }

        // 4. ÉP HIỆN HÌNH TẤT CẢ CON (Nếu mesh bị tắt sẵn)
        foreach (var r in GetComponentsInChildren<Renderer>(true)) {
            if (_meshMale != null && r.transform.IsChildOf(_meshMale.transform))
            {
                r.enabled = isHost;
            }
            else if (_meshFemale != null && r.transform.IsChildOf(_meshFemale.transform))
            {
                r.enabled = !isHost;
            }
            else
            {
                r.enabled = true;
            }
        }

        Debug.Log($"[PlayerModel] Forced Active and Applied {(isHost ? "Male" : "Female")} for Player {OwnerClientId}");
    }

    private void SetRenderersVisible(GameObject root, bool visible)
    {
        if (root == null) return;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = visible;
        }
    }

    private void BindSkinnedMeshesToDriverSkeleton(GameObject visualRoot, GameObject driverRoot)
    {
        if (visualRoot == null || driverRoot == null) return;

        var driverBones = driverRoot
            .GetComponentsInChildren<Transform>(true)
            .GroupBy(GetRelativeBonePath)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var skinnedMesh in visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var bones = skinnedMesh.bones;

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;

                var path = GetRelativeBonePath(bones[i]);
                if (driverBones.TryGetValue(path, out var driverBone))
                {
                    bones[i] = driverBone;
                }
            }

            skinnedMesh.bones = bones;

            if (skinnedMesh.rootBone != null &&
                driverBones.TryGetValue(GetRelativeBonePath(skinnedMesh.rootBone), out var driverRootBone))
            {
                skinnedMesh.rootBone = driverRootBone;
            }
        }
    }

    private string GetRelativeBonePath(Transform bone)
    {
        var path = bone.name;
        var current = bone.parent;

        while (current != null && current != _meshMale?.transform && current != _meshFemale?.transform)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
