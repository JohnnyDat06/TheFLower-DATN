using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Authoritatively completes the Sand Boat chase once the boat reaches the
/// TempleFinish trigger. Phase 19 releases player control on the stopped boat;
/// players choose when to jump down before the existing boss transition.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[DisallowMultipleComponent]
public sealed class SandBoatChaseCompletion : NetworkBehaviour
{
    [Header("Điểm kết thúc")]
    [SerializeField, Tooltip("Trigger TempleFinish chỉ được Server dùng để xác nhận Sand Boat đã tới đền.")]
    private Collider _templeFinishTrigger;
    [SerializeField, Tooltip("Trigger VaoDen báo rằng người chơi đã vào nơi trú trong đền và bão có thể dừng.")]
    private Collider _templeShelterTrigger;
    [SerializeField, Tooltip("Chuyển động Route được đặt đúng điểm kết thúc và khóa lại khi hoàn thành chase.")]
    private SandBoatMovement _movement;
    [SerializeField, Tooltip("Boarding hiện có khóa chase input và trả Player Control khi thuyền đã dừng ở TempleFinish.")]
    private SandBoatBoarding _boarding;
    [SerializeField, Tooltip("Collider boong chỉ bật tại TempleFinish để Player đứng trên thuyền mà không ảnh hưởng camera trong lúc chase.")]
    private Collider _deckCollider;

    [Header("Khóa gameplay Chase")]
    [SerializeField, Tooltip("Controller đánh lái P1 bị tắt sau khi Sand Boat vào đền.")]
    private SandBoatSteering _steering;
    [SerializeField, Tooltip("Controller tốc độ P2 bị tắt sau khi Sand Boat vào đền.")]
    private SandBoatSpeedController _speedController;
    [SerializeField, Tooltip("Logic áp lực bão vẫn giữ nguyên sau TempleFinish và chỉ dừng khi người chơi đi vào VaoDen.")]
    private SandstormChaseController _stormLogic;
    [SerializeField, Tooltip("Hiệu ứng bão vẫn hiện ngoài đền sau TempleFinish và chỉ ẩn khi người chơi vào VaoDen.")]
    private SandstormVisualController _stormVisual;
    [SerializeField, Tooltip("Controller đá bão bị tắt và dọn các đá/cảnh báo còn lại khi chase hoàn thành.")]
    private SandstormRockAttackController _stormRockAttack;
    [SerializeField, Tooltip("Controller thất bại của chase; được bật lại nếu bão bắt Player trước khi vào VaoDen.")]
    private SandBoatChaseFailController _failController;
    [SerializeField, Tooltip("Xử lý va chạm đá bị tắt sau TempleFinish vì gameplay Chase đã kết thúc.")]
    private SandBoatCollisionHandler _collisionHandler;

    [Header("Cổng đền")]
    [SerializeField, Tooltip("Mảnh cổng có sẵn trong scene sẽ hạ xuống để đóng lối sau khi Sand Boat đã vào đền.")]
    private Transform _gatePanel;
    [SerializeField, Tooltip("Độ lệch local của cổng ở trạng thái mở trước khi chase hoàn thành.")]
    private Vector3 _gateOpenLocalOffset = new(0f, 8f, 0f);
    [SerializeField, Min(0f), Tooltip("Số giây cổng chạy từ vị trí mở về vị trí đóng. 0 là đóng ngay.")]
    private float _gateCloseDuration = 1.25f;

    [Header("Bão bắt Player sau TempleFinish")]
    [SerializeField, Min(0.1f), Tooltip("Bán kính theo mặt phẳng XZ quanh tâm visual bão; Player đi vào vùng này trước VaoDen sẽ chơi lại chase.")]
    private float _templeStormCatchRadius = 6f;

    [Header("Runtime Debug")]
    [SerializeField, Tooltip("Trạng thái authoritative hiện tại của Sand Boat Chase. Phase 18 kết thúc ở Completed.")]
    private SandBoatChaseState _chaseState;

