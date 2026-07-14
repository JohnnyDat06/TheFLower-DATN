using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI.Lobby
{
    public class LobbyPlayerEntry : MonoBehaviour
    {
        [SerializeField] private TMP_Text _playerNameText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private Image _statusIndicator;

        private ulong _clientId;

        public void Initialize(ulong clientId, bool isHost)
        {
            _clientId = clientId;
            _playerNameText.text = isHost ? $"Player {clientId} (Host)" : $"Player {clientId}";
            SetReadyStatus(false);
        }

        public void SetReadyStatus(bool isReady)
        {
            if (_statusText != null)
                _statusText.text = isReady ? "READY" : "WAITING";
                
            if (_statusIndicator != null)
                _statusIndicator.color = isReady ? Color.green : Color.gray;
        }

        public ulong GetClientId() => _clientId;
    }
}
