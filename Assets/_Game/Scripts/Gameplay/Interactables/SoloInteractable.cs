using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class SoloInteractable : InteractableBase
{
    [SerializeField, Tooltip("Bật để tránh một cần gạt đóng Animator vẫn đang được cần gạt khác giữ kích hoạt.")]
    private bool _coordinateSharedAnimators = true;

    [SerializeField, Tooltip("Trigger dùng để bật Animator chung khi còn ít nhất một cần gạt được kích hoạt.")]
    private string _sharedActivateTrigger = "Rise";

    [SerializeField, Tooltip("Trigger dùng để tắt Animator chung khi tất cả cần gạt đã ngừng kích hoạt.")]
    private string _sharedDeactivateTrigger = "Close";

    private ulong _lastInteractedPlayerId;
    private bool _hasActivatingPlayer = false;

    internal bool CoordinatesSharedAnimators => _coordinateSharedAnimators;
    internal string SharedActivateTrigger => _sharedActivateTrigger;
    internal string SharedDeactivateTrigger => _sharedDeactivateTrigger;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // Luôn cho phép kích hoạt lại để có thể mở/đóng nhiều lần
        if (IsServer)
        {
            _allowReactivation = true;
        }

        if (_coordinateSharedAnimators)
        {
            SharedLeverAnimatorCoordinator.RefreshSharedAnimators(this);
        }
    }

    public override void Interact(ulong playerId)
    {
        if (!CanInteract) return;
        ActivateServerRpc(playerId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ActivateServerRpc(ulong playerId)
    {
        if (!CanInteract) return;
        if (!CanPlayerInteract(playerId)) return;

        Debug.Log($"[SoloInteractable] {_interactableId} - Kích hoạt bởi Player {playerId}");
        
        _lastInteractedPlayerId = playerId;
        _hasActivatingPlayer = true;
        ServerActivate();
    }

    private void Update()
    {
        // Chỉ xử lý trên Server và khi đang được bật
        if (!IsServer || !IsActivated || !_hasActivatingPlayer) return;

        // Kiểm tra khoảng cách thực tế từ Player đến cần gạt
        if (!CheckPlayerInRange(_lastInteractedPlayerId))
        {
            Debug.Log($"[SoloInteractable] {_interactableId} - Player {_lastInteractedPlayerId} đã rời xa quá {_maxInteractDistance}m. Đang đóng cầu...");
            _hasActivatingPlayer = false;
            ServerDeactivate();
        }
    }

    private bool CheckPlayerInRange(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return false;
        if (client.PlayerObject == null) return false;

        Vector3 playerPosition = client.PlayerObject.transform.position;
        Vector3 interactionPoint = GetInteractionPoint(playerPosition);
        float distance = Vector3.Distance(interactionPoint, playerPosition);
        
        // Trả về true nếu vẫn còn trong phạm vi
        return distance <= (_maxInteractDistance + 0.5f); // Thêm một chút bù trừ sai số
    }

    protected override void OnActivatedValueChanged(bool previousValue, bool newValue)
    {
        base.OnActivatedValueChanged(previousValue, newValue);
        
        Debug.Log($"[SoloInteractable] {_interactableId} thay đổi trạng thái: {previousValue} -> {newValue}");

        if (!newValue)
        {
            _hasActivatingPlayer = false;
        }

        if (_coordinateSharedAnimators)
        {
            SharedLeverAnimatorCoordinator.RefreshSharedAnimators(this);
        }
    }
}

/// <summary>
/// Giải quyết xung đột khi nhiều SoloInteractable cùng điều khiển một Animator.
/// </summary>
internal static class SharedLeverAnimatorCoordinator
{
    public static void RefreshSharedAnimators(SoloInteractable sourceLever)
    {
        if (sourceLever == null || !sourceLever.CoordinatesSharedAnimators) return;

        SoloInteractable[] sceneLevers = Object.FindObjectsByType<SoloInteractable>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (Animator animator in GetAnimatorTargets(sourceLever.OnActivated))
        {
            int controllingLeverCount = 0;
            bool shouldActivate = false;

            foreach (SoloInteractable lever in sceneLevers)
            {
                if (lever == null || !lever.CoordinatesSharedAnimators) continue;
                if (!ReferencesAnimator(lever.OnActivated, animator)) continue;

                controllingLeverCount++;
                shouldActivate |= lever.IsActivated;
            }

            if (controllingLeverCount < 2) continue;

            PlayAggregateState(
                animator,
                shouldActivate,
                sourceLever.SharedActivateTrigger,
                sourceLever.SharedDeactivateTrigger);
        }
    }

    private static IEnumerable<Animator> GetAnimatorTargets(UnityEvent unityEvent)
    {
        var uniqueTargets = new HashSet<Animator>();
        if (unityEvent == null) return uniqueTargets;

        for (int index = 0; index < unityEvent.GetPersistentEventCount(); index++)
        {
            if (unityEvent.GetPersistentMethodName(index) != nameof(Animator.SetTrigger)) continue;
            if (unityEvent.GetPersistentTarget(index) is Animator animator)
            {
                uniqueTargets.Add(animator);
            }
        }

        return uniqueTargets;
    }

    private static bool ReferencesAnimator(UnityEvent unityEvent, Animator targetAnimator)
    {
        if (unityEvent == null || targetAnimator == null) return false;

        for (int index = 0; index < unityEvent.GetPersistentEventCount(); index++)
        {
            if (unityEvent.GetPersistentMethodName(index) != nameof(Animator.SetTrigger)) continue;
            if (unityEvent.GetPersistentTarget(index) == targetAnimator) return true;
        }

        return false;
    }

    private static void PlayAggregateState(
        Animator animator,
        bool shouldActivate,
        string activateTrigger,
        string deactivateTrigger)
    {
        string triggerToReset = shouldActivate ? deactivateTrigger : activateTrigger;
        string triggerToPlay = shouldActivate ? activateTrigger : deactivateTrigger;

        if (!string.IsNullOrWhiteSpace(triggerToReset))
        {
            animator.ResetTrigger(triggerToReset);
        }

        if (!string.IsNullOrWhiteSpace(triggerToPlay))
        {
            animator.SetTrigger(triggerToPlay);
        }
    }
}
