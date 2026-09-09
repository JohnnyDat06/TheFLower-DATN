using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>Điều khiển một thùng gỗ nhận sóng xung kích và kích hoạt Seal tương ứng.</summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class RuneController : MonoBehaviour
{
    [Tooltip("Bán kính vùng sóng xung kích phải đi qua để phá thùng gỗ.")]
    [SerializeField, Min(0.1f)] private float _shockwaveTriggerRadius = 0.9f;
    [Tooltip("Thời gian Seal chờ người chơi tương tác trước khi thùng gỗ xuất hiện lại ở một ô sàn an toàn.")]
    [SerializeField, Min(0.1f)] private float _chargedDuration = 3f;
    [Tooltip("Tên object model thùng gỗ nằm dưới Rune trong Hierarchy.")]
    [SerializeField] private string _barrelVisualName = "ThungGo_Model";

    private SphereCollider _shockwaveTrigger;
    private GameObject _barrelVisual;
    private RuneManager _manager;
    private Vector3 _initialWorldPosition;
    private float _chargedUntil;

    /// <summary>Trạng thái hiện tại của thùng gỗ.</summary>
    public RuneState State { get; private set; } = RuneState.Inactive;

    /// <summary>Phát ra sau khi trạng thái thùng gỗ thay đổi thành công.</summary>
    public event Action<RuneController, RuneState> StateChanged;

    private void Awake()
    {
        _initialWorldPosition = transform.position;
        _shockwaveTrigger = GetComponent<SphereCollider>();
        _shockwaveTrigger.isTrigger = true;
        _shockwaveTrigger.radius = _shockwaveTriggerRadius;
        _manager = GetComponentInParent<RuneManager>();
        CacheBarrelVisual();
        ApplyVisualState();
    }

    private void Update()
    {
        if (!IsServerAuthority() || State != RuneState.Charged || Time.time < _chargedUntil) return;

        if (_manager == null) _manager = GetComponentInParent<RuneManager>();
        if (_manager != null) _manager.RespawnTimedOutRune(this);
        else ResetRune();

        Debug.Log($"[RuneController] {name} hết thời gian chờ và đã xuất hiện lại.", this);
    }

    /// <summary>Ẩn thùng gỗ sau khi bị sóng xung kích chạm vào.</summary>
    public bool TryCharge()
    {
        if (State != RuneState.Inactive) return false;

        State = RuneState.Charged;
        _chargedUntil = Time.time + _chargedDuration;
        ApplyVisualState();
        StateChanged?.Invoke(this, State);
        return true;
    }

    /// <summary>Đánh dấu thùng đã được Seal tương ứng sử dụng.</summary>
    public bool TryConsume()
    {
        if (State != RuneState.Charged) return false;

        State = RuneState.Consumed;
        _chargedUntil = 0f;
        ApplyVisualState();
        StateChanged?.Invoke(this, State);
        return true;
    }

    /// <summary>Cho thùng xuất hiện lại tại vị trí hiện tại.</summary>
    public void ResetRune()
    {
        RuneState previousState = State;
        State = RuneState.Inactive;
        _chargedUntil = 0f;
        ApplyVisualState();
        if (previousState != State) StateChanged?.Invoke(this, State);
    }

    /// <summary>Cho thùng xuất hiện lại tại một vị trí sàn an toàn do Host chọn.</summary>
    public void ResetRuneAt(Vector3 worldPosition)
    {
        transform.position = worldPosition;
        ResetRune();
    }

    /// <summary>Đưa thùng về vị trí ban đầu khi bắt đầu một chu kỳ Boss mới.</summary>
    public void RestoreInitialPosition()
    {
        transform.position = _initialWorldPosition;
        ResetRune();
    }

    /// <summary>Áp dụng vị trí thùng do Host đồng bộ sang Client.</summary>
    public void ApplyNetworkPosition(Vector3 worldPosition)
    {
        if ((transform.position - worldPosition).sqrMagnitude < 0.000001f) return;
        transform.position = worldPosition;
    }

    /// <summary>Áp dụng trạng thái do Host sở hữu và cập nhật model trên Client.</summary>
    public void ApplyNetworkState(RuneState state)
    {
        if (State == state)
        {
            ApplyVisualState();
            return;
        }

        State = state;
        _chargedUntil = 0f;
        ApplyVisualState();
        StateChanged?.Invoke(this, State);
    }

    [ContextMenu("Debug/Phá thùng bằng sóng xung kích")]
    private void ChargeRuneForDebug() => TryCharge();

    [ContextMenu("Debug/Khôi phục thùng")]
    private void ResetRuneForDebug() => ResetRune();

    private void OnTriggerEnter(Collider other)
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;
        if (other.GetComponentInParent<ShockwaveHitbox>() == null) return;

        if (_manager == null) _manager = GetComponentInParent<RuneManager>();
        _manager?.TryChargeRune(this);
    }

    private void CacheBarrelVisual()
    {
        Transform visual = transform.Find(_barrelVisualName);
        if (visual == null)
        {
            Renderer renderer = GetComponentInChildren<Renderer>(true);
            visual = renderer != null ? renderer.transform : null;
        }

        _barrelVisual = visual != null ? visual.gameObject : null;
        if (_barrelVisual == null)
            Debug.LogError($"[RuneController] Không tìm thấy model thùng gỗ '{_barrelVisualName}' cho {name}.", this);
    }

    private void ApplyVisualState()
    {
        if (_barrelVisual == null) CacheBarrelVisual();
        if (_barrelVisual != null) _barrelVisual.SetActive(State == RuneState.Inactive);
    }

    private static bool IsServerAuthority() =>
        NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
}
