using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Cinemachine;
using UnityEngine;
using MoreMountains.Feedbacks;

public class ErisMinigameManager : NetworkBehaviour
{
    [Header("References")]
    public GameObject TilePrefab;
    public GameObject ChessPiecePrefab;
    public ParticleSystem BlackFogVFX; 
    public Transform NextAreaSpawn; 
    public Transform ControllerStandPos; 
    public Transform ObserverStandPos;   

    [Header("Feel Feedbacks")]
    [SerializeField] private MMF_Player _gameStartFeedback;
    [SerializeField] private MMF_Player _gameSuccessFeedback;
    [SerializeField] private MMF_Player _gameFailureFeedback;
    [SerializeField] private MMF_Player _moveFeedback;

    [Header("Camera Customization")]
    [Tooltip("Transform vị trí Camera dành riêng cho Controller (Host)")]
    public Transform ControllerCameraPos;
    [Tooltip("Transform vị trí Camera dành riêng cho Observer (Client)")]
    public Transform ObserverCameraPos;

    [Space(5)]
    public Vector3 ControllerCameraOffset = new Vector3(4.935221f, 14f, 5f);
    public Vector3 ObserverCameraOffset = new Vector3(4.935221f, 14f, 5f);
    public float ControllerCameraFOV = 60f;
    public float ObserverCameraFOV = 60f;

    // Backward compatibility getters/setters
    [HideInInspector] public Vector3 CameraOffset { get => ControllerCameraOffset; set => ControllerCameraOffset = value; }
    [HideInInspector] public float CameraFOV { get => ControllerCameraFOV; set => ControllerCameraFOV = value; }

    [Header("Audio Configs")]
    public SOAudioClip CorrectMoveSFX;
    public SOAudioClip WrongMoveSFX;
    public SOAudioClip SuccessSFX;
    public SOAudioClip RevealTileSFX;
    public SOAudioClip ControllerWaitingSFX; 
    public SOAudioClip ObserverPathRevealSFX; 
    public SOAudioClip ReadyToPlaySFX;

    [Header("Terrain Alignment Configs")]
    [SerializeField] private LayerMask _groundLayerMask = ~0;
    [SerializeField] private float _raycastHeight = 20f;
    [SerializeField] private float _raycastDistance = 40f;
    [SerializeField] private float _surfaceOffset = 0.02f;
    [SerializeField] private bool _alignToTerrainNormal = true;
    [SerializeField] private float _pieceHeightOffset = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool _allowDebugCheat = true;
    private bool _showDebugPath = false;

    private List<ErisTile> _spawnedTiles = new List<ErisTile>();
    private Vector2Int[] _syncedPath; 
    
    private NetworkVariable<bool> _isGameActive = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> _isMemorizing = new NetworkVariable<bool>(true);
    private NetworkVariable<bool> _hasCompleted = new NetworkVariable<bool>(false); 
    private NetworkVariable<ulong> _controllerId = new NetworkVariable<ulong>();
    private NetworkVariable<ulong> _observerId = new NetworkVariable<ulong>();
    private NetworkVariable<int> _currentStepIndex = new NetworkVariable<int>(0);
    private NetworkVariable<Vector2Int> _pieceGridPos = new NetworkVariable<Vector2Int>(new Vector2Int(-1,-1));
    
    // Xóa NetworkObjectReference vì ta sẽ dùng Object cục bộ để đảm bảo 100% hiển thị
    private GameObject _spawnedPieceInstance;
    private Dictionary<ulong, Vector3> _lockedPositions = new Dictionary<ulong, Vector3>();
    private Dictionary<ulong, Quaternion> _lockedRotations = new Dictionary<ulong, Quaternion>();
    
    private Coroutine _moveCoroutine;
    private Coroutine _pathLoopCoroutine;
    private Coroutine _spawnCoroutine;
    private Coroutine _idleWaveCoroutine; 
    private bool _isReseting = false; 
    private bool _canInput = false;

    private const float TILE_SPACING = 1.3f;

