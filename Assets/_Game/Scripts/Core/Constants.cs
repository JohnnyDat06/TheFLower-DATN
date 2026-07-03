/// <summary>
/// Constants — Tập trung toàn bộ string và số constant của dự án.
/// Không có magic number hay hardcode string nào trong codebase ngoài file này.
/// SRS §12.3
/// </summary>
public static class Constants
{
    /// <summary>Tên các scene trong Build Settings.</summary>
    public static class Scenes
    {
        public const string MAIN_MENU = "MainMenu";
        public const string LOBBY     = "Lobby";
        public const string LEVEL_01  = "Level_01";
        public const string LEVEL_02  = "Level_02";
        public const string LEVEL_03  = "Level_03";
        public const string LEVEL_04  = "Map4_Flying";
    }

    /// <summary>Keys dùng với PlayerPrefs.</summary>
    public static class PlayerPrefsKeys
    {
        // Camera
        public const string CAM_SENSITIVITY_X = "cam_sensitivityX";
        public const string CAM_SENSITIVITY_Y = "cam_sensitivityY";
        public const string CAM_INVERT_Y      = "cam_invertY";
        public const string CAM_DISTANCE      = "cam_distance";

        // Audio
        public const string BGM_VOLUME        = "bgm_volume";
        public const string SFX_VOLUME        = "sfx_volume";

        // Accessibility
        public const string ACCESSIBILITY_CAMERA_SHAKE = "accessibility_cameraShake";
        public const string ACCESSIBILITY_PROMPT_SIZE  = "accessibility_promptUISize";

        // Input
        public const string INPUT_BINDINGS             = "inputBindings"; // Legacy — migrated automatically
        public const string INPUT_BINDINGS_KEYBOARD    = "inputBindings_keyboard";
        public const string INPUT_BINDINGS_GAMEPAD     = "inputBindings_gamepad";
        public const string INPUT_PREFERRED_DEVICE     = "inputPreferredDevice";

        // Auth (T0-4)
        public const string PLAYER_ID         = "playerId";
    }

    /// <summary>Keys dùng với Unity Cloud Save.</summary>
    public static class CloudSaveKeys
    {
        public const string LEVEL_INDEX   = "levelIndex";
        public const string CHECKPOINT_ID = "checkpointId";
        public const string PLAYTIME      = "playtime";
    }

    /// <summary>GameObject Tags trong Unity.</summary>
    public static class Tags
    {
        public const string PLAYER       = "Player";
        public const string CHECKPOINT   = "Checkpoint";
        public const string DEATH_ZONE   = "DeathZone";
        public const string INTERACTABLE = "Interactable";
    }

    /// <summary>Layer names trong Unity.</summary>
    public static class Layers
    {
        public const string ENVIRONMENT  = "Environment";
        public const string PLAYER       = "Player";
        public const string INTERACTABLE = "Interactable";
    }

    /// <summary>Các hằng số gameplay.</summary>
    public static class Gameplay
    {
        public const float RESPAWN_COUNTDOWN       = 10f;
        public const float RESPAWN_REDUCE_AMOUNT   = 0.5f;
        public const float RESPAWN_REDUCE_COOLDOWN = 0.1f;
        public const int   MAX_RELAY_PLAYERS       = 2;
        public const int   RELAY_JOINCODE_LENGTH   = 6;
        public const float DISCONNECT_TIMEOUT      = 30f;
        public const float LOBBY_COUNTDOWN         = 3f;
        public const int   CLOUD_SAVE_RETRY_COUNT  = 3;
        public const float CUTSCENE_SKIP_DELAY     = 2f;
    }

    /// <summary>Hằng số camera.</summary>
    public static class Camera
    {
        public const float DEFAULT_SENSITIVITY_X = 5f;
        public const float DEFAULT_SENSITIVITY_Y = 5f;
        public const float MIN_SENSITIVITY       = 0.01f;
        public const float MAX_SENSITIVITY       = 100f; // Tăng giới hạn lên cao để test
        public const float MIN_ARM_LENGTH        = 3f;
        public const float MAX_ARM_LENGTH        = 7f;
        public const float DEFAULT_ARM_LENGTH    = 5f;
        public const float MIN_PITCH             = -30f;
        public const float MAX_PITCH             = 60f;
    }
}
