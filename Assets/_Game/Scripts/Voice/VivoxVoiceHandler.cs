using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Vivox;
using UnityEngine;
using Unity.Collections;

public class VivoxVoiceHandler : NetworkBehaviour
{
    [Header("Occlusion Settings")]
    [SerializeField] private LayerMask _occlusionLayerMask = 1 << 0; // Default layer
    [Range(0f, 1f)]
    [SerializeField] private float _occludedVolumeReduction = 0.5f;
    [Range(0.05f, 1f)]
    [SerializeField] private float _occlusionCheckInterval = 0.2f;

    private static Dictionary<string, VivoxVoiceHandler> _allHandlers = new Dictionary<string, VivoxVoiceHandler>();

    private NetworkVariable<FixedString64Bytes> _syncedVivoxId = new NetworkVariable<FixedString64Bytes>(
        writePerm: NetworkVariableWritePermission.Owner);
    
    private string _vivoxId;
    private float _nextOcclusionCheck;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // Kiểm tra an toàn xem Services đã được khởi tạo chưa trước khi truy cập Instance
            if (Unity.Services.Core.UnityServices.State == Unity.Services.Core.ServicesInitializationState.Initialized)
            {
                if (AuthenticationService.Instance.IsSignedIn)
                {
                    _syncedVivoxId.Value = AuthenticationService.Instance.PlayerId;
                }
            }
            