    private readonly KeyCode[] _upKeys = { KeyCode.W, KeyCode.UpArrow };
    private readonly KeyCode[] _downKeys = { KeyCode.S, KeyCode.DownArrow };
    private readonly KeyCode[] _leftKeys = { KeyCode.A, KeyCode.LeftArrow };
    private readonly KeyCode[] _rightKeys = { KeyCode.D, KeyCode.RightArrow };

    private AudioSource _loopingSource;

    public override void OnNetworkSpawn()
    {
        _pieceGridPos.OnValueChanged += OnPiecePosChanged;
        _isMemorizing.OnValueChanged += OnMemorizingChanged;
    }

    public override void OnNetworkDespawn()
    {
        _pieceGridPos.OnValueChanged -= OnPiecePosChanged;
        _isMemorizing.OnValueChanged -= OnMemorizingChanged;
        
        if (_loopingSource != null) { try { AudioManager.Instance.StopSFX(_loopingSource); } catch {} _loopingSource = null; }
    }

    private void OnPiecePosChanged(Vector2Int oldVal, Vector2Int newVal)
    {
        if (newVal.x != -1) {
            if (_spawnedPieceInstance == null) SpawnChessPieceLocal();
            UpdatePieceTargetSafe(newVal); 
        }
    }

    private void OnMemorizingChanged(bool oldVal, bool newVal)
    {
        if (!newVal) {
            StopPathLoop();
            if (_loopingSource != null) { AudioManager.Instance.StopSFX(_loopingSource); _loopingSource = null; }
            PlayBoardSFX(ReadyToPlaySFX, nameof(ReadyToPlaySFX));
            if (_idleWaveCoroutine != null) StopCoroutine(_idleWaveCoroutine);
            _idleWaveCoroutine = StartCoroutine(IdleWaveRoutine());
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || _isGameActive.Value || _hasCompleted.Value) return;
        if (other.CompareTag("Player") && other.TryGetComponent<NetworkObject>(out var netObj)) { StartMinigameServer(netObj.OwnerClientId); }
    }

    private void StartMinigameServer(ulong triggerPlayerId)
    {
        _isGameActive.Value = true; _isMemorizing.Value = true; _controllerId.Value = triggerPlayerId; _hasCompleted.Value = false;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList) { if (client.ClientId != triggerPlayerId) { _observerId.Value = client.ClientId; break; } }
        _syncedPath = GeneratePathArray(); _pieceGridPos.Value = _syncedPath[0]; _currentStepIndex.Value = 0;
        
