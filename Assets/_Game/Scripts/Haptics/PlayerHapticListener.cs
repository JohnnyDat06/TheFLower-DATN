using Unity.Netcode;
using UnityEngine;

/// <summary>
/// PlayerHapticListener — Điểm kết nối duy nhất giữa game events và HapticService.
/// Subscribe vào PlayerStateMachine.OnStateChanged và các EventBus event để yêu cầu rung.
/// Chỉ chạy trên client sở hữu (IsOwner). Không sửa bất kỳ PlayerState nào.
/// SRP: chỉ lắng nghe event và ủy thác cho HapticService.
/// </summary>
public class PlayerHapticListener : NetworkBehaviour
{
    [Header("Service")]
    [SerializeField]
    [Tooltip("HapticService trên cùng GameObject với listener này.")]
    private HapticService _hapticService;

    [Header("Movement Profiles")]
    [SerializeField] private SOHapticProfile _jumpProfile;
    [SerializeField] private SOHapticProfile _runProfile;
    [SerializeField] private SOHapticProfile _slideProfile;
    [SerializeField] private SOHapticProfile _glideProfile;

    [Header("Combat / Health Profiles")]
    [SerializeField] private SOHapticProfile _damageProfile;
    [SerializeField] private SOHapticProfile _deathProfile;

    [Header("Gameplay Profiles")]
    [SerializeField] private SOHapticProfile _puzzleProfile;
    [SerializeField] private SOHapticProfile _questDoneProfile;

    private PlayerStateMachine _fsm;

    private void Awake()
    {
        _fsm = GetComponent<PlayerStateMachine>();

        if (_hapticService == null)
            Debug.LogError("[PlayerHapticListener] HapticService chưa được gán trong Inspector!");

        if (_fsm == null)
            Debug.LogError("[PlayerHapticListener] PlayerStateMachine không tìm thấy trên GameObject này!");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Chỉ owner mới cần nhận phản hồi xúc giác của chính mình
        if (!IsOwner) return;

        if (_fsm != null)
            _fsm.OnStateChanged += OnPlayerStateChanged;

        EventBus.OnPlayerTookDamage += OnPlayerTookDamage;
        EventBus.OnPlayerDied       += OnPlayerDied;
        EventBus.OnInteractableActivated += OnInteractableActivated;
        EventBus.OnQuestStepCompleted    += OnQuestStepCompleted;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (_fsm != null)
            _fsm.OnStateChanged -= OnPlayerStateChanged;

        EventBus.OnPlayerTookDamage      -= OnPlayerTookDamage;
        EventBus.OnPlayerDied            -= OnPlayerDied;
        EventBus.OnInteractableActivated -= OnInteractableActivated;
        EventBus.OnQuestStepCompleted    -= OnQuestStepCompleted;
    }

    // ─── State Handlers ──────────────────────────────────────────────────────

    private void OnPlayerStateChanged(PlayerStateType from, PlayerStateType to)
    {
        switch (to)
        {
            case PlayerStateType.Jump:
            case PlayerStateType.DoubleJump:
            case PlayerStateType.WallJump:
                _hapticService.Request(_jumpProfile);
                break;

            case PlayerStateType.Run:
                // Rung 1 lần duy nhất khi bắt đầu chạy (không lặp)
                _hapticService.Request(_runProfile);
                break;

            case PlayerStateType.GroundSlide:
                _hapticService.Request(_slideProfile);
                break;

            case PlayerStateType.AirGlide:
                _hapticService.Request(_glideProfile);
                break;
        }
    }

    // ─── EventBus Handlers ──────────────────────────────────────────────────

    private void OnPlayerTookDamage(ulong clientId)
    {
        // Đã được filter IsOwner tại PlayerHealth, guard thêm cho chắc
        if (clientId != OwnerClientId) return;
        _hapticService.Request(_damageProfile);
    }

    private void OnPlayerDied(ulong clientId)
    {
        if (clientId != OwnerClientId) return;
        _hapticService.Request(_deathProfile);
    }

    private void OnInteractableActivated(string interactableId)
    {
        // Rung khi bất kỳ puzzle/interactable nào được kích hoạt
        _hapticService.Request(_puzzleProfile);
    }

    private void OnQuestStepCompleted(int index, string stepId)
    {
        _hapticService.Request(_questDoneProfile);
    }
}