using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Networking.LobbySystem
{
    public class LobbyManager : MonoBehaviour
    {
        public static LobbyManager Instance { get; private set; }
        private Lobby _currentLobby;
        private string _playerId;
        private string _playerName;
        private float _pollTimer;
        private float _heartbeatTimer;
        private bool _isJoiningRelay;
        private bool _isAuthenticating;

        private const string KEY_RELAY_JOIN_CODE = "RelayJoinCode";
        private const string KEY_ROOM_CODE = "RoomCode";

        public event Action<Lobby> OnLobbyJoined;
        public event Action OnLobbyLeft;

        private void Awake() { 
            if (Instance == null) {
                Instance = this; 
                DontDestroyOnLoad(gameObject); 
            } else {
                Destroy(gameObject);
            }
        }

        private void Update() 
        { 
            if (_currentLobby != null)
            {
                // Handle Heartbeat
                if (_currentLobby.HostId == _playerId)
                {
                    _heartbeatTimer -= Time.deltaTime;
                    if (_heartbeatTimer <= 0f)
                    {
                        _heartbeatTimer = 15f;
                        HandleLobbyHeartbeat();
                    }
                }

                // Handle Poll
                _pollTimer -= Time.deltaTime;
                if (_pollTimer <= 0f)
                {
                    _pollTimer = 1.5f;
                    HandleLobbyPollForUpdates();
                }
            }
        }

        private async void HandleLobbyHeartbeat()
        {
            if (_currentLobby != null)
            {
                try {
                    await LobbyService.Instance.SendHeartbeatPingAsync(_currentLobby.Id);
                } catch { }
            }
        }

        private async void HandleLobbyPollForUpdates() 
        {
            if (_currentLobby != null) 
            {
                try {
                    _currentLobby = await LobbyService.Instance.GetLobbyAsync(_currentLobby.Id);
                    OnLobbyJoined?.Invoke(_currentLobby);

                    // Nếu là Client và chưa kết nối, hãy kiểm tra Relay Code
                    if (_currentLobby.HostId != _playerId && NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsConnectedClient)
                    {
                        if (_currentLobby.Data != null && _currentLobby.Data.ContainsKey(KEY_RELAY_JOIN_CODE))
                        {
                            string code = _currentLobby.Data[KEY_RELAY_JOIN_CODE].Value;
                            if (!string.IsNullOrEmpty(code)) 
                            {
                                Debug.Log($"[LobbyManager] Found Relay Code in Poll: {code}. Connecting...");
                                JoinRelay(code);
                            }
                        }
                    }
                } catch (LobbyServiceException e) { 
                    Debug.LogWarning($"[LobbyManager] Poll error (Lobby might be gone): {e.Message}");
                    if (e.Reason == LobbyExceptionReason.LobbyNotFound) {
                        ForceLeave();
                    }
                } catch (Exception e) {
                    Debug.LogError($"[LobbyManager] Unexpected poll error: {e.Message}");
                }
            }
        }

        public async Task Authenticate(string playerName) {
            if (_isAuthenticating) {
                while (_isAuthenticating) await Task.Yield();
                if (AuthenticationService.Instance.IsSignedIn) return;
            }

            try {
                _isAuthenticating = true;
                _playerName = playerName;

                // 1. Phải khởi tạo Unity Services trước khi chạm vào AuthenticationService
                if (UnityServices.State == ServicesInitializationState.Uninitialized) 
                {
                    await UnityServices.InitializeAsync();
                }
                
                while (UnityServices.State == ServicesInitializationState.Initializing) 
                {
                    await Task.Yield();
                }

                // 2. Bây giờ mới an toàn để truy cập Instance
                if (!AuthenticationService.Instance.IsSignedIn) 
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                _playerId = AuthenticationService.Instance.PlayerId;
                Debug.Log($"[LobbyManager] Authenticated (ID: {_playerId}). Name set to: {_playerName}");
            } catch (Exception e) {
                Debug.LogError($"[LobbyManager] Authentication error: {e.Message}");
                throw;
            } finally {
                _isAuthenticating = false;
            }
        }

        private Player GetPlayerData() {
            return new Player {
                Data = new Dictionary<string, PlayerDataObject> {
                    { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, _playerName) }
                }
            };
        }

        public async Task CreateLobby(string lobbyName, int maxPlayers, bool isPrivate) {
            try {
                await LeaveLobby();

                string roomCode = UnityEngine.Random.Range(1000, 9999).ToString();
                var options = new CreateLobbyOptions { 
                    IsPrivate = isPrivate,
                    Data = new Dictionary<string, DataObject> { { KEY_ROOM_CODE, new DataObject(DataObject.VisibilityOptions.Public, roomCode) } },
                    Player = GetPlayerData()
                };
                
                _currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
                OnLobbyJoined?.Invoke(_currentLobby);
                
                string relayCode = await CreateRelay();
                if (!string.IsNullOrEmpty(relayCode))
                {
                    await LobbyService.Instance.UpdateLobbyAsync(_currentLobby.Id, new UpdateLobbyOptions { 
                        Data = new Dictionary<string, DataObject> { { KEY_RELAY_JOIN_CODE, new DataObject(DataObject.VisibilityOptions.Member, relayCode) } } 
                    });
                    Debug.Log($"[LobbyManager] Lobby Created: {_currentLobby.Id} with Relay: {relayCode}");
                }
            } catch (Exception e) { 
                Debug.LogError($"[LobbyManager] Create error: {e.Message}\n{e.StackTrace}"); 
            }
        }

        public async Task QuickJoinLobby() {
            try {
                await LeaveLobby();
                var options = new QuickJoinLobbyOptions { Player = GetPlayerData() };
                _currentLobby = await LobbyService.Instance.QuickJoinLobbyAsync(options);
                OnLobbyJoined?.Invoke(_currentLobby);
                CheckAndJoinRelayImmediately();
            } catch (Exception e) { Debug.LogWarning($"[LobbyManager] QuickJoin failed: {e.Message}"); }
        }

        public async Task JoinLobbyByCode(string roomCode) {
            try {
                await LeaveLobby();
                var query = await LobbyService.Instance.QueryLobbiesAsync();
                foreach (var l in query.Results) {
                    if (l.Data != null && l.Data.ContainsKey(KEY_ROOM_CODE) && l.Data[KEY_ROOM_CODE].Value == roomCode) {
                        var options = new JoinLobbyByIdOptions { Player = GetPlayerData() };
                        _currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(l.Id, options);
                        OnLobbyJoined?.Invoke(_currentLobby);
                        CheckAndJoinRelayImmediately();
                        return;
                    }
                }
                Debug.LogError("[LobbyManager] Room not found with code: " + roomCode);
            } catch (Exception e) { Debug.LogError($"[LobbyManager] JoinCode error: {e.Message}"); }
        }

        private void CheckAndJoinRelayImmediately()
        {
            if (_currentLobby != null && _currentLobby.Data != null && _currentLobby.Data.ContainsKey(KEY_RELAY_JOIN_CODE))
            {
                string code = _currentLobby.Data[KEY_RELAY_JOIN_CODE].Value;
                if (!string.IsNullOrEmpty(code)) JoinRelay(code);
            }
        }

        public async Task LeaveLobby() {
            try {
                if (NetworkManager.Singleton != null) 
                {
                    NetworkManager.Singleton.Shutdown();
                    // Đợi vài frame để NetworkManager tắt hẳn
                    float timeout = 2f;
                    while (NetworkManager.Singleton.IsListening && timeout > 0) {
                        timeout -= Time.deltaTime;
                        await Task.Yield();
                    }
                }
                
                if (_currentLobby != null) 
                {
                    if (_currentLobby.HostId == _playerId)
                    {
                        await LobbyService.Instance.DeleteLobbyAsync(_currentLobby.Id);
                        Debug.Log($"[LobbyManager] Host left, Lobby deleted.");
                    }
                    else
                    {
                        await LobbyService.Instance.RemovePlayerAsync(_currentLobby.Id, _playerId);
                    }
                }
            } catch (Exception e) { Debug.LogWarning($"[LobbyManager] Leave error (safe to ignore if just starting): {e.Message}"); }
            finally {
                ForceLeave();
            }
        }

        private void ForceLeave() {
            _currentLobby = null; 
            OnLobbyLeft?.Invoke();
        }

        private async Task<string> CreateRelay() {
            try {
                if (RelayService.Instance == null || NetworkManager.Singleton == null) return null;

                // Tăng lên 4 slots để thoải mái hơn cho game 2 người (1 host + 3 slots)
                var allocation = await RelayService.Instance.CreateAllocationAsync(3);
                var code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                
                var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (utp != null) {
                    // Dùng "udp" thay cho "dtls" để tránh bị chặn bởi Firewall/Router
                    utp.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "udp"));
                    NetworkManager.Singleton.StartHost();
                    return code;
                }
            } catch (Exception e) {
                Debug.LogError($"[LobbyManager] CreateRelay Error: {e.Message}");
            }
            return null;
        }

        private async void JoinRelay(string code) {
            if (_isJoiningRelay) return;
            if (string.IsNullOrEmpty(code)) return;
            
            try {
                if (NetworkManager.Singleton == null) return;
                // Nếu đã đang kết nối hoặc đã kết nối rồi thì bỏ qua
                if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsConnectedClient) return;

                _isJoiningRelay = true;
                Debug.Log($"[LobbyManager] Attempting to join Relay with code: {code}");
                
                var joinAlloc = await RelayService.Instance.JoinAllocationAsync(code);
                var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (utp != null) {
                    utp.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAlloc, "udp"));
                    bool success = NetworkManager.Singleton.StartClient();
                    if (!success) {
                        Debug.LogError("[LobbyManager] NetworkManager failed to StartClient");
                    }
                }
            } catch (Exception e) { 
                Debug.LogError($"[LobbyManager] Relay Join Error: {e.Message}"); 
            } finally {
                // Đợi một chút rồi mới cho phép join lại nếu thất bại
                await Task.Delay(1000);
                _isJoiningRelay = false;
            }
        }

        public void StartGame(string sceneName) {
            if (NetworkManager.Singleton.IsServer) {
                StartCoroutine(StartGameWithFade(sceneName));
            }
        }

        private IEnumerator StartGameWithFade(string sceneName) {
            if (LoadingSyncManager.Instance != null) LoadingSyncManager.Instance.StartLoadingFadeClientRpc();
            yield return new WaitForSecondsRealtime(0.8f);
            if (SceneLoader.Instance != null) SceneLoader.Instance.LoadScene(sceneName);
            else if (NetworkManager.Singleton != null && SceneLoader.CanLoadScene(sceneName)) NetworkManager.Singleton.SceneManager.LoadScene(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        public string GetPlayerId() => _playerId;
        public string GetPlayerName() => _playerName;
    }
}