        SetupBoardClientRpc(_controllerId.Value, _observerId.Value, _syncedPath);
    }

    private Vector2Int[] GeneratePathArray()
    {
        List<Vector2Int> path = new List<Vector2Int>(); bool success = false;
        int outerAttempts = 0;
        while (!success && outerAttempts < 1000) {
            outerAttempts++;
            path.Clear(); Vector2Int current = new Vector2Int(Random.Range(0, 10), 0); path.Add(current);
            while (current.y < 9) {
                List<Vector2Int> moves = new List<Vector2Int>();
                Vector2Int[] neighbors = { current + Vector2Int.up, current + Vector2Int.left, current + Vector2Int.right, current + Vector2Int.down };
                foreach (var m in neighbors) {
                    if (m.x >= 0 && m.x < 10 && m.y >= 0 && m.y < 10 && !path.Contains(m)) {
                        int count = 0;
                        if (path.Contains(m + Vector2Int.up)) count++; if (path.Contains(m + Vector2Int.down)) count++;
                        if (path.Contains(m + Vector2Int.left)) count++; if (path.Contains(m + Vector2Int.right)) count++;
                        
                        // Relax neighbor constraint after many attempts to ensure a path can be found
                        int maxNeighbors = (outerAttempts < 300) ? 1 : 2;
                        if (count <= maxNeighbors) moves.Add(m);
                    }
                }
                if (moves.Count == 0) break;
                List<Vector2Int> sideMoves = moves.FindAll(v => v.y == current.y); float r = Random.value;
                if (sideMoves.Count > 0 && r < 0.6f) current = sideMoves[Random.Range(0, sideMoves.Count)];
                else {
                    List<Vector2Int> upMoves = moves.FindAll(v => v.y > current.y);
                    if (upMoves.Count > 0) current = upMoves[Random.Range(0, upMoves.Count)];
                    else current = moves[Random.Range(0, moves.Count)];
                }
                path.Add(current); 
                
                // Relax length constraint after many attempts to prevent hanging
                bool lengthOk = (outerAttempts < 200) ? (path.Count >= 15 && path.Count <= 25) : (path.Count >= 10);
                if (current.y == 9 && lengthOk) success = true;
            }
        }
        return path.ToArray();
    }

    [ClientRpc]
    private void SetupBoardClientRpc(ulong controllerId, ulong observerId, Vector2Int[] path)
    {
        _syncedPath = path; CleanupBoardImmediate(); 
        try { if (_gameStartFeedback != null) _gameStartFeedback.PlayFeedbacks(); } catch {}
        _spawnCoroutine = StartCoroutine(SpawnTilesWaveDiagonalSafe());
        
        // Spawn ChessPiece cục bộ trên mỗi máy
        SpawnChessPieceLocal();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null) {
            var lp = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (lp != null) {
                if(lp.TryGetComponent<Rigidbody>(out var rb)) { if (!rb.isKinematic) rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }
                if(lp.TryGetComponent<PlayerStateMachine>(out var fsm)) fsm.TransitionTo(PlayerStateType.Idle);
                Transform standPos = (NetworkManager.Singleton.LocalClientId == controllerId) ? ControllerStandPos : ObserverStandPos;
                if (standPos != null) { lp.transform.position = standPos.position; lp.transform.rotation = standPos.rotation; }
                _lockedPositions[NetworkManager.Singleton.LocalClientId] = lp.transform.position; _lockedRotations[NetworkManager.Singleton.LocalClientId] = lp.transform.rotation;
            }
        }
        CameraPreset preset = (NetworkManager.Singleton.LocalClientId == controllerId) ? CameraPreset.TopDownController : CameraPreset.TopDownObserver;
        if (CameraManager.Instance != null) { CameraManager.Instance.SwitchCamera(preset); StartCoroutine(DelayedCameraSync()); }
        if (NetworkManager.Singleton.LocalClientId == controllerId) {
            if (BlackFogVFX != null) BlackFogVFX.Play(); if (_loopingSource != null) AudioManager.Instance.StopSFX(_loopingSource);
            if (ControllerWaitingSFX != null && ControllerWaitingSFX.Clip != null)
                _loopingSource = AudioManager.Instance.PlaySFXLoop(ControllerWaitingSFX);
            else
                Debug.LogWarning("[ErisMinigameManager] 'ControllerWaitingSFX' is unassigned in Inspector!");
        } 
        else if (NetworkManager.Singleton.LocalClientId == observerId) { 
            StartPathLoop(); 
            PlayBoardSFX(ObserverPathRevealSFX, nameof(ObserverPathRevealSFX)); 
        }
        EventBus.RaiseGamePaused(); 
    }

    private IEnumerator DelayedCameraSync() { yield return new WaitForSeconds(0.2f); SyncCameraToManager(); }

    private bool GetTerrainPositionAndRotation(Vector3 flatWorldPos, out Vector3 worldPos, out Quaternion worldRot)
    {
        Vector3 rayOrigin = flatWorldPos + Vector3.up * _raycastHeight;
        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, _raycastDistance, _groundLayerMask, QueryTriggerInteraction.Ignore);
        
        if (hits != null && hits.Length > 0)
        {
            // Sort hits by distance (topmost first)
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;

                // Ignore Player colliders
                if (hit.collider.CompareTag("Player") || 
                    hit.collider.GetComponentInParent<PlayerStateMachine>() != null || 
                    hit.collider.GetComponentInParent<CharacterController>() != null)
                    continue;

                // Ignore ErisTile colliders
                if (hit.collider.GetComponentInParent<ErisTile>() != null)
                    continue;

                // Ignore ErisMinigameManager colliders
                if (hit.collider.GetComponentInParent<ErisMinigameManager>() != null)
                    continue;

                // Ignore ChessPiece instance or any ChessPiece colliders
                if (_spawnedPieceInstance != null && (hit.collider.gameObject == _spawnedPieceInstance || hit.collider.transform.IsChildOf(_spawnedPieceInstance.transform)))
                    continue;

                if (hit.collider.name.Contains("ChessPiece") || hit.collider.CompareTag("ChessPiece"))
                    continue;

                // Ignore stand position colliders if any
                if (ControllerStandPos != null && (hit.collider.transform == ControllerStandPos || hit.collider.transform.IsChildOf(ControllerStandPos)))
                    continue;

                if (ObserverStandPos != null && (hit.collider.transform == ObserverStandPos || hit.collider.transform.IsChildOf(ObserverStandPos)))
                    continue;

                // Valid terrain / ground hit found
                worldPos = hit.point + hit.normal * _surfaceOffset;
                worldRot = _alignToTerrainNormal ? Quaternion.FromToRotation(Vector3.up, hit.normal) * transform.rotation : transform.rotation;
                return true;
            }
        }

        worldPos = flatWorldPos;
        worldRot = transform.rotation;
        return false;
    }

    private Vector3 GetPieceTargetWorldPosition(Vector2Int gridPos, out Quaternion targetRotation)
    {
        ErisTile tile = GetTileAt(gridPos);
        if (tile != null)
        {
            targetRotation = tile.transform.rotation;
            return tile.transform.position + tile.transform.up * _pieceHeightOffset;
        }

        Vector3 flatWorldPos = transform.TransformPoint(new Vector3(gridPos.x * TILE_SPACING, 0, gridPos.y * TILE_SPACING));
        GetTerrainPositionAndRotation(flatWorldPos, out Vector3 terrainPos, out Quaternion terrainRot);
        targetRotation = terrainRot;
        return terrainPos + terrainRot * Vector3.up * _pieceHeightOffset;
    }

    private IEnumerator SpawnTilesWaveDiagonalSafe()
    {
        _spawnedTiles.Clear();
        for (int sum = 0; sum <= 18; sum++) {
            for (int x = 0; x <= sum; x++) {
                int y = sum - x;
                if (x < 10 && y < 10) {
                    Vector3 flatWorldPos = transform.TransformPoint(new Vector3(x * TILE_SPACING, 0, y * TILE_SPACING)); 
                    GetTerrainPositionAndRotation(flatWorldPos, out Vector3 worldPos, out Quaternion worldRot);
                    GameObject tileObj = Instantiate(TilePrefab, worldPos, worldRot, transform);
                    ErisTile tile = tileObj.GetComponent<ErisTile>(); try { tile.Init(new Vector2Int(x, y)); } catch {}
                    _spawnedTiles.Add(tile);
                }
            }
            yield return new WaitForSeconds(0.04f); 
        }
        _canInput = true; 
        
        // Cập nhật vị trí Piece một lần nữa sau khi tiles đã sẵn sàng
        if (_pieceGridPos.Value.x != -1) UpdatePieceTargetSafe(_pieceGridPos.Value);
    }

    private IEnumerator IdleWaveRoutine() {
        float timer = 0;
        while (_isGameActive.Value && !_hasCompleted.Value) {
            yield return new WaitForSeconds(0.2f); // Nhịp độ "mưa rơi"
            if (_isReseting || _isMemorizing.Value) continue;

            // 1. Hiệu ứng Raindrop: Nhúng nhảy ngẫu nhiên 2-3 ô
            for (int i = 0; i < 2; i++) {
                int idx = Random.Range(0, _spawnedTiles.Count);
                if (_spawnedTiles[idx] != null) _spawnedTiles[idx].PlayIdleBounce();
            }

            // 2. Hiệu ứng Center Pulse: Cứ mỗi 5 giây nổ một sóng từ tâm
            timer += 0.2f;
            if (timer >= 5f) {
                timer = 0;
                Vector2Int center = new Vector2Int(5, 5);
                for (int radius = 0; radius <= 8; radius++) {
                    foreach (var t in _spawnedTiles) {
                        if (t != null) {
                            int dist = Mathf.Max(Mathf.Abs(t.GridPos.x - center.x), Mathf.Abs(t.GridPos.y - center.y));
                            if (dist == radius) t.PlayIdleBounce();
                        }
                    }
                    yield return new WaitForSeconds(0.06f);
                }
            }
        }
    }

    private void SyncCameraToManager() {
        var vcams = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
        bool isController = (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == _controllerId.Value);

        Transform targetCamTransform = isController ? ControllerCameraPos : ObserverCameraPos;
        Vector3 targetOffset = isController ? ControllerCameraOffset : ObserverCameraOffset;
        float targetFOV = isController ? ControllerCameraFOV : ObserverCameraFOV;

        foreach (var vcam in vcams) {
            if (vcam.isActiveAndEnabled) {
                vcam.Target.TrackingTarget = null; 
                vcam.Target.LookAtTarget = null;

                if (targetCamTransform != null) {
                    vcam.transform.position = targetCamTransform.position;
                    vcam.transform.rotation = targetCamTransform.rotation;
                } else {
                    vcam.transform.position = transform.TransformPoint(targetOffset); 
                    vcam.transform.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
                }

                var lens = vcam.Lens; 
                lens.FieldOfView = targetFOV; 
                vcam.Lens = lens;
            }
        }
    }

    private void StartPathLoop() { if (_pathLoopCoroutine != null) StopCoroutine(_pathLoopCoroutine); _pathLoopCoroutine = StartCoroutine(PathRevealRoutine()); }
    private void StopPathLoop() {
        if (_pathLoopCoroutine != null) StopCoroutine(_pathLoopCoroutine);
        foreach (var t in _spawnedTiles) t.RestoreColor();
        if (NetworkManager.Singleton.LocalClientId == _controllerId.Value) { if (BlackFogVFX != null) { BlackFogVFX.Stop(); BlackFogVFX.Clear(); } HighlightPossibleMoves(_pieceGridPos.Value); }
        else { ErisTile st = GetTileAt(_syncedPath[0]); if (st != null) st.SetColor(Color.green, true); }
    }

    private IEnumerator PathRevealRoutine() {
        float timeout = 10f; // Chờ tối đa 10 giây cho tiles spawn xong
        while (_spawnedTiles.Count < 100 && timeout > 0) {
            timeout -= Time.deltaTime;
            yield return null;
        }
        
        if (_spawnedTiles.Count < 100) {
            Debug.LogWarning("[ErisMinigameManager] Tiles did not spawn 100 items in time. Starting path reveal anyway.");
        }

        while (true) {
            foreach (var t in _spawnedTiles) t.ResetTile();
            yield return new WaitForSeconds(0.5f); 
            foreach (var step in _syncedPath) {
                ErisTile tile = GetTileAt(step);
                if (tile != null) { tile.SetColor(Color.green); PlayBoardSFX(RevealTileSFX, nameof(RevealTileSFX)); }
                yield return new WaitForSeconds(0.2f); 
            }
            yield return new WaitForSeconds(2.5f); 
        }
    }

    private void SpawnChessPieceLocal() {
        if (_spawnedPieceInstance != null) return;
        
        Vector2Int gridPos = _pieceGridPos.Value.x != -1 ? _pieceGridPos.Value : new Vector2Int(0,0);
        Vector3 worldStart = GetPieceTargetWorldPosition(gridPos, out Quaternion targetRot);
        
        // KHÔNG gán 'transform' làm cha ở đây vì Prefab có thể chứa NetworkObject, gây crash nếu gán làm con của một NetworkObject khác mà không Spawn
        _spawnedPieceInstance = Instantiate(ChessPiecePrefab, worldStart, targetRot); 
        
        Debug.Log($"[ErisMinigameManager] ChessPiece spawned LOCALLY for client {NetworkManager.Singleton.LocalClientId}");
    }

    private void Update() {
        if (!IsSpawned || !_isGameActive.Value) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null) {
            var lp = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (lp != null) {
                if(_lockedPositions.TryGetValue(NetworkManager.Singleton.LocalClientId, out Vector3 lockPos)) lp.transform.position = lockPos;
                if(_lockedRotations.TryGetValue(NetworkManager.Singleton.LocalClientId, out Quaternion lockRot)) lp.transform.rotation = lockRot;
            }
        }
        
        if (NetworkManager.Singleton != null) {
            if (NetworkManager.Singleton.LocalClientId == _observerId.Value && _isMemorizing.Value && Input.GetKeyDown(KeyCode.E)) ReadyToPlayServerRpc(); 
            if (NetworkManager.Singleton.LocalClientId == _controllerId.Value && !_isMemorizing.Value && !_isReseting && _canInput) HandleKeyboardInput(); 
        }
        
        if (_allowDebugCheat && (Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Equals))) { _showDebugPath = !_showDebugPath; ToggleDebugPath(_showDebugPath); }
    }

    private void ToggleDebugPath(bool show) {
        if (show) { foreach (var pos in _syncedPath) { ErisTile t = GetTileAt(pos); if (t != null) t.SetColor(Color.yellow); } }
        else { foreach (var t in _spawnedTiles) t.RestoreColor(); }
    }

    private void HandleKeyboardInput() {
        Vector2Int moveDir = Vector2Int.zero;
        if (AnyKeyPressed(_upKeys)) moveDir = Vector2Int.up; else if (AnyKeyPressed(_downKeys)) moveDir = Vector2Int.down;
        else if (AnyKeyPressed(_leftKeys)) moveDir = Vector2Int.left; else if (AnyKeyPressed(_rightKeys)) moveDir = Vector2Int.right;
        if (moveDir != Vector2Int.zero) {
            Vector2Int targetPos = _pieceGridPos.Value + moveDir;
            if (targetPos.x >= 0 && targetPos.x < 10 && targetPos.y >= 0 && targetPos.y < 10) { _canInput = false; SubmitMoveServerRpc(targetPos); }
        }
    }

    private bool AnyKeyPressed(KeyCode[] keys) { foreach (var k in keys) if (Input.GetKeyDown(k)) return true; return false; }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReadyToPlayServerRpc() => _isMemorizing.Value = false;

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitMoveServerRpc(Vector2Int gridPos) {
        Vector2Int currentPos = _pieceGridPos.Value;
        if (Mathf.Abs(gridPos.x - currentPos.x) + Mathf.Abs(gridPos.y - currentPos.y) == 1 && _currentStepIndex.Value + 1 < _syncedPath.Length && gridPos == _syncedPath[_currentStepIndex.Value + 1]) 
        { _currentStepIndex.Value++; _pieceGridPos.Value = gridPos; } 
        else { WrongMoveEffectClientRpc(gridPos); StartCoroutine(ResetServerDelayed()); }
    }

    private IEnumerator ResetServerDelayed() { yield return new WaitForSeconds(2.0f); _currentStepIndex.Value = 0; _pieceGridPos.Value = new Vector2Int(-1, -1); yield return null; _pieceGridPos.Value = _syncedPath[0]; }

    [ClientRpc]
    private void WrongMoveEffectClientRpc(Vector2Int wrongPos) { try { if (_gameFailureFeedback != null) _gameFailureFeedback.PlayFeedbacks(); } catch {} StartCoroutine(WrongMoveRippleEffect(wrongPos)); }

    private IEnumerator WrongMoveRippleEffect(Vector2Int wrongPos) {
        _isReseting = true; _canInput = false; foreach (var t in _spawnedTiles) t.SetHighlight(false);
        PlayBoardSFX(WrongMoveSFX, nameof(WrongMoveSFX));
        for (int dist = 0; dist < 20; dist++) {
            bool found = false;
            foreach (var tile in _spawnedTiles) { if (Mathf.Abs(tile.GridPos.x - wrongPos.x) + Mathf.Abs(tile.GridPos.y - wrongPos.y) == dist) { tile.ApplyTemporaryRed(); found = true; } }
            if (!found && dist > 10) break; yield return new WaitForSeconds(0.05f);
        }
        yield return new WaitForSeconds(0.6f); foreach (var t in _spawnedTiles) t.RestoreColor();
        if (_showDebugPath) ToggleDebugPath(true); _isReseting = false;
    }

    private void UpdatePieceTargetSafe(Vector2Int gridPos) {
        if (gridPos.x == -1) return;
        
        // Đảm bảo piece đã tồn tại
        if (_spawnedPieceInstance == null) SpawnChessPieceLocal();
        
        Vector3 worldTarget = GetPieceTargetWorldPosition(gridPos, out Quaternion targetRot);
        if (_moveCoroutine != null) StopCoroutine(_moveCoroutine); 
        _moveCoroutine = StartCoroutine(SmoothMovePiece(worldTarget, targetRot, gridPos));
        
        ErisTile t = GetTileAt(gridPos); 
        if (t != null) { try { t.SetColor(Color.green, true); } catch {} }
        if (IsServer && _currentStepIndex.Value == _syncedPath.Length - 1) StartCoroutine(EndGameDelayed());
    }

    private IEnumerator SmoothMovePiece(Vector3 targetPos, Quaternion targetRot, Vector2Int gridPos) {
        if (_spawnedPieceInstance == null) yield break;

        // SNAP ngay lập tức nếu là vị trí khởi đầu
        if (_currentStepIndex.Value == 0 || _isReseting) {
            _spawnedPieceInstance.transform.position = targetPos;
            _spawnedPieceInstance.transform.rotation = targetRot;
        }

        if (!_isReseting && _currentStepIndex.Value > 0 && _moveFeedback != null) { try { _moveFeedback.PlayFeedbacks(_spawnedPieceInstance.transform.position); } catch {} }
        
        float speed = (_isReseting || _currentStepIndex.Value == 0) ? 50f : 12f; 
        while (Vector3.Distance(_spawnedPieceInstance.transform.position, targetPos) > 0.01f) {
            if (_spawnedPieceInstance == null) yield break;
            _spawnedPieceInstance.transform.position = Vector3.MoveTowards(_spawnedPieceInstance.transform.position, targetPos, speed * Time.deltaTime);
            _spawnedPieceInstance.transform.rotation = Quaternion.Lerp(_spawnedPieceInstance.transform.rotation, targetRot, Time.deltaTime * 10f);
            yield return null;
        }
        if (_spawnedPieceInstance != null) {
            _spawnedPieceInstance.transform.position = targetPos;
            _spawnedPieceInstance.transform.rotation = targetRot;
        }
        if (!_isReseting && _currentStepIndex.Value > 0) PlayBoardSFX(CorrectMoveSFX, nameof(CorrectMoveSFX));
        HighlightPossibleMoves(gridPos);
        yield return new WaitForSeconds(0.05f); if (!_isReseting) _canInput = true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        for (int x = 0; x < 10; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                Vector3 flatPos = transform.TransformPoint(new Vector3(x * TILE_SPACING, 0, y * TILE_SPACING));
                GetTerrainPositionAndRotation(flatPos, out Vector3 worldPos, out Quaternion worldRot);
                Gizmos.matrix = Matrix4x4.TRS(worldPos, worldRot, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(TILE_SPACING * 0.9f, 0.1f, TILE_SPACING * 0.9f));
            }
        }
    }