            // JoinVoiceChannel đã có sẵn logic đợi khởi tạo bên trong
        }

        // Register current ID if already set
        if (!_syncedVivoxId.Value.IsEmpty)
        {
            RegisterHandler(_syncedVivoxId.Value.ToString());
        }

        _syncedVivoxId.OnValueChanged += (prev, current) => 
        {
            if (!prev.IsEmpty) _allHandlers.Remove(prev.ToString());
            if (!current.IsEmpty) RegisterHandler(current.ToString());
        };
    }

    private void RegisterHandler(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        
        _vivoxId = id;
        _allHandlers[id] = this;
        Debug.Log($"[VivoxVoiceHandler] Registered handler for PlayerId: {id} (Owner: {OwnerClientId}, IsOwner: {IsOwner})");
    }

    public override void OnNetworkDespawn()
    {
        if (!string.IsNullOrEmpty(_vivoxId))
        {
            if (_allHandlers.TryGetValue(_vivoxId, out var handler) && handler == this)
            {
                _allHandlers.Remove(_vivoxId);
            }
        }

    }

    private async void JoinVoiceChannel()
    {
        if (VivoxManager.Instance == null)
        {
            Debug.LogWarning("[VivoxVoiceHandler] VivoxManager instance is null; voice chat will be skipped for this player.");
            return;
        }

        // Đợi một chút để đảm bảo NetworkId và Ownership đã ổn định
        await System.Threading.Tasks.Task.Delay(1000);
        if (this == null || !IsSpawned) return;

        int retries = 0;
        while (retries < 10) // Tăng số lần thử lại
        {
            try
            {
                Debug.Log($"[VivoxVoiceHandler] Login attempt {retries + 1}...");
                await VivoxManager.Instance.LoginAsync();
                
                if (VivoxManager.Instance.IsLoggedIn)
                {
                    if (IsOwner && AuthenticationService.Instance.IsSignedIn)
                    {
                        string currentId = AuthenticationService.Instance.PlayerId;
                        
                        // Cập nhật NetworkVariable để các máy khác biết PlayerId của mình
                        if (_syncedVivoxId.Value.ToString() != currentId)
                        {
                            _syncedVivoxId.Value = currentId;
                        }
                        
                        // Đăng ký local handler
                        RegisterHandler(currentId);
                    }

                    // Lấy channel name từ RelayManager (thông qua DefaultChannelName)
                    string channelToJoin = VivoxManager.Instance.DefaultChannelName;
                    
                    if (string.IsNullOrEmpty(channelToJoin) || channelToJoin == "MainLobby")
                    {
                        int waitLoops = 0;
                        while ((string.IsNullOrEmpty(channelToJoin) || channelToJoin == "MainLobby") && waitLoops < 6)
                        {
                            Debug.Log($"[VivoxVoiceHandler] Channel name is still default/empty, waiting for Relay JoinCode ({waitLoops + 1}/6)...");
                            await System.Threading.Tasks.Task.Delay(1000);
                            if (this == null || !IsSpawned) return;
                            channelToJoin = VivoxManager.Instance.DefaultChannelName;
                            waitLoops++;
                        }
                    }

                    Debug.Log($"[VivoxVoiceHandler] Attempting to join channel: {channelToJoin} (3D Positional)");
                    await VivoxManager.Instance.JoinChannelAsync(channelToJoin, true); // Bật lại 3D Positional
                    
                    if (!string.IsNullOrEmpty(VivoxManager.Instance.JoinedChannelName))
                    {
                        Debug.Log($"[VivoxVoiceHandler] Successfully joined voice channel: {VivoxManager.Instance.JoinedChannelName}");
                        break;
                    }
                }
                else
                {
                    Debug.LogWarning($"[VivoxVoiceHandler] Login failed at attempt {retries + 1}, retrying...");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VivoxVoiceHandler] Join attempt {retries + 1} failed with error: {e.Message}");
            }

            retries++;
            await System.Threading.Tasks.Task.Delay(3000); // Tăng thời gian delay giữa các lần retry
            if (this == null || !IsSpawned) return;
        }

        if (!VivoxManager.Instance.IsLoggedIn)
        {
            Debug.LogError("[VivoxVoiceHandler] Failed to login to Vivox after all retries.");
        }
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (VivoxManager.Instance == null || !VivoxManager.Instance.IsCurrentChannelPositional) return;

        // Cập nhật vị trí 3D cho chính mình (Speaker) và tai nghe (Listener)
        UpdatePositions();

        if (IsOwner)
        {
            HandleOcclusion();
        }
    }

    private void UpdatePositions()
    {
        // CHỈ CHẠY TRÊN OWNER (Người đang ngồi trước máy)
        // Set3DPosition cập nhật vị trí của BẢN THÂN trong không gian 3D của Vivox
        // Nếu chạy trên Proxy nó sẽ liên tục đưa vị trí mic của bản thân tới vị trí của người khác!
        if (!IsOwner) return;

        if (VivoxManager.Instance == null || !VivoxManager.Instance.IsCurrentChannelPositional || string.IsNullOrEmpty(VivoxManager.Instance.JoinedChannelName)) return;
        if (!VivoxManager.Instance.IsLoggedIn) return;

        Vector3 speakerPos = transform.position + Vector3.up * 1.5f;

        Transform listenerTransform = Camera.main != null ? Camera.main.transform : transform;
        
        try 
        {
            VivoxService.Instance.Set3DPosition(
                speakerPos,                      // Vị trí người nói (miệng của avatar bản thân)
                listenerTransform.position,      // Vị trí người nghe (camera)
                listenerTransform.forward,       // Hướng nhìn
                listenerTransform.up,            // Hướng lên
                VivoxManager.Instance.JoinedChannelName
            );
        }
        catch (System.Exception) { }
    }

    private void HandleOcclusion()
    {
        if (Time.time < _nextOcclusionCheck) return;
        if (VivoxManager.Instance == null || string.IsNullOrEmpty(VivoxManager.Instance.JoinedChannelName)) return;
        
        if (!VivoxService.Instance.ActiveChannels.TryGetValue(VivoxManager.Instance.JoinedChannelName, out var channel)) return;

        _nextOcclusionCheck = Time.time + _occlusionCheckInterval;

        foreach (var participant in channel)
        {
            if (participant.IsSelf) continue;

            // Tìm remote handler dựa trên PlayerId
            VivoxVoiceHandler remoteHandler = null;
            string participantId = participant.PlayerId;

            foreach (var kvp in _allHandlers)
            {
                if (participantId.Contains(kvp.Key))
                {
                    remoteHandler = kvp.Value;
                    break;
                }
            }

            if (remoteHandler != null)
            {
                // Thực hiện Raycast kiểm tra vật cản
                Transform listenerTransform = Camera.main != null ? Camera.main.transform : transform;
                Vector3 listenerPos = listenerTransform.position;
                Vector3 speakerPos = remoteHandler.transform.position + Vector3.up * 1.5f;

                Vector3 direction = speakerPos - listenerPos;
                float distance = direction.magnitude;

                bool isOccluded = Physics.Raycast(listenerPos, direction.normalized, distance, _occlusionLayerMask);

                if (isOccluded)
                {
                    // Vivox local volume: -50 đến 50. -50 = tắt hẳn, 0 = bình thường.
                    int occlusionVol = Mathf.RoundToInt(-50f * _occludedVolumeReduction);
                    participant.SetLocalVolume(occlusionVol);
                }
                else
                {
                    // Trả về mức âm thanh bình thường
                    participant.SetLocalVolume(0);
                }
            }
            else
            {
                // Nếu không tìm thấy handler (delay đồng bộ), để bình thường
                participant.SetLocalVolume(0);
            }
        }
    }
}