    private readonly NetworkVariable<SandBoatChaseState> _chaseStateNetwork = new(
        SandBoatChaseState.InProgress,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _stormStoppedAtShelter = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Vector3 _gateClosedLocalPosition;
    private Coroutine _gateRoutine;
    private bool _postFinishFailureTriggered;

    /// <summary>Current authoritative state of the Sand Boat chase.</summary>
    public SandBoatChaseState ChaseState => _chaseStateNetwork.Value;

    /// <summary>True after the server has completed the current chase attempt.</summary>
    public bool IsChaseCompleted => ChaseState == SandBoatChaseState.Completed;

    /// <summary>True after a player has reached VaoDen and the storm is safely stopped.</summary>
    public bool IsStormStoppedAtShelter => _stormStoppedAtShelter.Value;

    private void Awake()
    {
        if (_gatePanel != null)
        {
            _gateClosedLocalPosition = _gatePanel.localPosition;
            _gatePanel.localPosition = _gateClosedLocalPosition + _gateOpenLocalOffset;
        }
    }

    private void OnValidate()
    {
        _gateCloseDuration = Mathf.Max(0f, _gateCloseDuration);
        _templeStormCatchRadius = Mathf.Max(0.1f, _templeStormCatchRadius);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _chaseStateNetwork.OnValueChanged += OnChaseStateChanged;
        _stormStoppedAtShelter.OnValueChanged += OnStormStoppedAtShelterChanged;
        ApplyChaseState(_chaseStateNetwork.Value);
        ApplyStormShelterState(_stormStoppedAtShelter.Value);
    }

    public override void OnNetworkDespawn()
    {
        _chaseStateNetwork.OnValueChanged -= OnChaseStateChanged;
        _stormStoppedAtShelter.OnValueChanged -= OnStormStoppedAtShelterChanged;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!Application.isPlaying
            || !IsServer
            || !IsChaseCompleted
            || IsStormStoppedAtShelter
            || _postFinishFailureTriggered
            || _stormVisual == null
            || !_stormVisual.TryGetVisualPosition(out Vector3 stormPosition)
            || NetworkManager.Singleton == null)
        {
            return;
        }

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null)
            {
                continue;
            }

            if (IsPlayerInsideShelter(playerObject))
            {
                StopStormAtShelterServer();
                return;
            }

            Vector2 stormPositionXZ = new(stormPosition.x, stormPosition.z);
            Vector2 playerPositionXZ = new(playerObject.transform.position.x, playerObject.transform.position.z);
            if (Vector2.Distance(stormPositionXZ, playerPositionXZ) > _templeStormCatchRadius)
            {
                continue;
            }

            _postFinishFailureTriggered = true;
            if (_failController != null)
            {
                _failController.enabled = true;
                _failController.TriggerTempleStormFail();
            }