#endif

    private void HighlightPossibleMoves(Vector2Int center) {
        foreach (var t in _spawnedTiles) t.SetHighlight(false);
        if (NetworkManager.Singleton.LocalClientId != _controllerId.Value || _isMemorizing.Value || _isReseting) return;
        Vector2Int[] neighbors = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var dir in neighbors) { ErisTile t = GetTileAt(center + dir); if (t != null) t.SetHighlight(true); }
    }

    private IEnumerator EndGameDelayed() { yield return new WaitForSeconds(1f); SuccessEffectClientRpc(_pieceGridPos.Value); }

    [ClientRpc]
    private void SuccessEffectClientRpc(Vector2Int finalPos) { try { if (_gameSuccessFeedback != null) _gameSuccessFeedback.PlayFeedbacks(); } catch {} StartCoroutine(SuccessSequence(finalPos)); }

    private IEnumerator SuccessSequence(Vector2Int finalPos) {
        _canInput = false; PlayBoardSFX(SuccessSFX, nameof(SuccessSFX));
        for (int dist = 0; dist < 20; dist++) {
            bool found = false;
            foreach (var tile in _spawnedTiles) { if (Mathf.Abs(tile.GridPos.x - finalPos.x) + Mathf.Abs(tile.GridPos.y - finalPos.y) == dist) { tile.SetColor(Color.cyan, true); found = true; } }
            if (!found && dist > 10) break; yield return new WaitForSeconds(0.04f);
        }
        yield return new WaitForSeconds(1.5f);
        for (int sum = 0; sum <= 18; sum++) {
            for (int x = 0; x <= sum; x++) {
                int y = sum - x; ErisTile t = GetTileAt(new Vector2Int(x, y));
                if (t != null) { try { t.PlayDespawnEffect(); } catch {} }
            }
            yield return new WaitForSeconds(0.04f); 
        }
        yield return new WaitForSeconds(1f); FinalizeMinigameClientRpc(); 
    }

    [ClientRpc]
    private void FinalizeMinigameClientRpc() {
        var lp = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (lp != null) {
            if (lp.TryGetComponent<Rigidbody>(out var rb)) { rb.isKinematic = false; }
            if (NextAreaSpawn != null) lp.transform.position = NextAreaSpawn.position;
            var vcams = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
            foreach (var vcam in vcams) { vcam.Target.TrackingTarget = lp.transform; vcam.Target.LookAtTarget = lp.transform; }
        }
        CleanupBoardImmediate(); _lockedPositions.Clear(); _lockedRotations.Clear();
        if (BlackFogVFX != null) { BlackFogVFX.Stop(); BlackFogVFX.Clear(); }
        if (CameraManager.Instance != null) CameraManager.Instance.SwitchCamera(CameraPreset.ThirdPerson); 
        EventBus.RaiseGameResumed(); 
        if (IsServer) { _isGameActive.Value = false; _hasCompleted.Value = true; }
    }

    private ErisTile GetTileAt(Vector2Int pos) => _spawnedTiles.Find(t => t.GridPos == pos);

    private void PlayBoardSFX(SOAudioClip clip, string clipName = "")
    {
        if (clip == null || clip.Clip == null)
        {
            if (!string.IsNullOrEmpty(clipName))
            {
                Debug.LogWarning($"[ErisMinigameManager] Audio clip '{clipName}' is missing or unassigned in Inspector on {gameObject.name}!");
            }
            return;
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(clip);
        }
    }

    private void CleanupBoardImmediate() { 
        if (_spawnCoroutine != null) StopCoroutine(_spawnCoroutine); if (_pathLoopCoroutine != null) StopCoroutine(_pathLoopCoroutine);
        if (_idleWaveCoroutine != null) StopCoroutine(_idleWaveCoroutine);
        foreach (var t in _spawnedTiles) if (t != null) Destroy(t.gameObject); _spawnedTiles.Clear(); 
        if (_spawnedPieceInstance != null) Destroy(_spawnedPieceInstance); _spawnedPieceInstance = null;
        ErisTile[] existingTiles = GetComponentsInChildren<ErisTile>(); foreach(var et in existingTiles) Destroy(et.gameObject);
    }
}
