using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using MoreMountains.Feedbacks;

public enum ErisIllusionState
{
    Ready,
    Revealing,
    BoardActive,
    Completed
}

public enum ErisSessionPhase : byte
{
    Idle,
    RoleSelection,
    Countdown,
    Playing,
    Completed
}

public enum ErisRole : byte
{
    None,
    Observer,
    Controller
}

public class ErisMinigameManager : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("KÃ©o Empty GameObject lÃ m gá»‘c spawn bÃ n cá» vÃ o Ä‘Ã¢y. Äáº·t vá»‹ trÃ­ táº¡i Ã´ Ä‘áº§u tiÃªn (0,0), xoay Ä‘á»ƒ bÃ n cá» náº±m Ä‘Ãºng theo Ä‘á»‹a hÃ¬nh. Náº¿u Ä‘á»ƒ trá»‘ng, code sáº½ dÃ¹ng vá»‹ trÃ­ cá»§a ErisMinigame_Networker.")]
    public Transform BoardAnchor;
    [Tooltip("KÃ©o prefab Assets/_Game/Prefabs/MiniGame/Chess/ErisTile.prefab vÃ o Ä‘Ã¢y. ÄÃ¢y lÃ  prefab cá»§a tá»«ng Ã´ bÃ n cá».")]
    public GameObject TilePrefab;
    [Tooltip("KÃ©o prefab Assets/_Game/Prefabs/MiniGame/Chess/ChessPiece.prefab vÃ o Ä‘Ã¢y. ÄÃ¢y lÃ  quÃ¢n cá» Ä‘Æ°á»£c di chuyá»ƒn.")]
    public GameObject ChessPiecePrefab;
    [Tooltip("KÃ©o Particle System hiá»‡u á»©ng sÆ°Æ¡ng Ä‘en/áº£o giÃ¡c vÃ o Ä‘Ã¢y. CÃ³ thá»ƒ Ä‘á»ƒ trá»‘ng náº¿u chÆ°a cÃ³ VFX.")]
    public ParticleSystem BlackFogVFX; 
    [Tooltip("KÃ©o Transform TP hoáº·c Ä‘iá»ƒm spawn sau bÃ n cá» vÃ o Ä‘Ã¢y. Player sáº½ Ä‘Æ°á»£c chuyá»ƒn tá»›i Ä‘Ã¢y sau khi hoÃ n thÃ nh.")]
    public Transform NextAreaSpawn; 
    [Tooltip("KÃ©o Transform CT vÃ o Ä‘Ã¢y. ÄÃ¢y lÃ  vá»‹ trÃ­ Ä‘á»©ng cá»§a Player Ä‘iá»u khiá»ƒn bÃ n cá».")]
    public Transform ControllerStandPos; 
    [Tooltip("KÃ©o Transform OB vÃ o Ä‘Ã¢y. ÄÃ¢y lÃ  vá»‹ trÃ­ Ä‘á»©ng cá»§a Player quan sÃ¡t Ä‘Æ°á»ng Ä‘i.")]
    public Transform ObserverStandPos;   

    [Header("KÃ­ch thÆ°á»›c bÃ n cá»")]
    [Tooltip("Tá»· lá»‡ kÃ­ch thÆ°á»›c cá»§a má»—i Ã´ vÃ  toÃ n bá»™ lÆ°á»›i. 0.5 = Ã´ nhá» báº±ng má»™t ná»­a hiá»‡n táº¡i. KhÃ´ng cáº§n scale BoardAnchor khi dÃ¹ng field nÃ y.")]
    [SerializeField, Range(0.1f, 2f)] private float _boardScale = 0.5f;
    [Tooltip("Khoáº£ng cÃ¡ch gá»‘c giá»¯a tÃ¢m cÃ¡c Ã´. Vá»›i Ã´ gá»‘c rá»™ng 1 Ä‘Æ¡n vá»‹, giÃ¡ trá»‹ máº·c Ä‘á»‹nh 1.3 táº¡o khe 0.3.")]
    [SerializeField, Min(0.1f)] private float _baseTileSpacing = 1.3f;
    [Tooltip("Khe há»Ÿ cá»™ng thÃªm giá»¯a cÃ¡c Ã´ sau khi thu nhá». TÄƒng lÃªn náº¿u cÃ¡c Ã´ bá»‹ dÃ­nh.")]
    [SerializeField, Min(0f)] private float _minimumTileGap = 0.1f;
    [Tooltip("Báº­t Ä‘á»ƒ camera tá»± zoom gáº§n hÆ¡n khi bÃ n cá» Ä‘Æ°á»£c thu nhá». Táº¯t náº¿u camera bá»‹ quÃ¡ gáº§n hoáº·c dÃ­nh tráº§n.")]
    [SerializeField] private bool _scaleCameraWithBoard = false;

    [Header("Hiá»‡u á»©ng háº¡ Ã´ theo máº·t Ä‘áº¥t")]
    [SerializeField, Min(0f)] private float _tileDropHeight = 0.45f;
    [SerializeField, Min(0.01f)] private float _tileDropDuration = 0.7f;
    [SerializeField, Min(0f)] private float _tileWaveDelay = 0.035f;
    [SerializeField, Min(0f)] private float _groundProbeHeight = 8f;
    [SerializeField, Min(0.1f)] private float _groundProbeDistance = 24f;
    [SerializeField, Min(0f)] private float _surfaceOffset = 0.06f;
    [SerializeField] private LayerMask _groundLayers = ~0;
    [SerializeField] private bool _alignTilesToGround = true;
    [Tooltip("KhÃ³a toÃ n bá»™ Ã´ trÃªn máº·t pháº³ng BoardAnchor. DÃ¹ng cho Map2 Ä‘á»ƒ raycast khÃ´ng báº¯t nháº§m mÃ¡i/Ä‘á»‹a hÃ¬nh phÃ­a trÃªn bÃ n.")]
    [SerializeField] private bool _lockTilesToBoardPlane = false;

    [Header("Thiáº¿t láº­p chuá»—i áº£o giÃ¡c Ä‘á»‹a hÃ¬nh")]
    [Tooltip("KÃ©o BoxCollider cá»§a child BoardActivationTrigger vÃ o Ä‘Ã¢y. KhÃ´ng kÃ©o GameObject cha vÃ  khÃ´ng thÃªm NetworkObject cho Collider 2.")]
    [SerializeField] private Collider _boardActivationCollider;
    [Tooltip("KÃ©o cÃ¡c GameObject chá»©a pháº§n Ä‘áº¥t cáº§n biáº¿n máº¥t vÃ o Ä‘Ã¢y. CÃ³ thá»ƒ kÃ©o nhiá»u object. KhÃ´ng kÃ©o ErisMinigame_Networker, Player, CT, OB hoáº·c TP.")]
    [SerializeField] private GameObject[] _illusionGroundObjects = new GameObject[0];
    [Tooltip("Auto-detect small renderers directly below the board when no ground objects are assigned in the Inspector.")]
    [SerializeField] private bool _autoDetectIllusionGround = true;
    [SerializeField, Min(1f)] private float _illusionGroundSearchRadius = 8f;
    [Tooltip("Thá»i gian chá» sau khi Ä‘áº¥t biáº¿n máº¥t trÆ°á»›c khi bÃ n cá» xuáº¥t hiá»‡n. ÄÆ¡n vá»‹: giÃ¢y.")]
    [SerializeField, Min(0f)] private float _illusionRevealDelay = 0.8f;
    [Tooltip("Báº­t: bÃ n cá» tá»± xuáº¥t hiá»‡n sau khi Collider 1 lÃ m Ä‘áº¥t biáº¿n máº¥t. Táº¯t: Player pháº£i Ä‘i vÃ o Collider 2 Ä‘á»ƒ báº¯t Ä‘áº§u bÃ n cá».")]
    [SerializeField] private bool _autoStartBoardAfterReveal = false;
    [Tooltip("Báº­t Ä‘á»ƒ khÃ³a input vÃ  chuyá»ƒn tÃ­n hiá»‡u camera sang cháº¿ Ä‘á»™ cutscene trong lÃºc áº£o giÃ¡c diá»…n ra.")]
    [SerializeField] private bool _playCutsceneSignals = true;

    [Header("Hiá»‡u á»©ng Feel")]
    [Tooltip("Feedback khi bÃ n cá» báº¯t Ä‘áº§u xuáº¥t hiá»‡n.")]
    [SerializeField] private MMF_Player _gameStartFeedback;
    [Tooltip("Feedback khi Player hoÃ n thÃ nh bÃ n cá».")]
    [SerializeField] private MMF_Player _gameSuccessFeedback;
    [Tooltip("Feedback khi Player Ä‘i sai Ä‘Æ°á»ng.")]
    [SerializeField] private MMF_Player _gameFailureFeedback;
    [Tooltip("Feedback khi quÃ¢n cá» di chuyá»ƒn Ä‘Ãºng.")]
    [SerializeField] private MMF_Player _moveFeedback;

    [Header("TÃ¹y chá»‰nh camera")]
    [Tooltip("Offset camera TopDown tÃ­nh theo local rotation cá»§a ErisMinigame_Networker. CÃ³ thá»ƒ chá»‰nh Ä‘á»ƒ phÃ¹ há»£p Ä‘á»‹a hÃ¬nh nghiÃªng/xÃ©o.")]
    public Vector3 CameraOffset = new Vector3(4.935221f, 14f, 5f);
    [Tooltip("Field of View cá»§a camera bÃ n cá».")]
    public float CameraFOV = 60f;
    [Tooltip("Báº­t Ä‘á»ƒ camera tá»± Ä‘áº·t vÃ o tÃ¢m bÃ n cá» thay vÃ¬ lá»‡ch theo offset cÅ©.")]
    [SerializeField] private bool _centerCameraOnBoard = true;
    [Tooltip("Báº­t cho Map2 khi BoardAnchor Ä‘Æ°á»£c khÃ³a táº¡i tÃ¢m 4 Ã´ 45, 46, 55, 56. Táº¯t Ä‘á»ƒ giá»¯ cÃ¡ch Ä‘áº·t anchor táº¡i Ã´ Ä‘áº§u tiÃªn cá»§a cÃ¡c scene cÅ©.")]
    [SerializeField] private bool _boardAnchorIsCenter = false;
    [Tooltip("Äá»™ cao tá»‘i thiá»ƒu cá»§a camera so vá»›i bÃ n cá». TÄƒng lÃªn náº¿u camera bá»‹ dÃ­nh tráº§n hoáº·c khÃ´ng tháº¥y toÃ n bá»™ bÃ n.")]
    [SerializeField, Min(1f)] private float _minimumCameraHeight = 12f;
    [Tooltip("Ba camera cá»‘ Ä‘á»‹nh cá»§a Map2. MÅ©i tÃªn trÃ¡i/pháº£i chuyá»ƒn gÃ³c; mÅ©i tÃªn lÃªn chá»n gÃ³c TrÃªn.")]
    [SerializeField] private Transform _boardCameraTopMarker;
    [SerializeField] private Transform _boardCameraLeftMarker;
    [SerializeField] private Transform _boardCameraRightMarker;
    [Tooltip("GÃ³c camera khi vá»«a vÃ o bÃ n: 0 = TrÃ¡i, 1 = TrÃªn, 2 = Pháº£i.")]
    [SerializeField, Range(0, 2)] private int _initialCameraDirection = 0;
    [Header("Tá»a Ä‘á»™ chá»‰nh trá»±c tiáº¿p trong Inspector")]
    [Tooltip("Báº­t Ä‘á»ƒ dÃ¹ng cÃ¡c tá»a Ä‘á»™ bÃªn dÆ°á»›i thay cho Transform Ä‘áº·t sáºµn trong scene.")]
    [SerializeField] private bool _useInspectorCoordinates = false;
    [Tooltip("Báº­t Ä‘á»ƒ Ä‘á»“ng bá»™ hai chiá»u ngay trong Play Mode: kÃ©o Transform sáº½ cáº­p nháº­t sá»‘ tá»a Ä‘á»™, sá»­a sá»‘ sáº½ di chuyá»ƒn Transform.")]
    [SerializeField] private bool _liveInspectorCoordinateEditing = true;
    [Tooltip("Vá»‹ trÃ­ tháº¿ giá»›i cá»§a tÃ¢m bÃ n cá». Rotation bÃ n cá» luÃ´n Ä‘Æ°á»£c khÃ³a pháº³ng theo Euler bÃªn dÆ°á»›i.")]
    [SerializeField] private Vector3 _boardWorldPosition = new Vector3(-1273.6f, 106.12f, 391f);
    [SerializeField] private Vector3 _boardEulerAngles = new Vector3(0f, 3.75f, 0f);
    [Tooltip("Vá»‹ trÃ­/gÃ³c nhÃ¬n tháº¿ giá»›i cá»§a camera TrÃªn.")]
    [SerializeField] private Vector3 _topCameraWorldPosition = new Vector3(-1274.37f, 113f, 390.3f);
    [SerializeField] private Vector3 _topCameraEulerAngles = new Vector3(89.141f, 4.125f, -0.028f);
    [Tooltip("Vá»‹ trÃ­/gÃ³c nhÃ¬n tháº¿ giá»›i cá»§a camera TrÃ¡i.")]
    [SerializeField] private Vector3 _leftCameraWorldPosition = new Vector3(-1268.8f, 107.9f, 385.85f);
    [SerializeField] private Vector3 _leftCameraEulerAngles = new Vector3(12f, -57.4f, 0f);
    [Tooltip("Vá»‹ trÃ­/gÃ³c nhÃ¬n tháº¿ giá»›i cá»§a camera Pháº£i.")]
    [SerializeField] private Vector3 _rightCameraWorldPosition = new Vector3(-1278.7f, 111.4f, 399f);
    [SerializeField] private Vector3 _rightCameraEulerAngles = new Vector3(25.84f, 143.5f, -1f);

    [Header("Ã‚m thanh bÃ n cá»")]
    [Tooltip("SOAudioClip khi Ä‘i Ä‘Ãºng má»™t bÆ°á»›c.")]
    public SOAudioClip CorrectMoveSFX;
    [Tooltip("SOAudioClip khi Ä‘i sai.")]
    public SOAudioClip WrongMoveSFX;
    [Tooltip("SOAudioClip khi hoÃ n thÃ nh bÃ n cá».")]
    public SOAudioClip SuccessSFX;
    [Tooltip("SOAudioClip khi Ä‘Æ°á»ng Ä‘i Ä‘Æ°á»£c hiá»ƒn thá»‹ cho Observer.")]
    public SOAudioClip RevealTileSFX;
    [Tooltip("SOAudioClip loop trong lÃºc Controller Ä‘ang chá» Observer xÃ¡c nháº­n.")]
    public SOAudioClip ControllerWaitingSFX; 
    [Tooltip("SOAudioClip khi Observer nhÃ¬n tháº¥y Ä‘Æ°á»ng Ä‘i.")]
    public SOAudioClip ObserverPathRevealSFX; 
    [Tooltip("SOAudioClip khi Observer sáºµn sÃ ng báº¯t Ä‘áº§u.")]
    public SOAudioClip ReadyToPlaySFX;

    [Header("Debug")]
    [Tooltip("Chá»‰ báº­t khi test. Cho phÃ©p nháº¥n phÃ­m + Ä‘á»ƒ hiá»‡n Ä‘Æ°á»ng Ä‘i Ä‘Ãºng.")]
    [SerializeField] private bool _allowDebugCheat = false;
    private bool _showDebugPath = false;

    private List<ErisTile> _spawnedTiles = new List<ErisTile>();
    private Vector2Int[] _syncedPath; 
    private Vector2Int[] _lastSessionPath = System.Array.Empty<Vector2Int>();
    
    private NetworkVariable<bool> _isGameActive = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> _isMemorizing = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> _hasCompleted = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ulong> _controllerId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ulong> _observerId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> _currentStepIndex = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector2Int> _pieceGridPos = new NetworkVariable<Vector2Int>(
        new Vector2Int(-1, -1), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ErisIllusionState> _illusionState =
        new NetworkVariable<ErisIllusionState>(
            ErisIllusionState.Ready, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ErisSessionPhase> _sessionPhase =
        new(ErisSessionPhase.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> _roleControllerId =
        new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> _roleObserverId =
        new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _countdownValue =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> _startClientId =
        new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private ulong _roleSelectionTick = ulong.MaxValue;
    private ulong _roleSelectionFirstClient = ulong.MaxValue;
    private ErisRole _roleSelectionFirstRole = ErisRole.None;
    private Coroutine _countdownRoutine;
    private ErisBoardUI _boardUI;
    
    // XÃ³a NetworkObjectReference vÃ¬ ta sáº½ dÃ¹ng Object cá»¥c bá»™ Ä‘á»ƒ Ä‘áº£m báº£o 100% hiá»ƒn thá»‹
    private GameObject _spawnedPieceInstance;
    private Dictionary<ulong, Vector3> _lockedPositions = new Dictionary<ulong, Vector3>();
    private Dictionary<ulong, Quaternion> _lockedRotations = new Dictionary<ulong, Quaternion>();
    
    private Coroutine _moveCoroutine;
    private Coroutine _pathLoopCoroutine;
    private Coroutine _spawnCoroutine;
    private Coroutine _idleWaveCoroutine; 
    private Coroutine _illusionRoutine;
    private Coroutine _completionRoutine;
    private CinemachineCamera _runtimeErisCamera;
    private bool _ownsRuntimeErisCamera;
    private bool _boardCameraLeaseActive;
    private bool _playerCameraSetupApplied;
    private string _lastLoggedErisCameraName;
    private bool _deathExitInProgress;
    private readonly Dictionary<GameObject, bool> _illusionGroundStates = new Dictionary<GameObject, bool>();
    private readonly Dictionary<GameObject, Material> _illusionGroundMaterials = new Dictionary<GameObject, Material>();
    private readonly Dictionary<GameObject, Bounds> _illusionGroundBounds = new Dictionary<GameObject, Bounds>();
    private bool _isReseting = false; 
    private bool _canInput = false;
    private bool _completionStarted;

    // Must cover the cyan wave, dissolve, and a short settle before the
    // ground/camera are restored together.
    private const float SuccessPresentationDuration = 3.2f;

    private bool _liveCoordinatesInitialized;
    private Vector3 _lastBoardPosition;
    private Vector3 _lastBoardEulerAngles;
    private Vector3 _lastTopCameraPosition;
    private Vector3 _lastTopCameraEulerAngles;
    private Vector3 _lastLeftCameraPosition;
    private Vector3 _lastLeftCameraEulerAngles;
    private Vector3 _lastRightCameraPosition;
    private Vector3 _lastRightCameraEulerAngles;

    // Arrow keys are reserved for camera switching. Board movement stays on WASD
    // so camera input cannot also move the chess piece.
    private readonly KeyCode[] _upKeys = { KeyCode.W };
    private readonly KeyCode[] _downKeys = { KeyCode.S };
    private readonly KeyCode[] _leftKeys = { KeyCode.A };
    private readonly KeyCode[] _rightKeys = { KeyCode.D };

    private AudioSource _loopingSource;
    // The initial board view is the top camera; arrow keys switch views.
    private int _cameraDirection = 1;

    public ErisSessionPhase SessionPhase => _sessionPhase.Value;
    public int CountdownValue => _countdownValue.Value;
    /// <summary>Returns true when this board is running without an observer.</summary>
    public bool IsSoloSession => _observerId.Value == ulong.MaxValue;
    /// <summary>Returns true while a controller may start without waiting for an observer.</summary>
    public bool IsSoloSelection => _sessionPhase.Value == ErisSessionPhase.RoleSelection
        && _roleControllerId.Value != ulong.MaxValue
        && _roleObserverId.Value == ulong.MaxValue;
    public bool CanStartSession => NetworkManager.Singleton != null
        && CanStartSessionFor(NetworkManager.Singleton.LocalClientId);

    /// <summary>
    /// Starts the Eris role lobby for a direct MainMap2 test without requiring
    /// the illusion trigger or the Lobby scene. Only the server may invoke it.
    /// </summary>
    public void StartDirectTestSessionServer()
    {
        if (!Application.isEditor
            || SceneManager.GetActiveScene().name != Constants.Scenes.LEVEL_02
            || !IsServer
            || _sessionPhase.Value != ErisSessionPhase.Idle
            || _isGameActive.Value)
        {
            return;
        }

        _illusionState.Value = ErisIllusionState.Revealing;
        ApplyIllusionPresentationClientRpc(false);
        BeginRoleSelectionServer();
    }
    public ErisRole LocalRole
    {
        get
        {
            if (NetworkManager.Singleton == null) return ErisRole.None;
            if (NetworkManager.Singleton.LocalClientId == _roleControllerId.Value) return ErisRole.Controller;
            if (NetworkManager.Singleton.LocalClientId == _roleObserverId.Value) return ErisRole.Observer;
            return ErisRole.None;
        }
    }
    public string RoleStatusMessage
    {
        get
        {
            if (LocalRole == ErisRole.Controller && IsSoloSelection)
                return "\u0042\u1ea1n \u0111\u00e3 kh\u00f3a vai tr\u00f2: NG\u01af\u1edcI \u0110I\u1ec0U KHI\u1ec2N \u00b7 C\u00f3 th\u1ec3 b\u1eaft \u0111\u1ea7u solo ho\u1eb7c ch\u1edd Ng\u01b0\u1eddi Quan S\u00e1t";
            if (LocalRole != ErisRole.None) return $"\u0042\u1ea1n \u0111\u00e3 kh\u00f3a vai tr\u00f2: {RoleDisplayName(LocalRole)}";
            if (_roleControllerId.Value != ulong.MaxValue || _roleObserverId.Value != ulong.MaxValue)
                return "\u004d\u1ed9t vai tr\u00f2 \u0111\u00e3 b\u1ecb kh\u00f3a \u00b7 H\u00e3y ch\u1ecdn vai tr\u00f2 c\u00f2n l\u1ea1i";
            return "\u0043h\u1ecdn vai tr\u00f2 c\u1ee7a b\u1ea1n";
        }
    }

    public override void OnNetworkSpawn()
    {
        _boardCameraLeaseActive = false;
        ReleaseBoardCameraLease();
        _cameraDirection = Mathf.Clamp(_initialCameraDirection, 0, 2);
        ApplyInspectorCoordinates();
        CacheLiveInspectorCoordinates();
        _pieceGridPos.OnValueChanged += OnPiecePosChanged;
        _isMemorizing.OnValueChanged += OnMemorizingChanged;
        _sessionPhase.OnValueChanged += OnSessionPhaseChanged;
        EventBus.OnPlayerDied += HandlePlayerDied;
        _isGameActive.OnValueChanged += OnGameActiveChanged;
        _illusionState.OnValueChanged += OnIllusionStateChanged;
        if (_boardUI == null) _boardUI = gameObject.AddComponent<ErisBoardUI>();
        _boardUI.Initialize(this);
        SetBoardActivationColliderEnabled(false);
    }

    private void SyncLiveInspectorCoordinates()
    {
        if (!Application.isPlaying || !_useInspectorCoordinates || !_liveInspectorCoordinateEditing) return;
        if (!_liveCoordinatesInitialized)
        {
            CacheLiveInspectorCoordinates();
            return;
        }

        SyncLivePose(BoardAnchor, ref _boardWorldPosition, ref _boardEulerAngles,
            ref _lastBoardPosition, ref _lastBoardEulerAngles);
        SyncLivePose(_boardCameraTopMarker, ref _topCameraWorldPosition, ref _topCameraEulerAngles,
            ref _lastTopCameraPosition, ref _lastTopCameraEulerAngles);
        SyncLivePose(_boardCameraLeftMarker, ref _leftCameraWorldPosition, ref _leftCameraEulerAngles,
            ref _lastLeftCameraPosition, ref _lastLeftCameraEulerAngles);
        SyncLivePose(_boardCameraRightMarker, ref _rightCameraWorldPosition, ref _rightCameraEulerAngles,
            ref _lastRightCameraPosition, ref _lastRightCameraEulerAngles);
    }

    private void CacheLiveInspectorCoordinates()
    {
        if (BoardAnchor != null)
        {
            _lastBoardPosition = BoardAnchor.position;
            _lastBoardEulerAngles = BoardAnchor.eulerAngles;
        }
        CacheCameraPose(_boardCameraTopMarker, ref _lastTopCameraPosition, ref _lastTopCameraEulerAngles);
        CacheCameraPose(_boardCameraLeftMarker, ref _lastLeftCameraPosition, ref _lastLeftCameraEulerAngles);
        CacheCameraPose(_boardCameraRightMarker, ref _lastRightCameraPosition, ref _lastRightCameraEulerAngles);
        _liveCoordinatesInitialized = true;
    }

    private static void CacheCameraPose(Transform marker, ref Vector3 position, ref Vector3 eulerAngles)
    {
        if (marker == null) return;
        position = marker.position;
        eulerAngles = marker.eulerAngles;
    }

    private static void SyncLivePose(Transform target, ref Vector3 inspectorPosition, ref Vector3 inspectorEulerAngles,
        ref Vector3 lastPosition, ref Vector3 lastEulerAngles)
    {
        if (target == null) return;

        Vector3 actualPosition = target.position;
        Vector3 actualEulerAngles = target.eulerAngles;
        bool transformChanged = (actualPosition - lastPosition).sqrMagnitude > 0.000001f
            || Quaternion.Angle(Quaternion.Euler(actualEulerAngles), Quaternion.Euler(lastEulerAngles)) > 0.01f;
        bool inspectorChanged = (inspectorPosition - lastPosition).sqrMagnitude > 0.000001f
            || Quaternion.Angle(Quaternion.Euler(inspectorEulerAngles), Quaternion.Euler(lastEulerAngles)) > 0.01f;

        if (transformChanged && !inspectorChanged)
        {
            inspectorPosition = actualPosition;
            inspectorEulerAngles = actualEulerAngles;
        }
        else if (inspectorChanged && !transformChanged)
        {
            target.position = inspectorPosition;
            target.rotation = Quaternion.Euler(inspectorEulerAngles);
            actualPosition = target.position;
            actualEulerAngles = target.eulerAngles;
        }

        lastPosition = actualPosition;
        lastEulerAngles = actualEulerAngles;
    }

    private void ApplyInspectorCoordinates()
    {
        if (!_useInspectorCoordinates) return;

        if (BoardAnchor != null)
        {
            BoardAnchor.position = _boardWorldPosition;
            BoardAnchor.rotation = Quaternion.Euler(_boardEulerAngles);
        }

        ApplyCameraPose(_boardCameraTopMarker, _topCameraWorldPosition, _topCameraEulerAngles);
        ApplyCameraPose(_boardCameraLeftMarker, _leftCameraWorldPosition, _leftCameraEulerAngles);
        ApplyCameraPose(_boardCameraRightMarker, _rightCameraWorldPosition, _rightCameraEulerAngles);
    }

    private static void ApplyCameraPose(Transform marker, Vector3 worldPosition, Vector3 eulerAngles)
    {
        if (marker == null) return;
        marker.position = worldPosition;
        marker.rotation = Quaternion.Euler(eulerAngles);
    }

    public override void OnNetworkDespawn()
    {
        if (_illusionGroundStates.Count > 0)
            ApplyIllusionPresentationLocal(true);
        ReleaseBoardCameraLease();
        DestroyRuntimeErisCamera();
        _pieceGridPos.OnValueChanged -= OnPiecePosChanged;
        _isMemorizing.OnValueChanged -= OnMemorizingChanged;
        _sessionPhase.OnValueChanged -= OnSessionPhaseChanged;
        EventBus.OnPlayerDied -= HandlePlayerDied;
        _isGameActive.OnValueChanged -= OnGameActiveChanged;
        _illusionState.OnValueChanged -= OnIllusionStateChanged;
        
        if (_loopingSource != null) { try { AudioManager.Instance.StopSFX(_loopingSource); } catch {} _loopingSource = null; }
        if (_illusionRoutine != null) StopCoroutine(_illusionRoutine);
        if (_countdownRoutine != null) StopCoroutine(_countdownRoutine);
        if (_completionRoutine != null) StopCoroutine(_completionRoutine);
        if (_deathExitInProgress) _deathExitInProgress = false;
        _completionRoutine = null;
        _completionStarted = false;
    }

    private void HandlePlayerDied(ulong clientId)
    {
        // A death outside Eris must not interrupt an unrelated gameplay session.
        // During role selection/countdown no role may be assigned yet, so the
        // active session phase is also considered a valid participant window.
        bool sessionRunning = _sessionPhase.Value != ErisSessionPhase.Idle
            && _sessionPhase.Value != ErisSessionPhase.Completed;
        bool isSessionPlayer = clientId == _controllerId.Value
            || clientId == _observerId.Value
            || clientId == _roleControllerId.Value
            || clientId == _roleObserverId.Value;
        if (!sessionRunning || (!isSessionPlayer && _sessionPhase.Value == ErisSessionPhase.Playing)) return;

        if (IsServer)
        {
            ResetBoardAfterDeathServer(clientId);
        }
        else
        {
            // The authoritative server sends the same RPC. Keeping this local
            // path makes the board leave immediately on remote clients while
            // the death/respawn messages are still in flight.
            ExitBoardAfterDeathLocal();
        }
    }

    private void ResetBoardAfterDeathServer(ulong deceasedClientId)
    {
        if (!IsServer || _deathExitInProgress) return;
        _deathExitInProgress = true;

        // Leave the Eris arena immediately and revive at the latest checkpoint
        // so a second attempt never starts while an owner is still falling or
        // waiting for the normal world respawn delay.
        RespawnManager.Instance?.RequestImmediateRespawn(deceasedClientId);

        StopBoardCoroutines();
        if (_syncedPath != null && _syncedPath.Length > 0)
            _lastSessionPath = (Vector2Int[])_syncedPath.Clone();
        _syncedPath = System.Array.Empty<Vector2Int>();
        _isGameActive.Value = false;
        _sessionPhase.Value = ErisSessionPhase.Idle;
        _isMemorizing.Value = false;
        _hasCompleted.Value = false;
        _currentStepIndex.Value = 0;
        _pieceGridPos.Value = new Vector2Int(-1, -1);
        _controllerId.Value = ulong.MaxValue;
        _observerId.Value = ulong.MaxValue;
        _roleControllerId.Value = ulong.MaxValue;
        _roleObserverId.Value = ulong.MaxValue;
        _startClientId.Value = ulong.MaxValue;
        _countdownValue.Value = 0;
        _roleSelectionTick = ulong.MaxValue;
        _roleSelectionFirstClient = ulong.MaxValue;
        _roleSelectionFirstRole = ErisRole.None;
        _illusionState.Value = ErisIllusionState.Ready;
        _completionStarted = false;
        if (_completionRoutine != null)
        {
            StopCoroutine(_completionRoutine);
            _completionRoutine = null;
        }

        ExitBoardAfterDeathClientRpc();
        _deathExitInProgress = false;
    }

    private void OnSessionPhaseChanged(ErisSessionPhase oldPhase, ErisSessionPhase newPhase)
    {
        if (newPhase == ErisSessionPhase.Idle || newPhase == ErisSessionPhase.Completed)
        {
            ReleaseBoardCameraLease();
            // The phase NetworkVariable can change after the completion RPC has
            // already restored the camera. Resolve the owned Player again here
            // so the phase callback cannot leave the camera in TopDown mode.
            RestoreOwnedPlayerCamera(GetLocalPlayerTransform());
            return;
        }

        // Role selection/countdown must stay on the owned Player camera. Other
        // gameplay cameras (focus, quest, timeline) are not allowed to steal it.
        if ((newPhase == ErisSessionPhase.RoleSelection || newPhase == ErisSessionPhase.Countdown)
            && !_boardCameraLeaseActive)
        {
            _playerCameraSetupApplied = false;
            EnsurePlayerCameraDuringSetup();
        }
        else if (newPhase == ErisSessionPhase.Idle || newPhase == ErisSessionPhase.Completed)
        {
            ReleaseBoardCameraLease();
            EnsurePlayerCameraDuringSetup();
        }
    }

    private void EnsurePlayerCameraDuringSetup()
    {
        if (_boardCameraLeaseActive || CameraManager.Instance == null) return;

        CinemachineCamera playerCamera = CameraManager.Instance.VcamThirdPerson;
        if (playerCamera == null) return;

        bool needsPlayerCameraSwitch = !_playerCameraSetupApplied || playerCamera.Priority.Value <= 0;
        NetworkObject localPlayer = NetworkManager.Singleton != null
            && NetworkManager.Singleton.LocalClient != null
            ? NetworkManager.Singleton.LocalClient.PlayerObject
            : null;
        if (needsPlayerCameraSwitch && localPlayer != null)
        {
            Transform lookTarget = FindChildByName(localPlayer.transform, "CameraLookTarget") ?? localPlayer.transform;
            CameraManager.Instance.SetPlayerTarget(lookTarget, lookTarget);
        }

        if (needsPlayerCameraSwitch)
        {
            CameraManager.Instance.SwitchCamera(CameraPreset.ThirdPerson);
            _playerCameraSetupApplied = true;
        }
        foreach (CinemachineCamera camera in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
        {
            if (camera != null && camera != playerCamera) camera.Priority.Value = 0;
        }
        playerCamera.gameObject.SetActive(true);
        playerCamera.enabled = true;
        playerCamera.Priority.Value = 1000;
    }

    private static string RoleDisplayName(ErisRole role)
    {
        return role == ErisRole.Controller ? "\u004e\u0047\u01af\u1edcI \u0110I\u1ec0U KHI\u1ec2N" : "\u004e\u0047\u01af\u1edcI QUAN S\u00c1T";
    }

    public bool IsRoleLocked(ErisRole role)
    {
        return role == ErisRole.Controller
            ? _roleControllerId.Value != ulong.MaxValue
            : role == ErisRole.Observer && _roleObserverId.Value != ulong.MaxValue;
    }

    public bool CanSelectRole(ErisRole role)
    {
        if (_sessionPhase.Value != ErisSessionPhase.RoleSelection || role == ErisRole.None || NetworkManager.Singleton == null) return false;
        if (LocalRole != ErisRole.None) return false;
        return !IsRoleLocked(role);
    }

    public void RequestRole(ErisRole role)
    {
        if (CanSelectRole(role)) RequestRoleServerRpc(role);
    }

    public void RequestStart()
    {
        if (CanStartSession) RequestStartServerRpc();
    }

    public void RequestSwapRoles()
    {
        if (_sessionPhase.Value == ErisSessionPhase.Playing && !IsSoloSession) SwapRolesAndResetServerRpc();
    }

    public void RequestReplayPath()
    {
        if (_sessionPhase.Value == ErisSessionPhase.Playing) ReplayPathServerRpc();
    }

    private bool CanStartSessionFor(ulong clientId)
    {
        bool hasController = _roleControllerId.Value != ulong.MaxValue;
        bool hasObserver = _roleObserverId.Value != ulong.MaxValue;
        return _sessionPhase.Value == ErisSessionPhase.RoleSelection
            && hasController
            && (hasObserver || clientId == _roleControllerId.Value)
            && clientId == _startClientId.Value;
    }

    private void OnPiecePosChanged(Vector2Int oldVal, Vector2Int newVal)
    {
        if (newVal.x != -1
            && _sessionPhase.Value == ErisSessionPhase.Playing
            && _syncedPath != null
            && _syncedPath.Length > 0)
        {
            if (_spawnedPieceInstance == null) SpawnChessPieceLocal();
            UpdatePieceTargetSafe(newVal); 
        }
    }

    private void OnMemorizingChanged(bool oldVal, bool newVal)
    {
        // A death reset also clears this replicated flag. Do not start the
        // idle board animation/audio when the session is already leaving.
        if (!newVal
            && (_sessionPhase.Value != ErisSessionPhase.Playing
                || _syncedPath == null
                || _syncedPath.Length == 0)) return;
        if (!newVal) {
            StopPathLoop();
            if (_loopingSource != null) { AudioManager.Instance.StopSFX(_loopingSource); _loopingSource = null; }
            AudioManager.Instance.PlaySFX(ReadyToPlaySFX);
            if (_idleWaveCoroutine != null) StopCoroutine(_idleWaveCoroutine);
            _idleWaveCoroutine = StartCoroutine(IdleWaveRoutine());
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || _isGameActive.Value || _hasCompleted.Value) return;
        if (_illusionState.Value != ErisIllusionState.Ready) return;

        // The monkey Player's NetworkObject/Tag is on the root, while the
        // CharacterController collider that enters this trigger can be a child.
        // Resolve through the parent so the board cannot silently ignore a valid
        // Player collider.
        NetworkObject netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj == null) return;

        bool isPlayer = other.CompareTag(Constants.Tags.PLAYER)
            || netObj.CompareTag(Constants.Tags.PLAYER)
            || netObj.CompareTag("Player")
            || netObj.GetComponent<NGOPlayerSync>() != null
            || netObj.GetComponent<PlayerController>() != null;
        if (isPlayer)
            StartIllusionSequenceServer(netObj.OwnerClientId);
    }

    /// <summary>
    /// Called by Collider 2 after the terrain reveal. The server remains the only
    /// authority allowed to start the networked board state.
    /// </summary>
    public void TryStartBoardFromActivationTriggerServer(ulong triggerPlayerId)
    {
        if (!IsServer
            || _illusionState.Value != ErisIllusionState.Revealing
            || _isGameActive.Value
            || _hasCompleted.Value)
        {
            return;
        }

        if (!TryGetPlayerObject(triggerPlayerId, out NetworkObject playerObject)) return;

        if (_boardActivationCollider != null
            && Vector3.Distance(
                playerObject.transform.position,
                _boardActivationCollider.ClosestPoint(playerObject.transform.position)) > 1.5f)
        {
            return;
        }

        BeginRoleSelectionServer();
    }

    private void StartIllusionSequenceServer(ulong triggerPlayerId)
    {
        if (!IsServer
            || _illusionState.Value != ErisIllusionState.Ready
            || _isGameActive.Value
            || _hasCompleted.Value)
        {
            return;
        }

        if (!TryGetPlayerObject(triggerPlayerId, out _)) return;

        _illusionState.Value = ErisIllusionState.Revealing;
        SetBoardActivationColliderEnabled(false);
        FreezeLocalPlayersClientRpc();
        ApplyIllusionPresentationClientRpc(false);
        SetBoardActivationColliderClientRpc(true);

        if (_autoStartBoardAfterReveal)
        {
            _illusionRoutine = StartCoroutine(StartBoardAfterRevealRoutine(triggerPlayerId));
        }
    }

    private IEnumerator StartBoardAfterRevealRoutine(ulong triggerPlayerId)
    {
        yield return new WaitForSecondsRealtime(_illusionRevealDelay);

        if (IsServer
            && _illusionState.Value == ErisIllusionState.Revealing
            && !_isGameActive.Value
            && !_hasCompleted.Value)
        {
            BeginRoleSelectionServer();
        }

        _illusionRoutine = null;
    }

    private void BeginRoleSelectionServer()
    {
        if (!IsServer || _sessionPhase.Value != ErisSessionPhase.Idle) return;

        _sessionPhase.Value = ErisSessionPhase.RoleSelection;
        _roleControllerId.Value = ulong.MaxValue;
        _roleObserverId.Value = ulong.MaxValue;
        _countdownValue.Value = 0;
        _startClientId.Value = ulong.MaxValue;
        _roleSelectionTick = ulong.MaxValue;
        _roleSelectionFirstClient = ulong.MaxValue;
        _roleSelectionFirstRole = ErisRole.None;
        _isGameActive.Value = true;
        _hasCompleted.Value = false;
        FreezeLocalPlayersClientRpc();
        EventBus.RaiseGamePaused();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestRoleServerRpc(ErisRole role, RpcParams rpcParams = default)
    {
        if (!IsServer || _sessionPhase.Value != ErisSessionPhase.RoleSelection || role == ErisRole.None) return;

        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!TryGetPlayerObject(clientId, out _)) return;
        if (clientId == _roleControllerId.Value || clientId == _roleObserverId.Value) return;

        ulong serverTick = (ulong)NetworkManager.ServerTime.Tick;
        if (_roleSelectionTick == serverTick
            && _roleSelectionFirstClient != ulong.MaxValue
            && _roleSelectionFirstClient != clientId)
        {
            _roleControllerId.Value = ulong.MaxValue;
            _roleObserverId.Value = ulong.MaxValue;
            _roleSelectionTick = ulong.MaxValue;
            _roleSelectionFirstClient = ulong.MaxValue;
            _roleSelectionFirstRole = ErisRole.None;
            RoleConflictClientRpc();
            return;
        }

        if (role == ErisRole.Controller)
        {
            if (_roleControllerId.Value != ulong.MaxValue) return;
            _roleControllerId.Value = clientId;
        }
        else
        {
            if (_roleObserverId.Value != ulong.MaxValue) return;
            _roleObserverId.Value = clientId;
        }

        _roleSelectionTick = serverTick;
        _roleSelectionFirstClient = clientId;
        _roleSelectionFirstRole = role;
        _startClientId.Value = clientId;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestStartServerRpc(RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!IsServer || !CanStartSessionFor(senderClientId) || _countdownRoutine != null) return;
        if (!TryGetPlayerObject(senderClientId, out _)) return;
        _countdownRoutine = StartCoroutine(CountdownRoutine());
    }

    private IEnumerator CountdownRoutine()
    {
        _sessionPhase.Value = ErisSessionPhase.Countdown;
        for (int count = 3; count >= 0; count--)
        {
            if (!IsServer || _sessionPhase.Value != ErisSessionPhase.Countdown)
                yield break;
            _countdownValue.Value = count;
            if (count == 0)
                ForceBoardCameraAtCountdownEndClientRpc(_roleControllerId.Value, _roleObserverId.Value);
            yield return new WaitForSecondsRealtime(count == 0 ? 0.55f : 0.8f);
        }

        _countdownValue.Value = 0;
        _countdownRoutine = null;
        StartMinigameServer(_roleControllerId.Value);
    }

    private void OnGameActiveChanged(bool oldValue, bool newValue)
    {
        if (newValue || !IsSpawned) return;
        CleanupBoardImmediate();
        _canInput = false;
        _isReseting = false;
        _lockedPositions.Clear();
        _lockedRotations.Clear();
        _syncedPath = System.Array.Empty<Vector2Int>();
    }

    private void OnIllusionStateChanged(ErisIllusionState oldState, ErisIllusionState newState)
    {
        if (newState != ErisIllusionState.Completed) return;
        SetBoardActivationColliderEnabled(false);
        ApplyIllusionPresentationLocal(true);
    }

    [ClientRpc]
    private void RoleConflictClientRpc()
    {
        Debug.Log("[ErisMinigameManager] Hai ngÆ°á»i chá»n vai trÃ² cÃ¹ng lÃºc; cáº£ hai lá»±a chá»n Ä‘Ã£ Ä‘Æ°á»£c há»§y.");
    }

    [ClientRpc]
    private void ForceBoardCameraAtCountdownEndClientRpc(ulong controllerId, ulong observerId)
    {
        if (NetworkManager.Singleton == null) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (localId != controllerId && localId != observerId) return;

        _cameraDirection = localId == observerId ? 1 : 0;
        _boardCameraLeaseActive = true;

        EnsureRuntimeErisCamera();
        SyncCameraToManager();
        StartCoroutine(EnsureBoardCameraRoutine());
    }

    private void StartMinigameServer(ulong triggerPlayerId)
    {
        if (!IsServer
            || _illusionState.Value == ErisIllusionState.Completed
            || _roleControllerId.Value == ulong.MaxValue
            || _roleControllerId.Value != triggerPlayerId)
        {
            return;
        }

        ApplyIllusionPresentationClientRpc(false);
        _illusionState.Value = ErisIllusionState.BoardActive;
        _sessionPhase.Value = ErisSessionPhase.Playing;
        _isGameActive.Value = true; _isMemorizing.Value = true; _controllerId.Value = _roleControllerId.Value; _observerId.Value = _roleObserverId.Value; _hasCompleted.Value = false;
        _completionStarted = false;
        if (_completionRoutine != null)
        {
            StopCoroutine(_completionRoutine);
            _completionRoutine = null;
        }
        _syncedPath = GenerateFreshPathArray();
        _lastSessionPath = (Vector2Int[])_syncedPath.Clone();
        _pieceGridPos.Value = _syncedPath[0]; _currentStepIndex.Value = 0;
        
        StartCoroutine(SetupBoardAfterStandTeleportServer(
            _controllerId.Value,
            _observerId.Value,
            _syncedPath));
    }

    private IEnumerator SetupBoardAfterStandTeleportServer(
        ulong controllerId,
        ulong observerId,
        Vector2Int[] path)
    {
        if (!IsServer) yield break;

        ulong[] sessionPlayers = observerId == ulong.MaxValue
            ? new[] { controllerId }
            : new[] { controllerId, observerId };

        foreach (ulong clientId in sessionPlayers)
        {
            if (!TryGetPlayerObject(clientId, out NetworkObject playerObject)
                || !playerObject.TryGetComponent<NGOPlayerSync>(out NGOPlayerSync playerSync))
            {
                continue;
            }

            Transform standPoint = clientId == controllerId ? ControllerStandPos : ObserverStandPos;
            if (standPoint == null || !IsFinite(standPoint.position)) continue;

            yield return playerSync.TeleportAndConfirmWithRetry(
                standPoint.position,
                standPoint.rotation,
                null,
                1,
                0f);
        }

        if (IsServer && _sessionPhase.Value == ErisSessionPhase.Playing)
            SetupBoardClientRpc(controllerId, observerId, path);
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

    private Vector2Int[] GenerateFreshPathArray()
    {
        Vector2Int[] candidate = GeneratePathArray();
        for (int attempt = 0; attempt < 4 && PathsEqual(candidate, _lastSessionPath); attempt++)
            candidate = GeneratePathArray();
        return candidate;
    }

    private static bool PathsEqual(Vector2Int[] first, Vector2Int[] second)
    {
        if (first == null || second == null || first.Length != second.Length) return false;
        for (int i = 0; i < first.Length; i++)
            if (first[i] != second[i]) return false;
        return true;
    }

    [ClientRpc]
    private void SetupBoardClientRpc(ulong controllerId, ulong observerId, Vector2Int[] path)
    {
        _syncedPath = path; CleanupBoardImmediate();
        _canInput = false;
        try { if (_gameStartFeedback != null) _gameStartFeedback.PlayFeedbacks(); } catch {}
        _spawnCoroutine = StartCoroutine(SpawnTilesWaveDiagonalSafe());
        
        // Spawn ChessPiece cá»¥c bá»™ trÃªn má»—i mÃ¡y
        SpawnChessPieceLocal();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null) {
            var lp = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (lp != null) {
                if (lp.TryGetComponent<Rigidbody>(out var rb))
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = false;
                    rb.useGravity = true;
                }
                if (lp.TryGetComponent<PlayerController>(out var controller))
                    controller.SetExternalMovementOverride(true);
                if (lp.TryGetComponent<PlayerInputHandler>(out var inputHandler))
                    inputHandler.LockAllInput();
                if(lp.TryGetComponent<PlayerStateMachine>(out var fsm)) fsm.TransitionTo(PlayerStateType.Idle);
                _lockedPositions[NetworkManager.Singleton.LocalClientId] = lp.transform.position; _lockedRotations[NetworkManager.Singleton.LocalClientId] = lp.transform.rotation;
            }
        }
        _cameraDirection = NetworkManager.Singleton.LocalClientId == observerId ? 1 : 0;
        // Camera activation is handled by the countdown-zero RPC. Spawning the
        // board itself must never steal the Player camera.
        bool isSolo = observerId == ulong.MaxValue;
        if (NetworkManager.Singleton.LocalClientId == controllerId) {
            if (isSolo) {
                if (BlackFogVFX != null) { BlackFogVFX.Stop(); BlackFogVFX.Clear(); }
                if (_loopingSource != null) { try { AudioManager.Instance.StopSFX(_loopingSource); } catch {} _loopingSource = null; }
                StartCoroutine(SoloPathRevealRoutine());
            }
            else {
                if (BlackFogVFX != null) BlackFogVFX.Play(); if (_loopingSource != null) AudioManager.Instance.StopSFX(_loopingSource);
                _loopingSource = AudioManager.Instance.PlaySFXLoop(ControllerWaitingSFX);
            }
        } 
        else if (NetworkManager.Singleton.LocalClientId == observerId) { StartPathLoop(); AudioManager.Instance.PlaySFX(ObserverPathRevealSFX); }
        EventBus.RaiseGamePaused(); 
    }

    private IEnumerator DelayedCameraSync()
    {
        float timeout = 6f;
        while (!_canInput && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        SyncCameraToManager();
    }

    private IEnumerator EnsureBoardCameraRoutine()
    {
        yield return null;
        EnsureRuntimeErisCamera();
        SyncCameraToManager();
    }

    private void EnsureRuntimeErisCamera()
    {
        if (!_boardCameraLeaseActive) return;
        Transform marker = GetBoardCameraMarker(_cameraDirection);
        CinemachineCamera markerCamera = marker != null ? marker.GetComponent<CinemachineCamera>() : null;
        if (markerCamera != null)
        {
            SetErisCameraPriorities(markerCamera);
            _runtimeErisCamera = markerCamera;
            _ownsRuntimeErisCamera = false;
            markerCamera.Target.TrackingTarget = null;
            markerCamera.Target.LookAtTarget = null;
            ConfigureErisCameraLens(markerCamera);
            return;
        }

        if (_runtimeErisCamera == null)
        {
            GameObject cameraObject = new GameObject("Eris Board Camera (Local Player)");
            _runtimeErisCamera = cameraObject.AddComponent<CinemachineCamera>();
            _ownsRuntimeErisCamera = true;
        }

        SetErisCameraPriorities(_runtimeErisCamera);
        _runtimeErisCamera.Target.TrackingTarget = null;
        _runtimeErisCamera.Target.LookAtTarget = null;
        if (marker != null)
            _runtimeErisCamera.transform.SetPositionAndRotation(marker.position, marker.rotation);

        ConfigureErisCameraLens(_runtimeErisCamera);
    }

    private void SetErisCameraPriorities(CinemachineCamera selectedCamera)
    {
        foreach (CinemachineCamera camera in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
        {
            if (camera != null) camera.Priority.Value = camera == selectedCamera ? 1000 : 0;
        }
        if (selectedCamera != null)
        {
            selectedCamera.gameObject.SetActive(true);
            selectedCamera.enabled = true;
            selectedCamera.Priority.Value = 1000;
            if (_lastLoggedErisCameraName != selectedCamera.name)
            {
                _lastLoggedErisCameraName = selectedCamera.name;
                Debug.Log($"[ErisMinigameManager] Forced local Eris camera: {selectedCamera.name} (Priority {selectedCamera.Priority.Value})");
            }
        }
    }

    private void ConfigureErisCameraLens(CinemachineCamera camera)
    {
        if (camera == null) return;
        CinemachineFollow follow = camera.GetComponent<CinemachineFollow>();
        if (follow != null) follow.enabled = false;
        CinemachineInputAxisController inputAxis = camera.GetComponent<CinemachineInputAxisController>();
        if (inputAxis != null) inputAxis.enabled = false;
        var lens = camera.Lens;
        lens.FieldOfView = CameraFOV;
        lens.NearClipPlane = 0.05f;
        lens.FarClipPlane = Mathf.Max(lens.FarClipPlane, 250f);
        camera.Lens = lens;
    }

    private void DestroyRuntimeErisCamera()
    {
        if (_runtimeErisCamera == null) return;
        if (_ownsRuntimeErisCamera)
            Destroy(_runtimeErisCamera.gameObject);
        else
            _runtimeErisCamera.Priority.Value = 0;
        _runtimeErisCamera = null;
        _ownsRuntimeErisCamera = false;
        _lastLoggedErisCameraName = null;
    }

    private void ReleaseBoardCameraLease()
    {
        _boardCameraLeaseActive = false;
        foreach (CinemachineCamera camera in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
        {
            if (camera == null) continue;
            if (camera.transform == _boardCameraTopMarker
                || camera.transform == _boardCameraLeftMarker
                || camera.transform == _boardCameraRightMarker)
            {
                camera.Priority.Value = 0;
            }
        }
    }

    private IEnumerator SpawnTilesWaveDiagonalSafe()
    {
        _spawnedTiles.Clear();
        SetCachedIllusionGroundVisibleForSampling(true);
        Transform boardAnchor = GetBoardAnchor();
        for (int sum = 0; sum <= 18; sum++) {
            for (int x = 0; x <= sum; x++) {
                int y = sum - x;
                if (x < 10 && y < 10) {
                    Vector3 boardPosition = boardAnchor.TransformPoint(GetTileLocalOffset(x, y));
                    SampleTileSurface(boardPosition, boardAnchor, out Vector3 landingPosition, out Quaternion landingRotation, out Color surfaceColor);
                    GameObject tileObj = Instantiate(TilePrefab, landingPosition, landingRotation, boardAnchor);
                    ErisTile tile = tileObj.GetComponent<ErisTile>();
                    try
                    {
                        if (tile != null)
                        {
                            tile.SetBoardScale(GetBoardScale());
                            tile.SetSurfaceColorOnLanding(surfaceColor);
                        }
                        if (tile != null) tile.InitEntrance(new Vector2Int(x, y), landingPosition, landingRotation, _tileDropHeight, _tileDropDuration);
                    }
                    catch { }
                    _spawnedTiles.Add(tile);
                }
            }
            if (_tileWaveDelay > 0f) yield return new WaitForSeconds(_tileWaveDelay);
            else yield return null;
        }

        while (_spawnedTiles.Exists(tile => tile != null && tile.IsEntrancePlaying))
            yield return null;

        SetCachedIllusionGroundVisibleForSampling(false);

        _canInput = true; 
        _spawnCoroutine = null;
        
        // Cáº­p nháº­t vá»‹ trÃ­ Piece má»™t láº§n ná»¯a sau khi tiles Ä‘Ã£ sáºµn sÃ ng
        if (_pieceGridPos.Value.x != -1) UpdatePieceTargetSafe(_pieceGridPos.Value);
    }

    private void SetCachedIllusionGroundVisibleForSampling(bool visible)
    {
        foreach (GameObject groundObject in _illusionGroundStates.Keys)
        {
            if (groundObject != null) groundObject.SetActive(visible);
        }
    }

    private IEnumerator IdleWaveRoutine() {
        float timer = 0;
        while (_isGameActive.Value && !_hasCompleted.Value) {
            yield return new WaitForSeconds(0.2f); // Nhá»‹p Ä‘á»™ "mÆ°a rÆ¡i"
            if (_isReseting || _isMemorizing.Value) continue;

            // 1. Hiá»‡u á»©ng Raindrop: NhÃºng nháº£y ngáº«u nhiÃªn 2-3 Ã´
            for (int i = 0; i < 2; i++) {
                int idx = Random.Range(0, _spawnedTiles.Count);
                if (_spawnedTiles[idx] != null) _spawnedTiles[idx].PlayIdleBounce();
            }

            // 2. Hiá»‡u á»©ng Center Pulse: Cá»© má»—i 5 giÃ¢y ná»• má»™t sÃ³ng tá»« tÃ¢m
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

    private void SyncCameraToManager()
    {
        if (!_boardCameraLeaseActive) return;
        EnsureRuntimeErisCamera();
        Transform marker = GetBoardCameraMarker(_cameraDirection);
        if (_runtimeErisCamera == null) return;

        _runtimeErisCamera.Target.TrackingTarget = null;
        _runtimeErisCamera.Target.LookAtTarget = null;
        if (marker != null)
            _runtimeErisCamera.transform.SetPositionAndRotation(marker.position, marker.rotation);
        ConfigureErisCameraLens(_runtimeErisCamera);
    }

    private void StartPathLoop() { if (_pathLoopCoroutine != null) StopCoroutine(_pathLoopCoroutine); _pathLoopCoroutine = StartCoroutine(PathRevealRoutine()); }
    private void StopPathLoop() {
        if (_pathLoopCoroutine != null) StopCoroutine(_pathLoopCoroutine);
        foreach (var t in _spawnedTiles) t.RestoreColor();
        if (NetworkManager.Singleton.LocalClientId == _controllerId.Value) { if (BlackFogVFX != null) { BlackFogVFX.Stop(); BlackFogVFX.Clear(); } HighlightPossibleMoves(_pieceGridPos.Value); }
        else { ErisTile st = GetTileAt(_syncedPath[0]); if (st != null) st.SetColor(Color.green, true); }
    }

    private IEnumerator PathRevealRoutine() {
        float timeout = 10f; // Chá» tá»‘i Ä‘a 10 giÃ¢y cho tiles spawn xong
        while (_spawnedTiles.Count < 100 && timeout > 0) {
            timeout -= Time.deltaTime;
            yield return null;
        }
        
        if (_spawnedTiles.Count < 100) {
            Debug.LogWarning("[ErisMinigameManager] Tiles did not spawn 100 items in time. Starting path reveal anyway.");
        }

        while (_spawnedTiles.Exists(tile => tile != null && tile.IsEntrancePlaying))
            yield return null;

        while (true) {
            foreach (var t in _spawnedTiles) t.ResetTile();
            yield return new WaitForSeconds(0.5f); 
            foreach (var step in _syncedPath) {
                ErisTile tile = GetTileAt(step);
                if (tile != null) { tile.SetColor(Color.green); if (RevealTileSFX != null) AudioManager.Instance.PlaySFX(RevealTileSFX); }
                yield return new WaitForSeconds(0.2f); 
            }
            yield return new WaitForSeconds(2.5f); 
        }
    }

    private IEnumerator SoloPathRevealRoutine()
    {
        float timeout = 10f;
        while (_spawnedTiles.Count < 100 && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        while (_spawnedTiles.Exists(tile => tile != null && tile.IsEntrancePlaying))
            yield return null;

        foreach (ErisTile tile in _spawnedTiles) tile?.ResetTile();
        yield return new WaitForSeconds(0.5f);
        foreach (Vector2Int step in _syncedPath)
        {
            ErisTile tile = GetTileAt(step);
            if (tile != null)
            {
                tile.SetColor(Color.green);
                if (RevealTileSFX != null) AudioManager.Instance.PlaySFX(RevealTileSFX);
            }
            yield return new WaitForSeconds(0.2f);
        }

        yield return new WaitForSeconds(1.5f);
        foreach (ErisTile tile in _spawnedTiles) tile?.ResetTile();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == _controllerId.Value)
            ReadyToPlayServerRpc();
    }

    private void SpawnChessPieceLocal() {
        if (_spawnedPieceInstance != null) return;
        
        Vector2Int gridPos = _pieceGridPos.Value.x != -1 ? _pieceGridPos.Value : new Vector2Int(0,0);
        Transform boardAnchor = GetBoardAnchor();
        Vector3 worldStart = GetPieceWorldTarget(gridPos);
        
        // KHÃ”NG gÃ¡n 'transform' lÃ m cha á»Ÿ Ä‘Ã¢y vÃ¬ Prefab cÃ³ thá»ƒ chá»©a NetworkObject, gÃ¢y crash náº¿u gÃ¡n lÃ m con cá»§a má»™t NetworkObject khÃ¡c mÃ  khÃ´ng Spawn
        _spawnedPieceInstance = Instantiate(ChessPiecePrefab, worldStart, boardAnchor.rotation);
        _spawnedPieceInstance.transform.localScale *= GetBoardScale();
        
        Debug.Log($"[ErisMinigameManager] ChessPiece spawned LOCALLY for client {NetworkManager.Singleton.LocalClientId}");
    }

    private void Update() {
        SyncLiveInspectorCoordinates();
        if (!IsSpawned || !_isGameActive.Value || _sessionPhase.Value != ErisSessionPhase.Playing) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null) {
            var lp = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (lp != null) {
                if (_lockedPositions.TryGetValue(NetworkManager.Singleton.LocalClientId, out Vector3 lockPos))
                {
                    // Keep the assigned horizontal standing point while leaving
                    // Y dynamic so gravity can settle the player on the floor.
                    Vector3 currentPosition = lp.transform.position;
                    currentPosition.x = lockPos.x;
                    currentPosition.z = lockPos.z;
                    lp.transform.position = currentPosition;
                    if (lp.TryGetComponent<Rigidbody>(out var body))
                    {
                        Vector3 velocity = body.linearVelocity;
                        velocity.x = 0f;
                        velocity.z = 0f;
                        body.linearVelocity = velocity;
                    }
                }
                if(_lockedRotations.TryGetValue(NetworkManager.Singleton.LocalClientId, out Quaternion lockRot)) lp.transform.rotation = lockRot;
            }
        }
        
        if (NetworkManager.Singleton != null) {
            if (NetworkManager.Singleton.LocalClientId == _observerId.Value && _isMemorizing.Value && Input.GetKeyDown(KeyCode.E)) ReadyToPlayServerRpc();
            if (NetworkManager.Singleton.LocalClientId == _controllerId.Value && !_isMemorizing.Value && !_isReseting && _canInput) HandleKeyboardInput(); 
            // Camera switching is available during the reveal/memorization loop too.
            // E remains reserved for the observer's ready action.
            if (_boardCameraLeaseActive)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow)) SwitchBoardCamera(-1);
                if (Input.GetKeyDown(KeyCode.RightArrow)) SwitchBoardCamera(1);
                if (Input.GetKeyDown(KeyCode.UpArrow)) SetBoardCameraDirection(1);
            }
        }
        
        if (_allowDebugCheat && (Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Equals))) { _showDebugPath = !_showDebugPath; ToggleDebugPath(_showDebugPath); }
    }

    private void LateUpdate()
    {
        // Cinemachine/CameraManager may write the active virtual-camera pose
        // later in the frame. Re-apply the edited marker after those systems so
        // Inspector changes are visible immediately without switching camera keys.
        if (!Application.isPlaying || !IsSpawned || !_isGameActive.Value) return;

        if (!_boardCameraLeaseActive)
        {
            if (_sessionPhase.Value == ErisSessionPhase.RoleSelection
                || _sessionPhase.Value == ErisSessionPhase.Countdown)
            {
                EnsurePlayerCameraDuringSetup();
            }
            return;
        }

        Transform marker = GetBoardCameraMarker(_cameraDirection);
        if (_runtimeErisCamera == null)
            EnsureRuntimeErisCamera();
        CinemachineCamera selectedCamera = marker != null ? marker.GetComponent<CinemachineCamera>() : _runtimeErisCamera;
        if (selectedCamera != null)
        {
            SetErisCameraPriorities(selectedCamera);
            if (marker != null)
                selectedCamera.transform.SetPositionAndRotation(marker.position, marker.rotation);
        }
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
    private void ReadyToPlayServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer || _sessionPhase.Value != ErisSessionPhase.Playing || !_isMemorizing.Value) return;
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        bool isObserver = senderClientId == _observerId.Value;
        bool isSoloController = _observerId.Value == ulong.MaxValue && senderClientId == _controllerId.Value;
        if (!isObserver && !isSoloController) return;
        _isMemorizing.Value = false;
    }

    private void SampleTileSurface(Vector3 boardPosition, Transform boardAnchor,
        out Vector3 landingPosition, out Quaternion landingRotation, out Color surfaceColor)
    {
        Material cachedMaterial = FindCachedGroundMaterial(boardPosition);
        surfaceColor = ReadMaterialColor(cachedMaterial);
        if (_lockTilesToBoardPlane)
        {
            landingPosition = boardPosition + boardAnchor.up * _surfaceOffset;
            landingRotation = boardAnchor.rotation;
            return;
        }

        Vector3 probeDirection = boardAnchor.up;
        Vector3 origin = boardPosition + probeDirection * _groundProbeHeight;
        float distance = _groundProbeHeight + _groundProbeDistance;
        if (Physics.Raycast(origin, -probeDirection, out RaycastHit hit, distance, _groundLayers, QueryTriggerInteraction.Ignore))
        {
            Material hitMaterial = ResolveSurfaceMaterial(hit);
            surfaceColor = SampleSurfaceColor(hit, hitMaterial, surfaceColor);
            Vector3 normal = hit.normal.normalized;
            landingPosition = hit.point + normal * _surfaceOffset;
            landingRotation = _alignTilesToGround ? CreateSurfaceRotation(normal, boardAnchor) : boardAnchor.rotation;
            return;
        }

        landingPosition = boardPosition + probeDirection * _surfaceOffset;
        landingRotation = boardAnchor.rotation;
    }

    private static Material ResolveSurfaceMaterial(RaycastHit hit)
    {
        Renderer renderer = hit.collider != null
            ? hit.collider.GetComponentInParent<Renderer>()
            : null;
        if (renderer != null && renderer.sharedMaterial != null)
            return renderer.sharedMaterial;

        Terrain terrain = hit.collider != null
            ? hit.collider.GetComponentInParent<Terrain>()
            : null;
        return terrain != null ? terrain.materialTemplate : null;
    }

    private static Color SampleSurfaceColor(RaycastHit hit, Material material, Color fallback)
    {
        if (material == null) return fallback;

        Color tint = ReadMaterialColor(material);
        string textureProperty = material.HasProperty("_BaseMap") ? "_BaseMap"
            : material.HasProperty("_MainTex") ? "_MainTex" : null;
        if (textureProperty == null) return tint;

        Texture2D texture = material.GetTexture(textureProperty) as Texture2D;
        if (texture == null) return tint;

        try
        {
            Vector2 uv = hit.textureCoord;
            uv = Vector2.Scale(uv, material.GetTextureScale(textureProperty))
                + material.GetTextureOffset(textureProperty);
            Color texel = texture.GetPixelBilinear(Mathf.Repeat(uv.x, 1f), Mathf.Repeat(uv.y, 1f));
            return texel * tint;
        }
        catch (UnityException)
        {
            // Non-readable imported textures still provide their material tint.
            return tint;
        }
    }

    private static Color ReadMaterialColor(Material material)
    {
        if (material == null) return Color.white;
        if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color")) return material.GetColor("_Color");
        if (material.HasProperty("_TintColor")) return material.GetColor("_TintColor");
        if (material.HasProperty("_MainColor")) return material.GetColor("_MainColor");
        return material.color;
    }

    private Material FindCachedGroundMaterial(Vector3 worldPosition)
    {
        Material closest = null;
        float closestDistance = float.PositiveInfinity;
        foreach (KeyValuePair<GameObject, Material> entry in _illusionGroundMaterials)
        {
            if (entry.Key == null || entry.Value == null) continue;
            if (!_illusionGroundBounds.TryGetValue(entry.Key, out Bounds bounds)) continue;
            float distance = (bounds.ClosestPoint(worldPosition) - worldPosition).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = entry.Value;
            }
        }

        return closest;
    }

    private static Quaternion CreateSurfaceRotation(Vector3 normal, Transform boardAnchor)
    {
        Vector3 forward = Vector3.ProjectOnPlane(boardAnchor.forward, normal);
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(boardAnchor.right, normal);
        return Quaternion.LookRotation(forward.normalized, normal);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitMoveServerRpc(Vector2Int gridPos, RpcParams rpcParams = default) {
        if (!IsServer
            || _sessionPhase.Value != ErisSessionPhase.Playing
            || _isMemorizing.Value
            || _completionStarted)
        {
            return;
        }

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (senderClientId != _controllerId.Value) return;
        if (_syncedPath == null || _syncedPath.Length == 0) return;

        Vector2Int currentPos = _pieceGridPos.Value;
        if (Mathf.Abs(gridPos.x - currentPos.x) + Mathf.Abs(gridPos.y - currentPos.y) == 1 && _currentStepIndex.Value + 1 < _syncedPath.Length && gridPos == _syncedPath[_currentStepIndex.Value + 1]) 
        { _currentStepIndex.Value++; _pieceGridPos.Value = gridPos; } 
        else { ApplyMistakeDamageServer(); WrongMoveEffectClientRpc(gridPos); StartCoroutine(ResetServerDelayed()); }
    }

    private void ApplyMistakeDamageServer()
    {
        if (!IsServer || !TryGetPlayerObject(_controllerId.Value, out NetworkObject controller)) return;
        if (controller.TryGetComponent<PlayerHealth>(out PlayerHealth health))
            health.TakeDamage(health.MaxHealth * 0.1f);
    }

    private IEnumerator ResetServerDelayed()
    {
        yield return new WaitForSecondsRealtime(2.0f);
        if (!IsServer
            || _sessionPhase.Value != ErisSessionPhase.Playing
            || _completionStarted
            || _syncedPath == null
            || _syncedPath.Length == 0)
        {
            yield break;
        }

        _currentStepIndex.Value = 0;
        _pieceGridPos.Value = new Vector2Int(-1, -1);
        yield return null;
        _pieceGridPos.Value = _syncedPath[0];
    }

    [ClientRpc]
    private void WrongMoveEffectClientRpc(Vector2Int wrongPos) { try { if (_gameFailureFeedback != null) _gameFailureFeedback.PlayFeedbacks(); } catch {} StartCoroutine(WrongMoveRippleEffect(wrongPos)); }

    private IEnumerator WrongMoveRippleEffect(Vector2Int wrongPos) {
        _isReseting = true; _canInput = false; foreach (var t in _spawnedTiles) t.SetHighlight(false);
        AudioManager.Instance.PlaySFX(WrongMoveSFX);
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
        
        // Äáº£m báº£o piece Ä‘Ã£ tá»“n táº¡i
        if (_spawnedPieceInstance == null) SpawnChessPieceLocal();
        
        Vector3 worldTarget = GetPieceWorldTarget(gridPos);
        if (_moveCoroutine != null) StopCoroutine(_moveCoroutine); 
        _moveCoroutine = StartCoroutine(SmoothMovePiece(worldTarget, gridPos));
        
        ErisTile t = GetTileAt(gridPos); 
        if (t != null) { try { t.SetColor(Color.green, true); } catch {} }
        if (IsServer
            && !_completionStarted
            && _sessionPhase.Value == ErisSessionPhase.Playing
            && _syncedPath != null
            && _syncedPath.Length > 0
            && _currentStepIndex.Value == _syncedPath.Length - 1)
        {
            _completionStarted = true;
            _completionRoutine = StartCoroutine(EndGameDelayed());
        }
    }

    private IEnumerator SmoothMovePiece(Vector3 target, Vector2Int gridPos) {
        if (_spawnedPieceInstance == null) yield break;

        // SNAP ngay láº­p tá»©c náº¿u lÃ  vá»‹ trÃ­ khá»Ÿi Ä‘áº§u
        if (_currentStepIndex.Value == 0 || _isReseting) {
            _spawnedPieceInstance.transform.position = target;
            _spawnedPieceInstance.transform.rotation = GetPieceWorldRotation(gridPos);
        }

        if (!_isReseting && _currentStepIndex.Value > 0 && _moveFeedback != null) { try { _moveFeedback.PlayFeedbacks(_spawnedPieceInstance.transform.position); } catch {} }
        
        float speed = (_isReseting || _currentStepIndex.Value == 0) ? 50f : 12f; 
        while (Vector3.Distance(_spawnedPieceInstance.transform.position, target) > 0.01f) {
            if (_spawnedPieceInstance == null) yield break;
            _spawnedPieceInstance.transform.position = Vector3.MoveTowards(_spawnedPieceInstance.transform.position, target, speed * Time.deltaTime);
            _spawnedPieceInstance.transform.rotation = Quaternion.Lerp(_spawnedPieceInstance.transform.rotation, GetPieceWorldRotation(gridPos), Time.deltaTime * 10f);
            yield return null;
        }
        if (_spawnedPieceInstance != null) _spawnedPieceInstance.transform.position = target;
        if (!_isReseting && _currentStepIndex.Value > 0) AudioManager.Instance.PlaySFX(CorrectMoveSFX, _spawnedPieceInstance.transform.position);
        HighlightPossibleMoves(gridPos);
        yield return new WaitForSeconds(0.05f); if (!_isReseting) _canInput = true;
    }

    private void HighlightPossibleMoves(Vector2Int center) {
        foreach (var t in _spawnedTiles) t.SetHighlight(false);
        if (NetworkManager.Singleton.LocalClientId != _controllerId.Value || _isMemorizing.Value || _isReseting) return;
        Vector2Int[] neighbors = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var dir in neighbors) { ErisTile t = GetTileAt(center + dir); if (t != null) t.SetHighlight(true); }
    }

    private IEnumerator EndGameDelayed()
    {
        yield return new WaitForSecondsRealtime(1f);
        if (!IsServer || _sessionPhase.Value != ErisSessionPhase.Playing) yield break;

        // Read the authoritative final position only on the server and send the
        // same value to every client for an identical success presentation.
        SuccessEffectClientRpc(_pieceGridPos.Value);
        yield return new WaitForSecondsRealtime(SuccessPresentationDuration);
        if (IsServer && _sessionPhase.Value == ErisSessionPhase.Playing)
            yield return FinalizeMinigameServer();

        _completionRoutine = null;
    }

    private IEnumerator FinalizeMinigameServer()
    {
        if (!IsServer) yield break;

        // The owner-authoritative Player uses NGOPlayerSync for safe teleports.
        // Never assign transform.position from a ClientRpc: doing so races the
        // NetworkTransform and can launch a player outside the map.
        HashSet<ulong> sessionPlayers = new HashSet<ulong>();
        if (_controllerId.Value != ulong.MaxValue) sessionPlayers.Add(_controllerId.Value);
        if (_observerId.Value != ulong.MaxValue) sessionPlayers.Add(_observerId.Value);

        foreach (ulong clientId in sessionPlayers)
        {
            if (!TryGetCompletionPose(clientId, out Vector3 completionPosition, out Quaternion completionRotation))
            {
                Debug.LogError($"[ErisMinigameManager] KhÃ´ng cÃ³ Ä‘iá»ƒm thoÃ¡t an toÃ n cho client {clientId}; giá»¯ nguyÃªn vá»‹ trÃ­ hiá»‡n táº¡i Ä‘á»ƒ trÃ¡nh vÄƒng khá»i map.", this);
                continue;
            }

            if (!TryGetPlayerObject(clientId, out NetworkObject playerObject)) continue;
            if (!playerObject.TryGetComponent<NGOPlayerSync>(out NGOPlayerSync playerSync)) continue;

            yield return playerSync.TeleportAndConfirmWithRetry(
                completionPosition,
                completionRotation,
                null,
                1,
                0f);
        }
        _isGameActive.Value = false;
        _hasCompleted.Value = true;
        _illusionState.Value = ErisIllusionState.Completed;
        _sessionPhase.Value = ErisSessionPhase.Completed;
        _countdownValue.Value = 0;
        FinalizeMinigameClientRpc();
    }

    private bool TryGetCompletionPose(ulong clientId, out Vector3 position, out Quaternion rotation)
    {
        if (NextAreaSpawn != null)
        {
            position = NextAreaSpawn.position;
            rotation = NextAreaSpawn.rotation;
            return IsFinite(position);
        }

        // Map2 has no scene override for NextAreaSpawn. Use the server-seeded
        // PlayerSpawner/RespawnManager point as a safe fallback instead of
        // releasing a player at the board and risking a physics fling.
        if (RespawnManager.Instance != null
            && RespawnManager.Instance.TryGetCurrentSpawnPosition(clientId, out position))
        {
            rotation = Quaternion.identity;
            Debug.LogWarning("[ErisMinigameManager] NextAreaSpawn chÆ°a Ä‘Æ°á»£c gÃ¡n; dÃ¹ng Ä‘iá»ƒm PlayerSpawner an toÃ n cho lá»‘i ra.", this);
            return true;
        }

        position = default;
        rotation = Quaternion.identity;
        return false;
    }

    private static bool IsFinite(Vector3 position)
    {
        return float.IsFinite(position.x)
            && float.IsFinite(position.y)
            && float.IsFinite(position.z);
    }

    [ClientRpc]
    private void SuccessEffectClientRpc(Vector2Int finalPos) { try { if (_gameSuccessFeedback != null) _gameSuccessFeedback.PlayFeedbacks(); } catch {} StartCoroutine(SuccessSequence(finalPos)); }

    private IEnumerator SuccessSequence(Vector2Int finalPos) {
        _canInput = false;

        // Keep the board camera during the short dissolve. The final RPC
        // restores ground, cleans the board, and switches back to ThirdPerson
        // as one transition so the player never sees an empty/intermediate
        // state.
        AudioManager.Instance.PlaySFX(SuccessSFX);
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
        // Finalization is server-authoritative. Clients only play the visual
        // presentation and must not independently end the network session.
    }

    [ClientRpc]
    private void FinalizeMinigameClientRpc() {
        ApplyIllusionPresentationLocal(true);
        SetBoardActivationColliderEnabled(false);

        var lp = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (lp != null) {
            RestoreLocalPlayerMovement(lp);
        }
        CleanupBoardImmediate(); _lockedPositions.Clear(); _lockedRotations.Clear();
        if (BlackFogVFX != null) { BlackFogVFX.Stop(); BlackFogVFX.Clear(); }
        ReleaseBoardCameraLease();
        DestroyRuntimeErisCamera();
        RestoreOwnedPlayerCamera(lp != null ? lp.transform : null);
        EventBus.RaiseGameResumed(); 
        if (_playCutsceneSignals) EventBus.RaiseCutSceneEnded();
        // EventBus listeners may run after the first restore and re-apply a
        // special camera preset. Make ThirdPerson the final state of completion.
        RestoreOwnedPlayerCamera(lp != null ? lp.transform : GetLocalPlayerTransform());
    }

    [ClientRpc]
    private void ExitBoardAfterDeathClientRpc()
    {
        ExitBoardAfterDeathLocal();
    }

    private void ExitBoardAfterDeathLocal()
    {
        StopBoardCoroutines();
        CleanupBoardImmediate();
        _lockedPositions.Clear();
        _lockedRotations.Clear();
        _canInput = false;
        _isReseting = false;
        _showDebugPath = false;
        _syncedPath = System.Array.Empty<Vector2Int>();
        ReleaseBoardCameraLease();

        ApplyIllusionPresentationLocal(true);
        SetBoardActivationColliderEnabled(false);

        var networkManager = NetworkManager.Singleton;
        var localPlayer = networkManager != null && networkManager.LocalClient != null
            ? networkManager.LocalClient.PlayerObject
            : null;
        if (localPlayer != null)
        {
            RestoreLocalPlayerMovement(localPlayer);

        }

        RestoreOwnedPlayerCamera(localPlayer != null ? localPlayer.transform : null);
        EventBus.RaiseGameResumed();
        RestoreOwnedPlayerCamera(localPlayer != null ? localPlayer.transform : GetLocalPlayerTransform());
    }

    private static void RestoreLocalPlayerMovement(NetworkObject player)
    {
        if (player == null) return;

        if (player.TryGetComponent<Rigidbody>(out var rigidbody))
        {
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            rigidbody.isKinematic = false;
            rigidbody.useGravity = true;
        }
        if (player.TryGetComponent<PlayerController>(out var controller))
            controller.SetExternalMovementOverride(false);
        if (player.TryGetComponent<PlayerInputHandler>(out var inputHandler))
            inputHandler.UnlockAllInput();
    }

    private void RestoreOwnedPlayerCamera(Transform playerTransform)
    {
        DestroyRuntimeErisCamera();
        playerTransform ??= GetLocalPlayerTransform();
        if (CameraManager.Instance == null) return;

        if (playerTransform != null)
        {
            Transform lookTarget = FindChildByName(playerTransform, "CameraLookTarget") ?? playerTransform;
            CameraManager.Instance.SetPlayerTarget(lookTarget, lookTarget);
        }

        CameraManager.Instance.SetGameplayCameraLocked(false);
        CameraManager.Instance.SwitchCamera(CameraPreset.ThirdPerson);
        CameraManager.Instance.RefreshLocalCameraInput();

        // CameraManager may have cached a stale special-camera preset while the
        // board was active. Explicitly re-enable the third-person input axis.
        CinemachineCamera thirdPerson = CameraManager.Instance.VcamThirdPerson;
        if (thirdPerson != null)
        {
            CinemachineInputAxisController axisController =
                thirdPerson.GetComponent<CinemachineInputAxisController>();
            if (axisController != null) axisController.enabled = true;
        }

        PlayerInputHandler inputHandler = GetLocalPlayerInputHandler();
        inputHandler?.UnlockAllInput();
        inputHandler?.EnableCameraLook();
    }

    private static Transform GetLocalPlayerTransform()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null
            && networkManager.LocalClient != null
            && networkManager.LocalClient.PlayerObject != null
            ? networkManager.LocalClient.PlayerObject.transform
            : null;
    }

    private static PlayerInputHandler GetLocalPlayerInputHandler()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.LocalClient == null || networkManager.LocalClient.PlayerObject == null)
            return null;

        return networkManager.LocalClient.PlayerObject.GetComponent<PlayerInputHandler>();
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null) return null;
        if (root.name == childName) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindChildByName(root.GetChild(i), childName);
            if (result != null) return result;
        }

        return null;
    }

    private Vector3 GetPieceWorldTarget(Vector2Int gridPos)
    {
        ErisTile tile = GetTileAt(gridPos);
        if (tile != null)
            return tile.transform.position + tile.transform.up * (0.5f * GetBoardScale());

        Transform boardAnchor = GetBoardAnchor();
        Vector3 boardPosition = boardAnchor.TransformPoint(GetTileLocalOffset(gridPos.x, gridPos.y));
        SampleTileSurface(boardPosition, boardAnchor, out Vector3 landingPosition, out Quaternion landingRotation, out _);
        return landingPosition + landingRotation * Vector3.up * (0.5f * GetBoardScale());
    }

    private Quaternion GetPieceWorldRotation(Vector2Int gridPos)
    {
        ErisTile tile = GetTileAt(gridPos);
        return tile != null ? tile.transform.rotation : GetBoardAnchor().rotation;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SwapRolesAndResetServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer || _sessionPhase.Value != ErisSessionPhase.Playing || !IsAuthorizedSessionPlayer(rpcParams.Receive.SenderClientId)) return;

        ulong previousController = _roleControllerId.Value;
        _roleControllerId.Value = _roleObserverId.Value;
        _roleObserverId.Value = previousController;
        _controllerId.Value = _roleControllerId.Value;
        _observerId.Value = _roleObserverId.Value;
        _isMemorizing.Value = true;
        _currentStepIndex.Value = 0;
        _syncedPath = GeneratePathArray();
        _pieceGridPos.Value = _syncedPath[0];
        SetupBoardClientRpc(_controllerId.Value, _observerId.Value, _syncedPath);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReplayPathServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer || _sessionPhase.Value != ErisSessionPhase.Playing || !IsAuthorizedSessionPlayer(rpcParams.Receive.SenderClientId)) return;
        _isMemorizing.Value = true;
        _currentStepIndex.Value = 0;
        StartCoroutine(ReplayPathRoutineServer());
    }

    private IEnumerator ReplayPathRoutineServer()
    {
        _pieceGridPos.Value = new Vector2Int(-1, -1);
        ReplayPathPresentationClientRpc();
        yield return null;
        _pieceGridPos.Value = _syncedPath[0];
    }

    [ClientRpc]
    private void ReplayPathPresentationClientRpc()
    {
        foreach (ErisTile tile in _spawnedTiles)
            tile?.ResetTile();
        if (NetworkManager.Singleton.LocalClientId == _observerId.Value)
            StartPathLoop();
        else if (NetworkManager.Singleton.LocalClientId == _controllerId.Value)
        {
            if (_observerId.Value == ulong.MaxValue)
            {
                if (BlackFogVFX != null) { BlackFogVFX.Stop(); BlackFogVFX.Clear(); }
                StartCoroutine(SoloPathRevealRoutine());
            }
            else
            {
                if (BlackFogVFX != null) BlackFogVFX.Play();
                if (ControllerWaitingSFX != null) _loopingSource = AudioManager.Instance.PlaySFXLoop(ControllerWaitingSFX);
            }
        }
    }

    private bool IsAuthorizedSessionPlayer(ulong clientId)
    {
        return clientId == _controllerId.Value || clientId == _observerId.Value;
    }

    private void SwitchBoardCamera(int direction)
    {
        if (!_boardCameraLeaseActive) return;
        _cameraDirection = (_cameraDirection + direction + 3) % 3;
        ApplyBoardCameraDirection();
    }

    private void SetBoardCameraDirection(int direction)
    {
        if (!_boardCameraLeaseActive) return;
        _cameraDirection = (direction % 3 + 3) % 3;
        ApplyBoardCameraDirection();
    }

    private void ApplyBoardCameraDirection()
    {
        Transform boardAnchor = GetBoardAnchor();
        Vector3 center = GetBoardWorldCenter();
        Transform marker = GetBoardCameraMarker(_cameraDirection);
        if (marker != null)
        {
            EnsureRuntimeErisCamera();
            CinemachineCamera selectedCamera = marker.GetComponent<CinemachineCamera>();
            if (selectedCamera != null)
            {
                SetErisCameraPriorities(selectedCamera);
                selectedCamera.transform.SetPositionAndRotation(marker.position, marker.rotation);
                _runtimeErisCamera = selectedCamera;
                _ownsRuntimeErisCamera = false;
                ConfigureErisCameraLens(selectedCamera);
            }
            return;
        }

        float distance = Mathf.Max(GetTileSpacing() * 8f, 12f);
        Vector3 boardRight = GetBoardRight(boardAnchor);
        Vector3 worldUp = Vector3.up;
        Vector3 localDirection = _cameraDirection switch
        {
            0 => (-boardRight + worldUp * 0.85f).normalized,
            1 => worldUp,
            _ => (boardRight + worldUp * 0.85f).normalized
        };
        Vector3 cameraPosition = center + localDirection * distance;
        Vector3 upDirection = _cameraDirection == 1 ? GetBoardForward(boardAnchor) : worldUp;
        Quaternion cameraRotation = Quaternion.LookRotation(center - cameraPosition, upDirection);
        ApplyCameraPoseToActiveCameras(cameraPosition, cameraRotation);
    }

    private Transform GetBoardCameraMarker(int direction)
    {
        return direction switch
        {
            0 => _boardCameraLeftMarker,
            1 => _boardCameraTopMarker,
            _ => _boardCameraRightMarker
        };
    }

    private void ApplyCameraPoseToActiveCameras(Vector3 position, Quaternion rotation)
    {
        foreach (CinemachineCamera camera in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
        {
            if (!camera.isActiveAndEnabled || camera.Priority.Value <= 0) continue;
            camera.Target.TrackingTarget = null;
            camera.Target.LookAtTarget = null;
            camera.transform.SetPositionAndRotation(position, rotation);
            var lens = camera.Lens;
            lens.FieldOfView = CameraFOV;
            lens.NearClipPlane = 0.05f;
            lens.FarClipPlane = Mathf.Max(lens.FarClipPlane, 250f);
            camera.Lens = lens;
        }
    }

    [ClientRpc]
    private void FreezeLocalPlayersClientRpc()
    {
        if (NetworkManager.Singleton == null
            || NetworkManager.Singleton.LocalClient == null
            || NetworkManager.Singleton.LocalClient.PlayerObject == null)
        {
            return;
        }

        NetworkObject player = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (player.TryGetComponent<Rigidbody>(out Rigidbody rigidbody))
        {
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            rigidbody.isKinematic = false;
            rigidbody.useGravity = true;
        }
        if (player.TryGetComponent<PlayerController>(out var controller))
            controller.SetExternalMovementOverride(true);
        if (player.TryGetComponent<PlayerInputHandler>(out var inputHandler))
            inputHandler.LockAllInput();

        if (player.TryGetComponent<PlayerStateMachine>(out PlayerStateMachine stateMachine))
        {
            stateMachine.TransitionTo(PlayerStateType.Idle);
        }
    }

    [ClientRpc]
    private void ApplyIllusionPresentationClientRpc(bool restoreGround)
    {
        ApplyIllusionPresentationLocal(restoreGround);
    }

    private void ApplyIllusionPresentationLocal(bool restoreGround)
    {
        if (restoreGround)
        {
            foreach (KeyValuePair<GameObject, bool> entry in _illusionGroundStates)
            {
                if (entry.Key != null) entry.Key.SetActive(entry.Value);
            }
            _illusionGroundStates.Clear();
            _illusionGroundMaterials.Clear();
            _illusionGroundBounds.Clear();
        }
        else
        {
            foreach (GameObject groundObject in ResolveIllusionGroundObjects())
            {
                if (groundObject == null || _illusionGroundStates.ContainsKey(groundObject)) continue;
                _illusionGroundStates.Add(groundObject, groundObject.activeSelf);
                Renderer renderer = groundObject.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    if (renderer.sharedMaterial != null)
                        _illusionGroundMaterials[groundObject] = renderer.sharedMaterial;
                    _illusionGroundBounds[groundObject] = renderer.bounds;
                }
                groundObject.SetActive(false);
            }
        }

        if (BlackFogVFX != null)
        {
            if (restoreGround)
            {
                BlackFogVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            else
            {
                BlackFogVFX.Play(true);
            }
        }

        if (_playCutsceneSignals)
        {
            if (restoreGround) EventBus.RaiseCutSceneEnded();
            else EventBus.RaiseCutSceneStarted();
        }
    }

    private IEnumerable<GameObject> ResolveIllusionGroundObjects()
    {
        bool hasAssignedObject = false;
        if (_illusionGroundObjects != null)
        {
            foreach (GameObject groundObject in _illusionGroundObjects)
            {
                if (groundObject == null) continue;
                hasAssignedObject = true;
                yield return groundObject;
            }
        }

        if (hasAssignedObject || !_autoDetectIllusionGround) yield break;

        Vector3 boardCenter = GetBoardWorldCenter();
        float radius = Mathf.Max(_illusionGroundSearchRadius, GetTileSpacing() * 5.5f);
        float maxObjectSize = radius * 2.5f;
        foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (renderer == null
                || renderer is ParticleSystemRenderer
                || renderer.transform.IsChildOf(transform)
                || renderer.GetComponentInParent<NetworkObject>() != null
                || renderer.GetComponentInParent<ErisTile>() != null)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            Vector3 flatOffset = new Vector3(bounds.center.x - boardCenter.x, 0f, bounds.center.z - boardCenter.z);
            if (flatOffset.sqrMagnitude > radius * radius
                || bounds.max.y > boardCenter.y + 0.35f
                || bounds.size.x > maxObjectSize
                || bounds.size.z > maxObjectSize)
            {
                continue;
            }

            yield return renderer.gameObject;
        }
    }

    [ClientRpc]
    private void SetBoardActivationColliderClientRpc(bool enabled)
    {
        SetBoardActivationColliderEnabled(enabled);
    }

    private void SetBoardActivationColliderEnabled(bool enabled)
    {
        if (_boardActivationCollider != null) _boardActivationCollider.enabled = enabled;
    }

    private bool TryGetPlayerObject(ulong clientId, out NetworkObject playerObject)
    {
        playerObject = null;

        if (NetworkManager.Singleton == null
            || !NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
            || client.PlayerObject == null)
        {
            return false;
        }

        playerObject = client.PlayerObject;
        return true;
    }

    private Transform GetBoardAnchor()
    {
        return BoardAnchor != null ? BoardAnchor : transform;
    }

    private Vector3 GetBoardWorldCenter()
    {
        Vector2Int[] centerCells =
        {
            new Vector2Int(4, 5), // 45
            new Vector2Int(4, 6), // 46
            new Vector2Int(5, 5), // 55
            new Vector2Int(5, 6)  // 56
        };
        Vector3 total = Vector3.zero;
        int count = 0;
        foreach (Vector2Int cell in centerCells)
        {
            ErisTile tile = GetTileAt(cell);
            if (tile == null) continue;
            total += tile.transform.position;
            count++;
        }

        if (count > 0) return total / count;

        Transform boardAnchor = GetBoardAnchor();
        return _boardAnchorIsCenter
            ? boardAnchor.position
            : boardAnchor.TransformPoint(new Vector3(GetTileSpacing() * 4.5f, 0f, GetTileSpacing() * 5.5f));
    }

    /// <summary>
    /// BoardAnchor can be either the first-cell origin (legacy scenes) or the
    /// fixed world-space centre of cells 45, 46, 55 and 56 (Map2).
    /// </summary>
    private Vector3 GetTileLocalOffset(int x, int y)
    {
        if (!_boardAnchorIsCenter)
            return new Vector3(x * GetTileSpacing(), 0f, y * GetTileSpacing());

        return new Vector3(
            (x - 4.5f) * GetTileSpacing(),
            0f,
            (y - 5.5f) * GetTileSpacing());
    }

    private static Vector3 GetBoardRight(Transform boardAnchor)
    {
        Vector3 right = Vector3.ProjectOnPlane(boardAnchor.right, Vector3.up);
        return right.sqrMagnitude > 0.001f ? right.normalized : Vector3.right;
    }

    private static Vector3 GetBoardForward(Transform boardAnchor)
    {
        Vector3 forward = Vector3.ProjectOnPlane(boardAnchor.forward, Vector3.up);
        return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
    }

    private float GetBoardScale()
    {
        return Mathf.Max(0.01f, _boardScale);
    }

    private float GetTileSpacing()
    {
        // Scale the board footprint, then keep a small safety gap between visuals.
        // With the default tile (width 1), scale 0.5 and spacing 1.3 produce:
        // 0.5 tile width + 0.25 gap = 0.75 center spacing.
        return Mathf.Max(0.01f, _baseTileSpacing * GetBoardScale() + _minimumTileGap);
    }

    private ErisTile GetTileAt(Vector2Int pos) => _spawnedTiles.Find(t => t != null && t.GridPos == pos);

    private void StopBoardCoroutines()
    {
        if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);
        if (_pathLoopCoroutine != null) StopCoroutine(_pathLoopCoroutine);
        if (_spawnCoroutine != null) StopCoroutine(_spawnCoroutine);
        if (_idleWaveCoroutine != null) StopCoroutine(_idleWaveCoroutine);
        if (_illusionRoutine != null) StopCoroutine(_illusionRoutine);
        if (_countdownRoutine != null) StopCoroutine(_countdownRoutine);

        _moveCoroutine = null;
        _pathLoopCoroutine = null;
        _spawnCoroutine = null;
        _idleWaveCoroutine = null;
        _illusionRoutine = null;
        _countdownRoutine = null;

        if (_loopingSource != null)
        {
            try { AudioManager.Instance.StopSFX(_loopingSource); } catch { }
            _loopingSource = null;
        }
    }

    private void CleanupBoardImmediate()
    {
        StopBoardCoroutines();
        foreach (var tile in _spawnedTiles)
        {
            if (tile != null) Destroy(tile.gameObject);
        }
        _spawnedTiles.Clear();

        if (_spawnedPieceInstance != null) Destroy(_spawnedPieceInstance);
        _spawnedPieceInstance = null;

        ErisTile[] existingTiles = GetComponentsInChildren<ErisTile>();
        foreach (var tile in existingTiles)
        {
            if (tile != null) Destroy(tile.gameObject);
        }
    }
}
