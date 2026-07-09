using System.Collections;
using System.Collections.Generic;
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
        private int _selectedPointIndex;

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

            if (_isUIVisible)
            {
                HandleGamepadUIInput();
            }
        }

        public void ToggleUI()
        {
            _isUIVisible = !_isUIVisible;
            
            if (_uiRoot != null)
                _uiRoot.SetActive(_isUIVisible);

            if (_isUIVisible)
            {
                _selectedPointIndex = Mathf.Clamp(_selectedPointIndex, 0, Mathf.Max(0, _teleportPoints.Count - 1));
                UpdatePointsListUI();
                SyncSelectedPointToInputField();
                _idInputField.ActivateInputField();
                LockPlayerInput(true);
                
                // Giải phóng chuột
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                LockPlayerInput(false);
                
                // Khóa lại chuột (giả định game góc nhìn thứ 3)
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void UpdatePointsListUI()
        {
            if (_pointsListText == null) return;

            string listStr = "<b>Teleport Points:</b>\n";
            for (int i = 0; i < _teleportPoints.Count; i++)
            {
                string marker = i == _selectedPointIndex ? ">" : " ";
                listStr += $"{marker} [{i}] {_teleportPoints[i].Name}\n";
            }
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
                ToggleUI();
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
            if (int.TryParse(_idInputField.text, out int id))
            {
                TeleportToPoint(id);
            }
            ToggleUI();
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

            // ─── THỰC HIỆN DỊCH CHUYỂN AN TOÀN ───
            
            // 1. Tạm thời tắt Rigidbody Interpolation để tránh rubber banding
            bool hasRigidbody = playerObject.TryGetComponent<Rigidbody>(out var rb);
            RigidbodyInterpolation originalInterpolation = RigidbodyInterpolation.None;
            if (hasRigidbody)
            {
                originalInterpolation = rb.interpolation;
                rb.interpolation = RigidbodyInterpolation.None;
                
                // Đưa Rigidbody về trạng thái tĩnh ngay lập tức
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true; // Tạm khóa vật lý
            }

            // 2. Cập nhật vị trí transform
            playerObject.transform.position = target.position;
            playerObject.transform.rotation = target.rotation;

            // 3. Thông báo cho NetworkTransform thực hiện Teleport (nếu có hỗ trợ)
            // Trong NGO 1.x trở lên, NetworkTransform tự động theo dõi transform. 
            // Nếu bạn dùng ClientNetworkTransform kế thừa NetworkTransform, 
            // nó sẽ tự đồng bộ vị trí mới trong frame tiếp theo.
            if (playerObject.TryGetComponent<ClientNetworkTransform>(out var nt))
            {
                // Gọi hàm Teleport của NetworkTransform để clear nội suy cũ phía Network
                // nt.Teleport(target.position, target.rotation, target.localScale); // Chỉ có từ NGO 1.5.x+
                // Nếu version cũ hơn, việc gán trực tiếp transform phía Owner là đủ, 
                // nhưng Rigidbody mới là thủ phạm chính gây "giật".
            }

            // 4. Khôi phục Rigidbody (Dùng Coroutine để đảm bảo frame tiếp theo mới bật lại)
            if (hasRigidbody)
            {
                StartCoroutine(RestoreRigidbodyState(rb, originalInterpolation));
            }

            Debug.Log($"[TeleportManager] Đã dịch chuyển đến: {_teleportPoints[id].Name}");
        }

        private IEnumerator RestoreRigidbodyState(Rigidbody rb, RigidbodyInterpolation original)
        {
            // Chờ 1 frame để Engine vật lý và NetworkTransform ghi nhận vị trí mới
            yield return new WaitForFixedUpdate();
            
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.interpolation = original;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        private void LockPlayerInput(bool isLocked)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;

            NetworkObject playerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (playerObject != null && playerObject.TryGetComponent<PlayerInputHandler>(out var handler))
            {
                if (isLocked)
                    handler.LockAllInput();
                else
                    handler.UnlockAllInput();
            }
        }
    }
}
