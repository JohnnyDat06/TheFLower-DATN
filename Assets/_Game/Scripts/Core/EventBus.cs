using System;
using UnityEngine;

public enum Level04Phase : byte
{
    IntroPeak,
    WingUnlock,
    TakeOff,
    CloudDescent,
    CloudCorridor,
    GalaxyGate,
    TimeWarpAscent,
    StarfallReturn,
    TerrainReveal,
    EndTransition
}

/// <summary>
/// EventBus — Static class chứa toàn bộ C# Action của dự án.
/// Là trung tâm giao tiếp giữa mọi module.
/// Không module nào được gọi trực tiếp module khác nếu có thể dùng EventBus.
/// SRS §13.2
/// </summary>
public static class EventBus
{
    public static event Action<int, string> OnQuestStepChanged;
    public static event Action<int, string> OnQuestStepCompleted;
    public static event Action OnQuestRouteCompleted;

    public static void RaiseQuestStepChanged(int index, string stepId) => OnQuestStepChanged?.Invoke(index, stepId);
    public static void RaiseQuestStepCompleted(int index, string stepId) => OnQuestStepCompleted?.Invoke(index, stepId);
    public static void RaiseQuestRouteCompleted() => OnQuestRouteCompleted?.Invoke();

    // ─── Player ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Publisher: PlayerHealth | Subscriber: RespawnManager, HUDController
    /// </summary>
    public static event Action<ulong> OnPlayerDied;

    /// <summary>
    /// Publisher: RespawnManager | Subscriber: PlayerController, HUDController, CameraManager
    /// </summary>
    public static event Action<ulong, Vector3> OnPlayerRespawned;

    // ─── Level ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Publisher: LevelGoal | Subscriber: SceneLoader, CloudSaveManager
    /// </summary>
    public static event Action<int> OnLevelCompleted;

    /// <summary>
    /// Publisher: CheckpointTrigger | Subscriber: CheckpointManager, CloudSaveManager
    /// </summary>
    public static event Action<string, Vector3, Vector3> OnCheckpointReached;

    public static event Action<Level04Phase> OnLevel04PhaseChanged;

    public static event Action<string, ulong, bool> OnLevel04RingActivated;

    public static event Action<string, ulong> OnLevel04MemoryShardCollected;

    // ─── Interactable ─────────────────────────────────────────────────────────

    /// <summary>
    /// Publisher: InteractableBase | Subscriber: Door, Platform, any receiver
    /// </summary>
    public static event Action<string> OnInteractableActivated;

    /// <summary>
    /// Publisher: CoopInteractable | Subscriber: PromptUIManager
    /// </summary>
    public static event Action<string, ulong> OnCoopInteractablePlayerReady;

    /// <summary>
    /// Publisher: CoopInteractable | Subscriber: PromptUIManager
    /// </summary>
    public static event Action<string> OnCoopInteractableReset;

    // ─── Game State ───────────────────────────────────────────────────────────

    /// <summary>
    /// Publisher: PauseManager | Subscriber: UIManager, Network
    /// </summary>
    public static event Action OnGamePaused;

    /// <summary>
    /// Publisher: PauseManager | Subscriber: UIManager, Network
    /// </summary>
    public static event Action OnGameResumed;

    // ─── Settings ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Publisher: SettingsManager | Subscriber: AudioMixer, ScreenShakeController
    /// </summary>
    public static event Action OnSettingsChanged;

    /// <summary>
    /// Publisher: AccessibilitySettingsService | Subscriber: ScreenShakeController, PromptUI
    /// </summary>
    public static event Action OnAccessibilityChanged;

    // ─── Network / Lobby ──────────────────────────────────────────────────────

    /// <summary>
    /// Publisher: LobbyManager | Subscriber: SceneLoader
    /// </summary>
    public static event Action OnAllPlayersReady;

    /// <summary>
    /// Publisher: NGO NetworkManager | Subscriber: GameFlowManager
    /// </summary>
    public static event Action<ulong> OnClientConnected;

    /// <summary>
    /// Publisher: NGO NetworkManager | Subscriber: GameFlowManager
    /// </summary>
    public static event Action<ulong> OnClientDisconnected;

    // ─── Camera ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Publisher: CameraZoneTrigger, CutSceneManager | Subscriber: CameraManager
    /// </summary>
    public static event Action<CameraPreset> OnCameraPresetChanged;

    /// <summary>
    /// Publisher: CameraSettingsService | Subscriber: VCam
    /// </summary>
    public static event Action OnCameraSettingsChanged;

    // ─── FX ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Publisher: PlayerController, Environment | Subscriber: ScreenShakeController
    /// </summary>
    public static event Action<SOScreenShakeConfig> OnScreenShakeRequested;

    // ─── CutScene ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Publisher: CutSceneManager | Subscriber: PlayerInputHandler
    /// </summary>
    public static event Action OnCutSceneStarted;

    /// <summary>
    /// Publisher: CutSceneManager | Subscriber: PlayerInputHandler
    /// </summary>
    public static event Action OnCutSceneEnded;

    // ─── Input ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Publisher: InputRebindService | Subscriber: PlayerInputHandler, PromptUIManager
    /// </summary>
    public static event Action OnInputBindingChanged;

