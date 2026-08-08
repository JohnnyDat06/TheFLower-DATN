using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;
using Networking.LobbySystem;

/// <summary>
/// PlayerHUDController — Quản lý UI thanh máu và Tên cho Host và Client.
/// Đồng bộ trạng thái từ NetworkVariable của PlayerHealth và LobbyPlayerState.
/// SRS §9.3
/// </summary>
public class PlayerHUDController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Slider _hostSlider;
    [SerializeField] private Slider _clientSlider;
    [SerializeField] private TextMeshProUGUI _hostNameText;
    [SerializeField] private TextMeshProUGUI _clientNameText;
    [SerializeField] private GameObject _hostBarRoot;
    [SerializeField] private GameObject _clientBarRoot;

    [Header("Settings")]
    [SerializeField] private float _smoothSpeed = 5f;
    [SerializeField] private bool _hideIfFull = false;

    private PlayerHealth _hostHealth;
    private PlayerHealth _clientHealth;
    private LobbyPlayerState _hostState;
    private LobbyPlayerState _clientState;

    private float _targetHostValue = 1f;
    private float _targetClientValue = 1f;

    private void Start()
    {
        // Khởi tạo ẩn nếu chưa có player
        if (_hostBarRoot != null) _hostBarRoot.SetActive(false);
        if (_clientBarRoot != null) _clientBarRoot.SetActive(false);
    }

    private void Update()
    {
        // Kiểm tra xem có đang ở Lobby không, nếu có thì ẩn HUD
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("Lobby"))
        {
            if (_hostBarRoot != null && _hostBarRoot.activeSelf) _hostBarRoot.SetActive(false);
            if (_clientBarRoot != null && _clientBarRoot.activeSelf) _clientBarRoot.SetActive(false);
            return;
        }

        // Tìm player nếu chưa có
        if (_hostHealth == null || _clientHealth == null)
        {
            FindPlayers();
        }

        // Cập nhật giá trị Slider mượt mà bằng Lerp
        UpdateSliderSmoothly(_hostSlider, _targetHostValue, _hostBarRoot);
        UpdateSliderSmoothly(_clientSlider, _targetClientValue, _clientBarRoot);
    }

    private void FindPlayers()
    {
        var allPlayers = Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        foreach (var player in allPlayers)
        {
            if (!player.IsSpawned) continue;

            // Host thường có OwnerClientId = 0
            if (player.OwnerClientId == 0)
            {
                if (_hostHealth == null)
                {
                    _hostHealth = player;
                    _hostHealth.OnHealthChanged += HandleHostHealthChanged;
                    
                    if (player.TryGetComponent<LobbyPlayerState>(out var state))
                    {
                        _hostState = state;
                        // Hủy đăng ký cũ nếu có để tránh trùng
                        _hostState.PlayerName.OnValueChanged -= OnHostNameChanged;
                        _hostState.PlayerName.OnValueChanged += OnHostNameChanged;
                        UpdateNameText(_hostNameText, _hostState.PlayerName.Value.ToString());
                    }

                    if (_hostBarRoot != null) _hostBarRoot.SetActive(true);
                    _targetHostValue = _hostHealth.CurrentHealth / _hostHealth.MaxHealth;
                    if (_hostSlider != null) _hostSlider.value = _targetHostValue;
                }
            }
            else
            {
                if (_clientHealth == null)
                {
                    _clientHealth = player;
                    _clientHealth.OnHealthChanged += HandleClientHealthChanged;

                    if (player.TryGetComponent<LobbyPlayerState>(out var state))
                    {
                        _clientState = state;
                        // Hủy đăng ký cũ nếu có để tránh trùng
                        _clientState.PlayerName.OnValueChanged -= OnClientNameChanged;
                        _clientState.PlayerName.OnValueChanged += OnClientNameChanged;
                        UpdateNameText(_clientNameText, _clientState.PlayerName.Value.ToString());
                    }

                    if (_clientBarRoot != null) _clientBarRoot.SetActive(true);
                    _targetClientValue = _clientHealth.CurrentHealth / _clientHealth.MaxHealth;
                    if (_clientSlider != null) _clientSlider.value = _targetClientValue;
                }
            }
        }
    }

    private void OnHostNameChanged(Unity.Collections.FixedString32Bytes oldVal, Unity.Collections.FixedString32Bytes newVal)
    {
        UpdateNameText(_hostNameText, newVal.ToString());
    }

    private void OnClientNameChanged(Unity.Collections.FixedString32Bytes oldVal, Unity.Collections.FixedString32Bytes newVal)
    {
        UpdateNameText(_clientNameText, newVal.ToString());
    }

    private void UpdateNameText(TextMeshProUGUI textComp, string name)
    {
        if (textComp != null)
        {
            textComp.text = string.IsNullOrEmpty(name) ? "Player" : name;
            Debug.Log($"[HUD] Updating UI Name to: {textComp.text}");
        }
    }

    private void HandleHostHealthChanged(float curr, float max)
    {
        _targetHostValue = Mathf.Clamp01(curr / max);
    }

    private void HandleClientHealthChanged(float curr, float max)
    {
        _targetClientValue = Mathf.Clamp01(curr / max);
    }

    private void UpdateSliderSmoothly(Slider slider, float targetValue, GameObject root)
    {
        if (slider == null) return;

        // Lerp để trượt mượt
        slider.value = Mathf.Lerp(slider.value, targetValue, Time.deltaTime * _smoothSpeed);

        // Logic ẩn/hiện nếu cần
        if (_hideIfFull && root != null)
        {
            bool isFull = slider.value >= 0.999f && targetValue >= 1f;
            if (root.activeSelf == isFull) root.SetActive(!isFull);
        }
    }

    private void OnDestroy()
    {
        if (_hostHealth != null) _hostHealth.OnHealthChanged -= HandleHostHealthChanged;
        if (_clientHealth != null) _clientHealth.OnHealthChanged -= HandleClientHealthChanged;
        if (_hostState != null) _hostState.PlayerName.OnValueChanged -= OnHostNameChanged;
        if (_clientState != null) _clientState.PlayerName.OnValueChanged -= OnClientNameChanged;
    }
}