            return;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!Application.isPlaying
            || !IsServer
            || IsChaseCompleted
            || _templeFinishTrigger == null
            || other != _templeFinishTrigger)
        {
            return;
        }

        _chaseStateNetwork.Value = SandBoatChaseState.Completed;
        ApplyChaseState(SandBoatChaseState.Completed);
    }

    /// <summary>
    /// Called by the VaoDen trigger on the local owning player. The server
    /// verifies that sender's player is actually inside the authored shelter.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestStormStopAtShelterServerRpc(RpcParams rpcParams = default)
    {
        if (!IsChaseCompleted || _stormStoppedAtShelter.Value || !IsSenderInsideShelter(rpcParams.Receive.SenderClientId))
        {
            return;
        }

        StopStormAtShelterServer();
    }

    private void OnChaseStateChanged(SandBoatChaseState previousValue, SandBoatChaseState currentValue)
    {
        ApplyChaseState(currentValue);
    }

    private void OnStormStoppedAtShelterChanged(bool previousValue, bool currentValue)
    {
        ApplyStormShelterState(currentValue);
    }

    private void ApplyChaseState(SandBoatChaseState nextState)
    {
        _chaseState = nextState;
        if (nextState != SandBoatChaseState.Completed)
        {
            ApplyRetryReadyState();
            return;
        }

        _movement?.CompleteRoute();
        if (_deckCollider != null)
        {
            _deckCollider.enabled = true;
        }

        if (IsServer)
        {
            _boarding?.EndChaseAndReleasePlayersOnBoat();
        }

        if (_steering != null)
        {
            _steering.enabled = false;
        }

        if (_speedController != null)
        {
            _speedController.enabled = false;
        }

        _stormVisual?.SetVisibleDuringTempleTransition(true);

        if (_stormRockAttack != null)
        {
            _stormRockAttack.enabled = false;
        }

        if (_failController != null)
        {
            _failController.enabled = false;
        }

        if (_collisionHandler != null)
        {
            _collisionHandler.enabled = false;
        }

        CloseGate();
    }

    private void ApplyStormShelterState(bool isStoppedAtShelter)
    {
        if (!isStoppedAtShelter)
        {
            return;
        }

        if (_stormLogic != null)
        {
            _stormLogic.enabled = false;
        }

        if (_stormRockAttack != null)
        {
            _stormRockAttack.enabled = false;
        }

        _stormVisual?.SetVisibleDuringTempleTransition(false);
    }

    /// <summary>Server-only reset hook used by the existing chase checkpoint transaction.</summary>
    public void ResetCompletionForRetry()
    {
        if (!IsServer)
        {
            return;
        }

        _postFinishFailureTriggered = false;
        _stormStoppedAtShelter.Value = false;
        _chaseStateNetwork.Value = SandBoatChaseState.InProgress;
        ApplyChaseState(SandBoatChaseState.InProgress);
    }

    private void ApplyRetryReadyState()
    {
        if (_deckCollider != null)
        {
            _deckCollider.enabled = false;
        }

        if (_stormLogic != null)
        {
            _stormLogic.enabled = true;
        }

        if (_stormRockAttack != null)
        {
            _stormRockAttack.enabled = true;
        }

        if (_failController != null)
        {
            _failController.enabled = true;
        }

        if (_collisionHandler != null)
        {
            _collisionHandler.enabled = true;
        }

        _stormVisual?.SetVisibleDuringTempleTransition(false);
        OpenGateInstantly();
    }

    private void StopStormAtShelterServer()
    {
        if (!IsServer || _stormStoppedAtShelter.Value)
        {
            return;
        }

        _stormStoppedAtShelter.Value = true;
        ApplyStormShelterState(true);
    }

    private bool IsSenderInsideShelter(ulong senderClientId)
    {
        if (_templeShelterTrigger == null || NetworkManager.Singleton == null)
        {
            return false;
        }

        NetworkObject playerObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(senderClientId);
        if (playerObject == null)
        {
            return false;
        }

        return IsPlayerInsideShelter(playerObject);
    }

    private bool IsPlayerInsideShelter(NetworkObject playerObject)
    {
        if (_templeShelterTrigger == null || playerObject == null)
        {
            return false;
        }

        Bounds shelterBounds = _templeShelterTrigger.bounds;
        shelterBounds.Expand(1f);
        return shelterBounds.Contains(playerObject.transform.position);
    }

    private void OpenGateInstantly()
    {
        if (_gatePanel == null)
        {
            return;
        }

        if (_gateRoutine != null)
        {
            StopCoroutine(_gateRoutine);
            _gateRoutine = null;
        }

        _gatePanel.localPosition = _gateClosedLocalPosition + _gateOpenLocalOffset;
    }

    private void CloseGate()
    {
        if (_gatePanel == null)
        {
            return;
        }

        if (_gateRoutine != null)
        {
            StopCoroutine(_gateRoutine);
        }

        _gateRoutine = StartCoroutine(CloseGateRoutine());
    }

    private IEnumerator CloseGateRoutine()
    {
        Vector3 startPosition = _gatePanel.localPosition;
        if (_gateCloseDuration <= 0f)
        {
            _gatePanel.localPosition = _gateClosedLocalPosition;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < _gateCloseDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / _gateCloseDuration);
            _gatePanel.localPosition = Vector3.Lerp(startPosition, _gateClosedLocalPosition, Mathf.SmoothStep(0f, 1f, progress));
            yield return null;
        }

        _gatePanel.localPosition = _gateClosedLocalPosition;
        _gateRoutine = null;
    }
}

/// <summary>Authoritative lifecycle state used by the Sand Boat chase completion flow.</summary>
public enum SandBoatChaseState
{
    InProgress,
    Completed
}
