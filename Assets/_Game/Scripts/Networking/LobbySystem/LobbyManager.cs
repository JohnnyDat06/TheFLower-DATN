using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Networking.LobbySystem
{
    /// <summary>
    /// Owns the UGS Lobby + Relay lifecycle. Lobby data is discovery metadata while NGO remains
    /// authoritative for connected players, ready state, and scene transitions.
    /// </summary>
    public sealed class LobbyManager : MonoBehaviour
    {
        private const string RelayJoinCodeKey = "RelayJoinCode";
        private const string RoomCodeKey = "RoomCode";
        private const string PlayerNameKey = "PlayerName";
        private const string PlayerReadyKey = "PlayerReady";
        private const float PollInterval = 1.5f;
        private const float HeartbeatInterval = 15f;

        public static LobbyManager Instance { get; private set; }
        public Lobby CurrentLobby => _currentLobby;
        public bool IsBusy => _isAuthenticating || _isJoiningRelay || _isLeaving;

        public event Action<Lobby> OnLobbyJoined;
        public event Action<Lobby> OnLobbyUpdated;
        public event Action OnLobbyLeft;

        private Lobby _currentLobby;
        private string _playerId;
        private string _playerName;
        private float _pollTimer;
        private float _heartbeatTimer;
        private bool _isAuthenticating;
        private bool _isJoiningRelay;
        private bool _isLeaving;
        private Task _leaveTask;
        private bool _isPolling;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (_currentLobby == null) return;

            if (_currentLobby.HostId == _playerId)
            {
                _heartbeatTimer -= Time.unscaledDeltaTime;
                if (_heartbeatTimer <= 0f)
                {
                    _heartbeatTimer = HeartbeatInterval;
                    SendHeartbeat();
                }
            }

            _pollTimer -= Time.unscaledDeltaTime;
            if (_pollTimer <= 0f)
            {
                _pollTimer = PollInterval;
                PollLobby();
            }
        }

        private async void SendHeartbeat()
        {
            Lobby lobby = _currentLobby;
            if (lobby == null) return;

            try { await LobbyService.Instance.SendHeartbeatPingAsync(lobby.Id); }
            catch (Exception exception) { Debug.LogWarning($"[LobbyManager] Heartbeat failed: {exception.Message}"); }
        }

        private async void PollLobby()
        {
            if (_isPolling || _currentLobby == null) return;
            _isPolling = true;

            try
            {
                _currentLobby = await LobbyService.Instance.GetLobbyAsync(_currentLobby.Id);
                OnLobbyUpdated?.Invoke(_currentLobby);
            }
            catch (LobbyServiceException exception)
            {
                Debug.LogWarning($"[LobbyManager] Poll failed: {exception.Message}");
                if (exception.Reason == LobbyExceptionReason.LobbyNotFound) ForceLeave();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LobbyManager] Poll failed: {exception.Message}");
            }
            finally
            {
                _isPolling = false;
            }
        }

        public async Task Authenticate(string playerName)
        {
            string normalizedName = NormalizePlayerName(playerName);
            if (_isAuthenticating)
            {
                while (_isAuthenticating) await Task.Yield();
                _playerName = normalizedName;
                return;
            }

            _isAuthenticating = true;
            try
            {
                _playerName = normalizedName;
                await UgsServiceBootstrap.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                _playerId = AuthenticationService.Instance.PlayerId;
                Debug.Log(
                    $"[LobbyManager] Authenticated {_playerId} as {_playerName} " +
                    $"(profile: {UgsServiceBootstrap.Profile ?? "pre-initialized"}).");
            }
            finally
            {
                _isAuthenticating = false;
            }
        }

        public Task CreateLobby(string lobbyName, int maxPlayers, bool isPrivate)
        {
            return CreateLobby(lobbyName, maxPlayers, isPrivate, null);
        }

        public async Task CreateLobby(string lobbyName, int maxPlayers, bool isPrivate, string password)
        {
            EnsureAuthenticated();
            await LeaveLobby();

            Lobby createdLobby = null;
            try
            {
                string roomCode = UnityEngine.Random.Range(1000, 10000).ToString();
                CreateLobbyOptions options = new()
                {
                    IsPrivate = isPrivate,
                    Password = NormalizePassword(password),
                    Player = CreatePlayerData(),
                    Data = new Dictionary<string, DataObject>
                    {
                        { RoomCodeKey, new DataObject(DataObject.VisibilityOptions.Public, roomCode, DataObject.IndexOptions.S1) }
                    }
                };

                createdLobby = await LobbyService.Instance.CreateLobbyAsync(
                    string.IsNullOrWhiteSpace(lobbyName) ? $"{_playerName}'s Journey" : lobbyName,
                    Mathf.Clamp(maxPlayers, 2, Constants.Gameplay.MAX_RELAY_PLAYERS),
                    options);

                string relayCode = await CreateRelayHost();
                createdLobby = await LobbyService.Instance.UpdateLobbyAsync(createdLobby.Id, new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        { RelayJoinCodeKey, new DataObject(DataObject.VisibilityOptions.Member, relayCode) }
                    }
                });

                SetCurrentLobby(createdLobby);
                OnLobbyJoined?.Invoke(createdLobby);
                Debug.Log($"[LobbyManager] Created room {roomCode} ({createdLobby.Id}).");
            }
            catch
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                    NetworkManager.Singleton.Shutdown();

                if (createdLobby != null)
                {
                    try { await LobbyService.Instance.DeleteLobbyAsync(createdLobby.Id); }
                    catch (Exception cleanupError) { Debug.LogWarning($"[LobbyManager] Cleanup failed: {cleanupError.Message}"); }
                }

                ForceLeave();
                throw;
            }
        }

        public async Task QuickJoinLobby()
        {
            EnsureAuthenticated();
            await LeaveLobby();

            try
            {
                Lobby lobby = await LobbyService.Instance.QuickJoinLobbyAsync(new QuickJoinLobbyOptions
                {
                    Player = CreatePlayerData()
                });
                await JoinRelayClient(lobby);
                SetCurrentLobby(lobby);
                OnLobbyJoined?.Invoke(lobby);
            }
            catch
            {
                ForceLeave();
                throw;
            }
        }

        public Task JoinLobbyByCode(string roomCode)
        {
            return JoinLobbyByCode(roomCode, null);
        }

        public async Task JoinLobbyByCode(string roomCode, string password)
        {
            EnsureAuthenticated();
            await LeaveLobby();
            string normalizedCode = roomCode?.Trim();

            try
            {
                QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
                {
                    Count = 1,
                    Filters = new List<QueryFilter>
                    {
                        new(QueryFilter.FieldOptions.S1, normalizedCode, QueryFilter.OpOptions.EQ)
                    }
                });
                Lobby match = response.Results.Find(lobby =>
                    lobby.Data != null
                    && lobby.Data.TryGetValue(RoomCodeKey, out DataObject data)
                    && string.Equals(data.Value, normalizedCode, StringComparison.Ordinal));

                if (match == null)
                    throw new InvalidOperationException("Room not found. Check the 4-digit code and try again.");

                await JoinMatchedLobby(match, password);
            }
            catch
            {
                ForceLeave();
                throw;
            }
        }

        public async Task<IReadOnlyList<Lobby>> QueryPublicLobbies(int count = 20)
        {
            EnsureAuthenticated();
            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
            {
                Count = Mathf.Clamp(count, 1, 100),
                Filters = new List<QueryFilter>
                {
                    new(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                    new(QueryFilter.FieldOptions.IsLocked, "0", QueryFilter.OpOptions.EQ)
                },
                Order = new List<QueryOrder>
                {
                    new(false, QueryOrder.FieldOptions.Created)
                }
            });
            return response.Results;
        }

        public async Task JoinLobbyByName(string roomName, string password)
        {
            EnsureAuthenticated();
            await LeaveLobby();
            string normalizedName = roomName?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
                throw new ArgumentException("Enter a room name.", nameof(roomName));

            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
            {
                Count = 1,
                Filters = new List<QueryFilter>
                {
                    new(QueryFilter.FieldOptions.Name, normalizedName, QueryFilter.OpOptions.EQ),
                    new(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                    new(QueryFilter.FieldOptions.IsLocked, "0", QueryFilter.OpOptions.EQ)
                }
            });

            Lobby match = response.Results.Count > 0 ? response.Results[0] : null;
            if (match == null) throw new InvalidOperationException("Room not found or already full.");
            await JoinMatchedLobby(match, password);
        }

        public async Task JoinLobbyById(string lobbyId, string password)
        {
            EnsureAuthenticated();
            await LeaveLobby();
            if (string.IsNullOrWhiteSpace(lobbyId)) throw new ArgumentException("Lobby id is empty.", nameof(lobbyId));

            // Do not call GetLobbyAsync here. Password-protected public lobbies can reject
            // detail requests from non-members before the password ever reaches the join request.
            await JoinMatchedLobby(lobbyId, password);
        }

        public Task LeaveLobby()
        {
            // Coalesce concurrent leave requests. Disconnect recovery and the UI can
            // both observe the same NGO shutdown; they must share one cleanup task.
            if (_leaveTask != null && !_leaveTask.IsCompleted)
                return _leaveTask;

            _leaveTask = LeaveLobbyInternal();
            return _leaveTask;
        }

        private async Task LeaveLobbyInternal()
        {
            if (_isLeaving) return;
            _isLeaving = true;
            Lobby lobby = _currentLobby;

            try
            {
                if (NetworkManager.Singleton != null)
                {
                    NetworkManager.Singleton.Shutdown();
                    float timeout = 2f;
                    while ((NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ShutdownInProgress) && timeout > 0f)
                    {
                        timeout -= Time.unscaledDeltaTime;
                        await Task.Yield();
                    }
                    await Task.Yield();
                }

                if (lobby != null && !string.IsNullOrEmpty(_playerId))
                {
                    if (lobby.HostId == _playerId)
                        await LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
                    else
                        await LobbyService.Instance.RemovePlayerAsync(lobby.Id, _playerId);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LobbyManager] Leave failed: {exception.Message}");
            }
            finally
            {
                ForceLeave();
                _isLeaving = false;
            }
        }
        public void StartGame(string sceneName)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (!SceneLoader.CanLoadScene(sceneName)) return;
            StartCoroutine(StartGameWithFade(sceneName));
        }

        public string GetPlayerId() => _playerId;
        public string GetPlayerName() => _playerName;

        public async Task SetPlayerReady(bool isReady)
        {
            if (_currentLobby == null) throw new InvalidOperationException("Join a room before changing ready state.");

            _currentLobby = await LobbyService.Instance.UpdatePlayerAsync(
                _currentLobby.Id,
                _playerId,
                new UpdatePlayerOptions
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { PlayerReadyKey, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, isReady ? "1" : "0") }
                    }
                });
            OnLobbyUpdated?.Invoke(_currentLobby);
        }

        private Player CreatePlayerData()
        {
            return new Player
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { PlayerNameKey, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, _playerName) },
                    { PlayerReadyKey, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "0") }
                }
            };
        }

        private async Task JoinMatchedLobby(Lobby lobby, string password)
        {
            if (lobby == null) throw new ArgumentNullException(nameof(lobby));
            await JoinMatchedLobby(lobby.Id, password);
        }

        private async Task JoinMatchedLobby(string lobbyId, string password)
        {
            Lobby joinedLobby = null;
            try
            {
                joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, new JoinLobbyByIdOptions
                {
                    Player = CreatePlayerData(),
                    Password = NormalizePassword(password)
                });
                await JoinRelayClient(joinedLobby);
                SetCurrentLobby(joinedLobby);
                OnLobbyJoined?.Invoke(joinedLobby);
            }
            catch (Exception exception)
            {
                if (joinedLobby != null)
                {
                    try { await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, _playerId); }
                    catch (Exception cleanupError) { Debug.LogWarning($"[LobbyManager] Join cleanup failed: {cleanupError.Message}"); }
                }
                ForceLeave();

                if (exception is LobbyServiceException lobbyException &&
                    lobbyException.Reason == LobbyExceptionReason.IncorrectPassword)
                    throw new InvalidOperationException("Incorrect room password.", lobbyException);

                throw;
            }
        }

        private async Task<string> CreateRelayHost()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) throw new InvalidOperationException("NetworkManager is missing.");
            float timeout = 2f;
            while (manager.ShutdownInProgress && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                await Task.Yield();
            }
            if (manager.IsListening || manager.ShutdownInProgress)
                throw new InvalidOperationException("NetworkManager is still shutting down. Please try again.");
            if (!manager.TryGetComponent(out UnityTransport transport))
                throw new InvalidOperationException("Unity Transport is missing from NetworkManager.");

            var allocation = await RelayService.Instance.CreateAllocationAsync(Constants.Gameplay.MAX_RELAY_PLAYERS - 1);
            string relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
            VivoxManager.Instance?.SetChannelName(relayCode);
            if (!manager.StartHost()) throw new InvalidOperationException("NetworkManager failed to start the host.");
            return relayCode;
        }

        private async Task JoinRelayClient(Lobby lobby)
        {
            if (_isJoiningRelay) throw new InvalidOperationException("A connection is already in progress.");
            if (lobby?.Data == null
                || !lobby.Data.TryGetValue(RelayJoinCodeKey, out DataObject relayData)
                || string.IsNullOrWhiteSpace(relayData.Value))
                throw new InvalidOperationException("The room is not ready for connections yet.");

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) throw new InvalidOperationException("NetworkManager is missing.");
            if (!manager.TryGetComponent(out UnityTransport transport))
                throw new InvalidOperationException("Unity Transport is missing from NetworkManager.");

            _isJoiningRelay = true;
            try
            {
                var allocation = await RelayService.Instance.JoinAllocationAsync(relayData.Value);
                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
                VivoxManager.Instance?.SetChannelName(relayData.Value);
                if (!manager.StartClient()) throw new InvalidOperationException("NetworkManager failed to start the client.");
            }
            finally
            {
                _isJoiningRelay = false;
            }
        }

        private IEnumerator StartGameWithFade(string sceneName)
        {
            Task lockTask = LockLobbyForGame();
            while (!lockTask.IsCompleted) yield return null;
            if (LoadingSyncManager.Instance != null) LoadingSyncManager.Instance.StartLoadingFadeClientRpc();
            yield return new WaitForSecondsRealtime(0.8f);
            if (SceneLoader.Instance != null) SceneLoader.Instance.LoadScene(sceneName);
            else NetworkManager.Singleton.SceneManager.LoadScene(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        private async Task LockLobbyForGame()
        {
            if (_currentLobby == null || string.IsNullOrEmpty(_currentLobby.Id)) return;
            try
            {
                _currentLobby = await LobbyService.Instance.UpdateLobbyAsync(
                    _currentLobby.Id,
                    new UpdateLobbyOptions { IsLocked = true });
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LobbyManager] Could not lock lobby before game: {exception.Message}");
            }
        }

        private void SetCurrentLobby(Lobby lobby)
        {
            _currentLobby = lobby;
            _pollTimer = PollInterval;
            _heartbeatTimer = HeartbeatInterval;
        }

        /// <summary>Clears local state after transport loss so a new Lobby scene is interactive.</summary>
        public void ResetAfterDisconnect()
        {
            bool hadLobby = _currentLobby != null;
            _currentLobby = null;
            _pollTimer = 0f;
            _heartbeatTimer = 0f;
            _isAuthenticating = false;
            _isJoiningRelay = false;
            _isPolling = false;
            _isLeaving = false;
            if (hadLobby) OnLobbyLeft?.Invoke();
        }

        private void ForceLeave()
        {
            bool hadLobby = _currentLobby != null;
            _currentLobby = null;
            if (hadLobby) OnLobbyLeft?.Invoke();
        }

        private void EnsureAuthenticated()
        {
            if (string.IsNullOrEmpty(_playerId))
                throw new InvalidOperationException("Authenticate before using lobby services.");
        }

        private static string NormalizePlayerName(string playerName)
        {
            string value = string.IsNullOrWhiteSpace(playerName) ? "Traveler" : playerName.Trim();
            return value[..Mathf.Min(value.Length, 20)];
        }

        private static string NormalizePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password)) return null;
            string value = password.Trim();
            if (value.Length >= 8 && value.Length <= 64) return value;

            // UGS itself accepts only 8-64 characters. Hash values outside that range
            // in both Create and Join so players can use any non-empty password length.
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            StringBuilder result = new(hash.Length * 2);
            foreach (byte item in hash) result.Append(item.ToString("x2"));
            return result.ToString();
        }
    }
}
