using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public enum QuestCompletionScope : byte { AnyPlayer, AllPlayers }

[Serializable]
public class QuestRouteStep
{
    [Tooltip("ID duy nhất của bước trong route.")]
    public string id = "step_01";
    public string displayName = "Đến điểm tiếp theo";
    [TextArea] public string description;
    [Tooltip("Transform của điểm đích. Có thể là QuestTarget hoặc một object bất kỳ.")]
    public Transform destination;
    [Min(0.1f)] public float completionRadius = 3f;
    [Tooltip("Nếu bật, người chơi phải gọi Interact tại QuestTarget thay vì chỉ đi vào vùng.")]
    public bool requiresInteraction;
    [Tooltip("ID của QuestTarget cần tương tác. Để trống sẽ dùng id của step.")]
    public string interactionTargetId;
}

/// <summary>
/// Quản lý một chuỗi nhiệm vụ tuyến tính. Setup chủ yếu bằng cách kéo Transform vào routeSteps.
/// Server là nguồn sự thật; client chỉ gửi yêu cầu tương tác và hiển thị state.
/// </summary>
public class QuestRouteManager : NetworkBehaviour
{
    [Header("Route Setup")]
    [SerializeField] private List<QuestRouteStep> routeSteps = new();
    [SerializeField] private QuestCompletionScope completionScope = QuestCompletionScope.AnyPlayer;
    [SerializeField] private bool startOnNetworkSpawn = true;
    [SerializeField] private bool loopRoute;

    [Header("Events")]
    public UnityEvent<int> onStepChanged;
    public UnityEvent<int> onStepCompleted;
    public UnityEvent onRouteCompleted;

    private readonly NetworkVariable<int> currentStep = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> routeCompleted = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly HashSet<ulong> playersInside = new();

    public IReadOnlyList<QuestRouteStep> Steps => routeSteps;
    public int CurrentStepIndex => currentStep.Value;
    public bool IsRouteCompleted => routeCompleted.Value;
    public QuestRouteStep CurrentStep => IsValidStep(currentStep.Value) ? routeSteps[currentStep.Value] : null;
    public event Action<int> StepChanged;
    public event Action<int> StepCompleted;

    private void Awake()
    {
        currentStep.OnValueChanged += HandleStepChanged;
        routeCompleted.OnValueChanged += HandleRouteCompleted;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        EventBus.OnInteractableActivated += HandleInteractableActivated;
        if (IsServer && startOnNetworkSpawn && currentStep.Value < 0 && routeSteps.Count > 0)
            currentStep.Value = 0;
        NotifyCurrentStep();
    }

    public override void OnNetworkDespawn()
    {
        EventBus.OnInteractableActivated -= HandleInteractableActivated;
        base.OnNetworkDespawn();
    }

    private void OnDestroy()
    {
        currentStep.OnValueChanged -= HandleStepChanged;
        routeCompleted.OnValueChanged -= HandleRouteCompleted;
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned || routeCompleted.Value || CurrentStep == null || CurrentStep.requiresInteraction)
            return;

        playersInside.Clear();
        foreach (var pair in NetworkManager.ConnectedClients)
        {
            var player = pair.Value.PlayerObject;
            if (player != null && Vector3.Distance(player.transform.position, CurrentStep.destination.position) <= CurrentStep.completionRadius)
                playersInside.Add(pair.Key);
        }

        bool complete = completionScope == QuestCompletionScope.AnyPlayer
            ? playersInside.Count > 0
            : NetworkManager.ConnectedClients.Count > 0 && playersInside.Count == NetworkManager.ConnectedClients.Count;
        if (complete) CompleteCurrentStepServer();
    }

    private void HandleInteractableActivated(string interactableId)
    {
        if (!IsServer || routeCompleted.Value || CurrentStep == null || !CurrentStep.requiresInteraction) return;
        string expected = string.IsNullOrWhiteSpace(CurrentStep.interactionTargetId) ? CurrentStep.id : CurrentStep.interactionTargetId;
        if (string.Equals(expected, interactableId, StringComparison.Ordinal)) CompleteCurrentStepServer();
    }

    [ContextMenu("Start Route")]
    public void StartRoute()
    {
        if (IsServer) StartRouteServer();
        else if (IsClient) StartRouteServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void StartRouteServerRpc() => StartRouteServer();

    private void StartRouteServer()
    {
        routeCompleted.Value = false;
        currentStep.Value = routeSteps.Count > 0 ? 0 : -1;
    }

    public void CompleteStepFromGameplay(string stepId)
    {
        if (!IsServer || CurrentStep == null || CurrentStep.id != stepId) return;
        CompleteCurrentStepServer();
    }

    private void CompleteCurrentStepServer()
    {
        if (!IsValidStep(currentStep.Value)) return;
        int completedIndex = currentStep.Value;
        StepCompleted?.Invoke(completedIndex);
        onStepCompleted?.Invoke(completedIndex);
        EventBus.RaiseQuestStepCompleted(completedIndex, routeSteps[completedIndex].id);

        int next = completedIndex + 1;
        if (next >= routeSteps.Count)
        {
            if (loopRoute && routeSteps.Count > 0) currentStep.Value = 0;
            else routeCompleted.Value = true;
        }
        else currentStep.Value = next;
    }

    private void HandleStepChanged(int _, int __) => NotifyCurrentStep();
    private void HandleRouteCompleted(bool _, bool completed)
    {
        if (completed) { onRouteCompleted?.Invoke(); EventBus.RaiseQuestRouteCompleted(); }
        NotifyCurrentStep();
    }

    private void NotifyCurrentStep()
    {
        if (!IsValidStep(currentStep.Value)) return;
        onStepChanged?.Invoke(currentStep.Value);
        StepChanged?.Invoke(currentStep.Value);
        EventBus.RaiseQuestStepChanged(currentStep.Value, routeSteps[currentStep.Value].id);
    }

    private bool IsValidStep(int index) => index >= 0 && index < routeSteps.Count && routeSteps[index] != null && routeSteps[index].destination != null;

    private void OnDrawGizmosSelected()
    {
        if (routeSteps == null) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < routeSteps.Count; i++)
        {
            if (routeSteps[i]?.destination == null) continue;
            Gizmos.DrawWireSphere(routeSteps[i].destination.position, routeSteps[i].completionRadius);
            if (i + 1 < routeSteps.Count && routeSteps[i + 1]?.destination != null)
                Gizmos.DrawLine(routeSteps[i].destination.position, routeSteps[i + 1].destination.position);
        }
    }
}
