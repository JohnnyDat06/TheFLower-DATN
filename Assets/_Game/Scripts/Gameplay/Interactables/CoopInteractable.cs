using Unity.Netcode;
using UnityEngine;

public class CoopInteractable : InteractableBase
{
    [Header("Coop Settings")]
    [SerializeField] private Transform _pointA;
    [SerializeField] private Transform _pointB;
    [SerializeField] private float _validDistance = 2f;

    [Header("Animations")]
    [SerializeField] private Animator _animatorA;
    [SerializeField] private Animator _animatorB;
    [SerializeField] private string _animParamName = "IsActive";

    private readonly NetworkVariable<bool> _playerAReady = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);


    public bool PlayerAReady => _playerAReady.Value;
    public bool PlayerBReady => _playerBReady.Value;

    public bool IsPlayerInInteractionZone(ulong playerId)
    {
        if (!TryGetPlayerObject(playerId, out NetworkObject playerObject)) return false;
        Transform point = playerId == NetworkManager.ServerClientId ? _pointA : _pointB;
        return point == null || Vector3.Distance(playerObject.transform.position, point.position) <= _validDistance;
    }

    public Transform GetPromptTransformForPlayer(ulong playerId)
    {
        return playerId == NetworkManager.ServerClientId ? _pointA : _pointB;
    }

    private readonly NetworkVariable<bool> _playerBReady = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _playerAReady.OnValueChanged += OnPlayerAReadyChanged;
        _playerBReady.OnValueChanged += OnPlayerBReadyChanged;
        
        // Initial state
        UpdateLeverAnimations(_playerAReady.Value, _playerBReady.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _playerAReady.OnValueChanged -= OnPlayerAReadyChanged;
        _playerBReady.OnValueChanged -= OnPlayerBReadyChanged;
    }

    public override Transform GetPromptTransform()
    {
        bool isHost = NetworkManager.Singleton.IsHost;
        return isHost ? _pointA : _pointB;
    }

    public override void Interact(ulong playerId)
    {
        if (!CanInteract) return;

        AttemptReadyServerRpc(playerId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void AttemptReadyServerRpc(ulong playerId)
    {
        if (!CanInteract) return;
        if (!TryGetPlayerObject(playerId, out NetworkObject playerObject)) return;

        Vector3 playerPos = playerObject.transform.position;
        bool isHost = playerId == NetworkManager.ServerClientId;
        bool nearA = _pointA == null || Vector3.Distance(playerPos, _pointA.position) <= _validDistance;
        bool nearB = _pointB == null || Vector3.Distance(playerPos, _pointB.position) <= _validDistance;

        if (isHost && nearA)
        {
            _playerAReady.Value = true;
            Debug.Log($"[CoopInteractable] {_interactableId} - Host (A) da san sang.");
        }
        else if (!isHost && nearB)
        {
            _playerBReady.Value = true;
            Debug.Log($"[CoopInteractable] {_interactableId} - Client (B) da san sang.");
        }
        else
        {
            Debug.Log($"[CoopInteractable] {_interactableId} - Player {playerId} bam nhung dung sai vi tri.");
        }

        CheckActivationConditions();
    }

    private void Update()
    {
        if (!IsServer) return;
        if (IsActivated && !_allowReactivation) return;

        // THAY ĐỔI CỐT LÕI: Update CHỈ ĐƯỢC PHÉP dùng để HỦY (Tắt) trạng thái ready 
        // nếu người chơi bỏ đi rông quá xa Point A / Point B. 
        // Tuyệt đối KHÔNG được tự động Bật (gán True) ở đây, Bật (True) chỉ được làm khi bấm [E].
        if (_playerAReady.Value)
        {
            if (!IsPlayerNear(NetworkManager.ServerClientId, true))
            {
                _playerAReady.Value = false;
                Debug.Log($"[CoopInteractable] {_interactableId} - Host roi khoi vi tri -> Huy San Sang.");
            }
        }

        if (_playerBReady.Value)
        {
            ulong clientPlayerId = GetFirstClientId();
            if (clientPlayerId != NetworkManager.ServerClientId && !IsPlayerNear(clientPlayerId, false))
            {
                _playerBReady.Value = false;
                Debug.Log($"[CoopInteractable] {_interactableId} - Client roi khoi vi tri -> Huy San Sang.");
            }
        }
    }

    private ulong GetFirstClientId()
    {
        if (NetworkManager.Singleton == null) return 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.ClientId != NetworkManager.ServerClientId) return client.ClientId;
        }
        return 0;
    }

    private bool IsPlayerNear(ulong playerId, bool isHost)
    {
        if (!TryGetPlayerObject(playerId, out NetworkObject playerObject)) return false;

        Vector3 playerPos = playerObject.transform.position;
        Transform targetPoint = isHost ? _pointA : _pointB;
        
        return targetPoint == null || Vector3.Distance(playerPos, targetPoint.position) <= _validDistance;
    }

    private void CheckActivationConditions()
    {
        if (!IsServer) return;

        if (_playerAReady.Value && _playerBReady.Value)
        {
            Debug.Log($"[CoopInteractable] {_interactableId} - Ca 2 da san sang -> KICH HOAT.");
            ServerActivate();

            if (_allowReactivation)
            {
                _playerAReady.Value = false;
                _playerBReady.Value = false;
            }
        }
    }

    public override void ResetInteractable()
    {
        if (!IsServer) return;
        base.ResetInteractable();
        _playerAReady.Value = false;
        _playerBReady.Value = false;
    }

    private void OnPlayerAReadyChanged(bool previousValue, bool newValue)
    {
        HandleReadinessUI(NetworkManager.ServerClientId, previousValue, newValue);
        if (_animatorA != null) _animatorA.SetBool(_animParamName, newValue);
    }

    private void OnPlayerBReadyChanged(bool previousValue, bool newValue)
    {
        ulong clientPlayerId = GetFirstClientId();
        HandleReadinessUI(clientPlayerId, previousValue, newValue);
        if (_animatorB != null) _animatorB.SetBool(_animParamName, newValue);
    }

    private void UpdateLeverAnimations(bool readyA, bool readyB)
    {
        if (_animatorA != null) _animatorA.SetBool(_animParamName, readyA);
        if (_animatorB != null) _animatorB.SetBool(_animParamName, readyB);
    }

    private void HandleReadinessUI(ulong playerId, bool previousValue, bool newValue)
    {
        if (newValue && !previousValue)
        {
            EventBus.RaiseCoopInteractablePlayerReady(_interactableId, playerId);
        }
        else if (!newValue && previousValue && !IsActivated)
        {
            EventBus.RaiseCoopInteractableReset(_interactableId);
        }
    }
}