    /// <summary>
    /// Publisher: InputDeviceDetector | Subscriber: InteractPromptHUD, InputSettingsPanelController, CameraManager
    /// </summary>
    public static event Action<InputDeviceType> OnInputDeviceChanged;

    // ─── Raise Methods ────────────────────────────────────────────────────────

    /// <summary>PlayerHealth raises this khi player chết.</summary>
    public static void RaisePlayerDied(ulong clientId)
        => OnPlayerDied?.Invoke(clientId);

    /// <summary>RespawnManager raises this khi player hồi sinh.</summary>
    public static void RaisePlayerRespawned(ulong clientId, Vector3 spawnPosition)
        => OnPlayerRespawned?.Invoke(clientId, spawnPosition);

    /// <summary>LevelGoal raises this khi hoàn thành màn.</summary>
    public static void RaiseLevelCompleted(int levelIndex)
        => OnLevelCompleted?.Invoke(levelIndex);

    /// <summary>
    /// CheckpointTrigger raises this khi chạm checkpoint.
    /// Returns false when no checkpoint system is ready to receive the event yet.
    /// </summary>
    public static bool RaiseCheckpointReached(string checkpointId, Vector3 hostSpawnPos, Vector3 clientSpawnPos)
    {
        Action<string, Vector3, Vector3> handlers = OnCheckpointReached;
        if (handlers == null) return false;

        handlers.Invoke(checkpointId, hostSpawnPos, clientSpawnPos);
        return true;
    }

    public static void RaiseLevel04PhaseChanged(Level04Phase phase)
        => OnLevel04PhaseChanged?.Invoke(phase);

    public static void RaiseLevel04RingActivated(string ringId, ulong clientId, bool cooperative)
        => OnLevel04RingActivated?.Invoke(ringId, clientId, cooperative);

    public static void RaiseLevel04MemoryShardCollected(string shardId, ulong clientId)
        => OnLevel04MemoryShardCollected?.Invoke(shardId, clientId);

    /// <summary>InteractableBase raises this khi được kích hoạt.</summary>
    public static void RaiseInteractableActivated(string interactableId)
        => OnInteractableActivated?.Invoke(interactableId);

    /// <summary>CoopInteractable raises this khi một player đã sẵn sàng.</summary>
    public static void RaiseCoopInteractablePlayerReady(string interactableId, ulong clientId)
        => OnCoopInteractablePlayerReady?.Invoke(interactableId, clientId);

    /// <summary>CoopInteractable raises this khi reset trạng thái.</summary>
    public static void RaiseCoopInteractableReset(string interactableId)
        => OnCoopInteractableReset?.Invoke(interactableId);

    /// <summary>PauseManager raises this khi game bị pause.</summary>
    public static void RaiseGamePaused()
        => OnGamePaused?.Invoke();

    /// <summary>PauseManager raises this khi game được resume.</summary>
    public static void RaiseGameResumed()
        => OnGameResumed?.Invoke();

    /// <summary>SettingsManager raises this khi settings thay đổi.</summary>
    public static void RaiseSettingsChanged()
        => OnSettingsChanged?.Invoke();

    /// <summary>AccessibilitySettingsService raises this khi accessibility thay đổi.</summary>
    public static void RaiseAccessibilityChanged()
        => OnAccessibilityChanged?.Invoke();

    /// <summary>LobbyManager raises this khi cả 2 player đã sẵn sàng.</summary>
    public static void RaiseAllPlayersReady()
        => OnAllPlayersReady?.Invoke();

    /// <summary>NetworkManagerWrapper raises this khi client kết nối.</summary>
    public static void RaiseClientConnected(ulong clientId)
        => OnClientConnected?.Invoke(clientId);

    /// <summary>NetworkManagerWrapper raises this khi client ngắt kết nối.</summary>
    public static void RaiseClientDisconnected(ulong clientId)
        => OnClientDisconnected?.Invoke(clientId);

    /// <summary>CameraZoneTrigger/CutSceneManager raises này để đổi camera preset.</summary>
    public static void RaiseCameraPresetChanged(CameraPreset preset)
        => OnCameraPresetChanged?.Invoke(preset);

    /// <summary>CameraSettingsService raises this khi camera settings thay đổi.</summary>
    public static void RaiseCameraSettingsChanged()
        => OnCameraSettingsChanged?.Invoke();

    /// <summary>PlayerController/Environment raises this để yêu cầu screen shake.</summary>
    public static void RaiseScreenShakeRequested(SOScreenShakeConfig shakeConfig)
        => OnScreenShakeRequested?.Invoke(shakeConfig);

    /// <summary>CutSceneManager raises this khi cutscene bắt đầu.</summary>
    public static void RaiseCutSceneStarted()
        => OnCutSceneStarted?.Invoke();

    /// <summary>CutSceneManager raises this khi cutscene kết thúc.</summary>
    public static void RaiseCutSceneEnded()
        => OnCutSceneEnded?.Invoke();

    /// <summary>InputRebindService raises này khi input binding thay đổi.</summary>
    public static void RaiseInputBindingChanged()
        => OnInputBindingChanged?.Invoke();

    /// <summary>InputDeviceDetector raises này khi device type thay đổi (KB ↔ Gamepad).</summary>
    public static void RaiseInputDeviceChanged(InputDeviceType deviceType)
        => OnInputDeviceChanged?.Invoke(deviceType);
}
