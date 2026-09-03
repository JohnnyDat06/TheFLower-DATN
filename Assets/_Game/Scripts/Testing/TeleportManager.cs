using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine.InputSystem;

namespace Game.Testing
{
    [System.Serializable]
    public struct TeleportPoint
    {
        public string Name;
        public Transform Point;
    }

    /// <summary>
    /// TeleportManager - Chức năng hỗ trợ test game.
    /// Cho phép dịch chuyển Player đến các vị trí được đánh dấu sẵn.
    /// </summary>
    public class TeleportManager : MonoBehaviour
    {
        public static TeleportManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private List<TeleportPoint> _teleportPoints = new List<TeleportPoint>();
        
        [Header("UI References")]
        [SerializeField] private GameObject _uiRoot;
        [SerializeField] private TMP_InputField _idInputField;
        [SerializeField] private TextMeshProUGUI _pointsListText;

        private bool _isUIVisible = false;
        private CursorLockMode _cursorLockStateBeforeUI;
        private bool _cursorVisibleBeforeUI;
        private bool _hasSavedCursorState;

        private bool _managesPlayerInput = true;
        private int _selectedPointIndex;
        private Action _onClosed;
        private bool _skipGamepadInputFrame;
        private readonly Dictionary<PlayerInputHandler, bool> _cameraLookStates = new Dictionary<PlayerInputHandler, bool>();

        public bool IsUIVisible => _isUIVisible;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (_uiRoot != null)
                _uiRoot.SetActive(false);

            UpdatePointsListUI();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            UICursorLockService.Release(this);
        }

        private void Update()
        {
            // Kiểm tra phím Tab từ Input System mới
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                ToggleUI();
            }

            if (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame)
            {
                ToggleUI();
            }

            // Nếu UI đang hiện và nhấn Enter
            if (_isUIVisible && Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            {
                OnTeleportRequested();
            }

            if (_isUIVisible && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                HideUI();
            }

            if (_isUIVisible)
            {
                // The pause menu can open this panel from the same gamepad A/Cross
                // press that would otherwise be interpreted as "teleport" below.
                // Consume that frame so opening the panel never teleports to index 0.
                if (_skipGamepadInputFrame)
                {
                    _skipGamepadInputFrame = false;
                    return;
                }

                HandleGamepadUIInput();
            }
        }

        public void ToggleUI()
        {
            SetUIVisible(!_isUIVisible);
        }

        public void ShowUI(Action onClosed = null, bool managePlayerInput = true)
        {
            _onClosed = onClosed;
            _managesPlayerInput = managePlayerInput;
            SetUIVisible(true);
        }

        public void HideUI()
        {
            SetUIVisible(false);
        }

        private void SetUIVisible(bool visible)
        {
            if (_isUIVisible == visible) return;

            _isUIVisible = visible;

            if (_uiRoot != null)
                _uiRoot.SetActive(_isUIVisible);

            if (_isUIVisible)
            {
                // Save the exact gameplay cursor state before the modal UI takes it.
                // This keeps Tab behaviour identical to the state the player started in.
                if (_managesPlayerInput)
                {
                    _cursorLockStateBeforeUI = Cursor.lockState;
                    _cursorVisibleBeforeUI = Cursor.visible;
                    _hasSavedCursorState = true;
                }

                _selectedPointIndex = Mathf.Clamp(_selectedPointIndex, 0, Mathf.Max(0, _teleportPoints.Count - 1));
                UpdatePointsListUI();
                SyncSelectedPointToInputField();
                if (IsCurrentDeviceGamepad())
                    _idInputField?.DeactivateInputField();
                else
                    _idInputField?.ActivateInputField();
                if (_managesPlayerInput)
                    LockPlayerInput(true);

                _skipGamepadInputFrame = Gamepad.current != null;
                UICursorLockService.Request(this);
            }
            else
            {
                _skipGamepadInputFrame = false;
                bool restoreCursor = _managesPlayerInput && _hasSavedCursorState;
                if (_managesPlayerInput)
                    LockPlayerInput(false);

                _managesPlayerInput = true;
                UICursorLockService.Release(this);

                if (restoreCursor)
                    StartCoroutine(RestoreSavedCursorState());

                var onClosed = _onClosed;
                _onClosed = null;
                onClosed?.Invoke();
            }
        }


        private IEnumerator RestoreSavedCursorState()
        {
            // UGUI/Input System can apply its pointer state one frame after the panel closes.
            yield return null;

            if (_hasSavedCursorState)
            {
                Cursor.lockState = _cursorLockStateBeforeUI;
                Cursor.visible = _cursorVisibleBeforeUI;
                _hasSavedCursorState = false;
            }
        }


