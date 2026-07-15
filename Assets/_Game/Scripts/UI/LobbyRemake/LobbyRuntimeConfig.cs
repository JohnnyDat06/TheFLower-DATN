using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI.LobbyAuto
{
    [CreateAssetMenu(fileName = "LobbyRuntimeConfig", menuName = "CoopGame/UI/Lobby Runtime Config")]
    public sealed class LobbyRuntimeConfig : ScriptableObject
    {
        public SOAudioClip HoverSfx;
        public SOAudioClip ClickSfx;
        public AudioClip LobbyMusic;
        public Sprite HostPortrait;
        public Sprite ClientPortrait;
        public Sprite CreateRoomButton;
        public Sprite JoinRoomButton;
        public Sprite LobbyLogo;
        public Sprite StartButton;
        public Sprite SettingsButton;
        public Sprite BackButton;
        public Sprite CreateButton;
        public Sprite CancelButton;
        public Sprite RoomReadyButton;
        public Sprite RoomStartButton;
        public Sprite RoomWaitingButton;
        public Sprite RoomLeaveButton;
        public Sprite RoomJoinButton;
        public Sprite RoomRefreshButton;
        public Sprite KeyBindingsButton;
        public TMP_FontAsset HeadingFont;
        public PanelSettings InputPanelSettings;
        public VisualTreeAsset InputSettingsUxml;
        public InputIconMap InputIconMap;
        public InputActionAsset InputActions;
    }
}
