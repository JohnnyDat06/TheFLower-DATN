using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct StepConfig
{
    public string StepName;
    public List<Transform> PointAnchors; // Kéo các Empty Object từ Hierarchy vào đây
    public GameObject VisualPrefab;      // Prefab Mũi tên/Cube Toon
    public bool RequiredByAllPlayers;    // True nếu cần 2 người cùng đứng vào
}

public class TutorialManager : NetworkBehaviour
{
    public static TutorialManager Instance { get; private set; }

    [Header("Tutorial Sequence")]
    [SerializeField] private List<StepConfig> _steps;
    [SerializeField] private bool _autoStart = true;

    [Header("State (Networked)")]
    private NetworkVariable<int> _currentStepIndex = new NetworkVariable<int>(-1);
    private NetworkVariable<ulong> _playersAtPointMask = new NetworkVariable<ulong>(0);

    private List<TutorialPoint> _activePoints = new List<TutorialPoint>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && _autoStart) StartTutorial();

        _currentStepIndex.OnValueChanged += (oldVal, newVal) =>
        {
            if (newVal >= 0) SetupStepLocal(newVal);
        };

        if (_currentStepIndex.Value >= 0) SetupStepLocal(_currentStepIndex.Value);
    }

    public void StartTutorial()
    {
        if (!IsServer) return;
        _currentStepIndex.Value = 0;
    }

    private void SetupStepLocal(int stepIndex)
    {
        // Xóa các điểm cũ
        foreach (var p in _activePoints) if (p != null) p.Disappear();
        _activePoints.Clear();

        if (stepIndex >= _steps.Count)
        {
            Debug.Log("<color=green>Tutorial All Completed!</color>");
            return;
        }

        var step = _steps[stepIndex];

        // Tạo các điểm mới tại vị trí của các Anchor
        for (int i = 0; i < step.PointAnchors.Count; i++)
        {
            Transform anchor = step.PointAnchors[i];
            if (anchor == null) continue;

            var go = Instantiate(step.VisualPrefab, anchor.position, anchor.rotation);
            var point = go.GetComponent<TutorialPoint>();
            if (point == null) point = go.AddComponent<TutorialPoint>();

            point.PointIndex = i;
            point.OnPlayerReached += ReportPointReachedServerRpc;
            _activePoints.Add(point);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReportPointReachedServerRpc(int pointIndex, ulong clientId)
    {
        if (_currentStepIndex.Value < 0 || _currentStepIndex.Value >= _steps.Count) return;

        var step = _steps[_currentStepIndex.Value];
        if (step.RequiredByAllPlayers)
        {
            _playersAtPointMask.Value |= (1UL << (int)clientId);
            int connectedCount = NetworkManager.Singleton.ConnectedClients.Count;
            ulong targetMask = (1UL << connectedCount) - 1;

            if ((_playersAtPointMask.Value & targetMask) == targetMask) AdvanceStep();
        }
        else
        {
            AdvanceStep();
        }
    }

    private void AdvanceStep()
    {
        if (!IsServer) return;
        _playersAtPointMask.Value = 0;
        _currentStepIndex.Value++;
    }
}