        private void UpdatePointsListUI()
        {
            if (_pointsListText == null) return;

            string listStr = "<b>Teleport Points:</b>\n";
            for (int i = 0; i < _teleportPoints.Count; i++)
            {
                bool selected = i == _selectedPointIndex;
                string marker = selected ? ">" : " ";
                string colorOpen = selected ? "<color=#f2e2a8>" : "<color=#d0c7aa>";
                listStr += $"{colorOpen}{marker} [{i}] {_teleportPoints[i].Name}</color>\n";
            }

            listStr += IsCurrentDeviceGamepad()
                ? "\n<size=80%><color=#9fd58b><sprite name=\"xa\"> Teleport</color>   <color=#e48a7e><sprite name=\"xb\"> Back</color>   <sprite name=\"dpad\"> / <sprite name=\"jl\"> Select</size>"
                : "\n<size=80%><sprite name=\"enter\"> Teleport   <sprite name=\"esc\"> Back</size>";

            _pointsListText.text = listStr;
        }

        private void HandleGamepadUIInput()
        {
            var gamepad = Gamepad.current;
            if (gamepad == null) return;

            if (gamepad.dpad.up.wasPressedThisFrame || gamepad.leftStick.up.wasPressedThisFrame)
            {
                MoveSelection(-1);
            }
            else if (gamepad.dpad.down.wasPressedThisFrame || gamepad.leftStick.down.wasPressedThisFrame)
            {
                MoveSelection(1);
            }

            if (gamepad.buttonSouth.wasPressedThisFrame)
                OnTeleportRequested();
            else if (gamepad.buttonEast.wasPressedThisFrame)
                HideUI();
        }

        private void MoveSelection(int direction)
        {
            if (_teleportPoints.Count == 0) return;

            _selectedPointIndex = (_selectedPointIndex + direction + _teleportPoints.Count) % _teleportPoints.Count;
            SyncSelectedPointToInputField();
            UpdatePointsListUI();
        }

        private void SyncSelectedPointToInputField()
        {
            if (_idInputField == null) return;
            _idInputField.SetTextWithoutNotify(_selectedPointIndex.ToString());
        }

        public void OnTeleportRequested()
        {
            if (_idInputField != null && int.TryParse(_idInputField.text, out int id))
            {
                TeleportToPoint(id);
            }
            else if (_idInputField == null)
            {
                Debug.LogError("[TeleportManager] Teleport ID input field is not assigned.");
            }
            HideUI();
        }

        private void TeleportToPoint(int id)
        {
            if (id < 0 || id >= _teleportPoints.Count)
            {
                Debug.LogWarning($"[TeleportManager] ID {id} không hợp lệ.");
                return;
            }

            Transform target = _teleportPoints[id].Point;
            if (target == null)
            {
                Debug.LogWarning($"[TeleportManager] Point tại ID {id} bị null.");
                return;
            }

            // Tìm Player của máy local
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            {
                Debug.LogError("[TeleportManager] NetworkManager chưa sẵn sàng.");
                return;
            }

            NetworkObject playerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (playerObject == null)
            {
                Debug.LogError("[TeleportManager] Không tìm thấy Local PlayerObject.");
                return;
            }

            if (!playerObject.TryGetComponent<NGOPlayerSync>(out var playerSync))
            {
                Debug.LogError("[TeleportManager] Local PlayerObject is missing NGOPlayerSync.");
                return;
            }

            // Keep fast travel inside the same authoritative teleport transaction
            // used by spawn and respawn. Directly moving the local transform lets
            // ClientNetworkTransform/physics overwrite the pose after a restart.
            playerSync.RequestTeleport(target.position, target.rotation);

            Debug.Log($"[TeleportManager] Đã dịch chuyển đến: {_teleportPoints[id].Name}");
        }

        private void LockPlayerInput(bool isLocked)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;

            NetworkObject playerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (playerObject != null && playerObject.TryGetComponent<PlayerInputHandler>(out var handler))
            {
                if (isLocked)
                {
                    if (!_cameraLookStates.ContainsKey(handler))
                        _cameraLookStates.Add(handler, handler.CameraLookEnabled);
                    handler.LockAllInput();
                    handler.DisableCameraLook();
                }
                else
                {
                    handler.UnlockAllInput();
                    if (_cameraLookStates.TryGetValue(handler, out bool wasEnabled) && wasEnabled)
                        handler.EnableCameraLook();
                    else
                        handler.DisableCameraLook();

                    _cameraLookStates.Remove(handler);
                }
            }
        }

        private static bool IsCurrentDeviceGamepad()
        {
            return InputDeviceDetector.Instance != null
                && InputDeviceDetector.Instance.CurrentDeviceType == InputDeviceType.Gamepad;
        }
    }
}
