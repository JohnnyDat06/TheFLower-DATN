using System.Collections.Generic;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

public class VoiceDebugUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject _panel;
    [SerializeField] private Image _energyBar;
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private TextMeshProUGUI _energyValueText;

    private bool _isVisible = false;

    private void Start()
    {
        // Setup initial UI state
        if (_panel == null)
        {
            // If not assigned, try to find a child panel
            if (transform.childCount > 0)
                _panel = transform.GetChild(0).gameObject;
        }
        
        if (_panel != null)
        {
            _panel.SetActive(_isVisible);
        }
    }

    private void Update()
    {
        // Toggle UI visibility with 'V' key using New Input System
        if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
        {
            _isVisible = !_isVisible;
            if (_panel != null)
            {
                _panel.SetActive(_isVisible);
                Debug.Log($"[VoiceDebugUI] UI Visibility: {_isVisible}");
            }
        }

        if (!_isVisible || _panel == null) return;

        UpdateStatus();
        UpdateEnergy();
    }

    private void UpdateStatus()
    {
        if (VivoxManager.Instance == null)
        {
            if (_statusText != null) _statusText.text = "Status: VivoxManager Missing";
            return;
        }

        string status = VivoxManager.Instance.IsLoggedIn ? "<color=green>Logged In</color>" : "<color=red>Not Logged In</color>";
        string channel = !string.IsNullOrEmpty(VivoxManager.Instance.JoinedChannelName) 
            ? $"<color=yellow>{VivoxManager.Instance.JoinedChannelName}</color>" 
            : "<color=gray>None</color>";
        
        bool isMuted = VivoxManager.Instance.IsMicrophoneMuted();
        string muteStatus = isMuted ? "<color=red>MUTED</color>" : "<color=green>UNMUTED</color>";

        if (_statusText != null) 
            _statusText.text = $"Status: {status}\nChannel: {channel}\nMic: {muteStatus}\nKey: [V] Debug, [K] Mute";
    }

    private void UpdateEnergy()
    {
        if (VivoxManager.Instance == null || string.IsNullOrEmpty(VivoxManager.Instance.JoinedChannelName))
        {
            if (_energyBar != null) _energyBar.fillAmount = 0;
            if (_energyValueText != null) _energyValueText.text = "0%";
            return;
        }

        // Find local participant energy
        double energy = 0;
        try 
        {
            if (VivoxService.Instance.ActiveChannels.TryGetValue(VivoxManager.Instance.JoinedChannelName, out var participants))
            {
                foreach (var participant in participants)
                {
                    if (participant.IsSelf)
                    {
                        energy = participant.AudioEnergy;
                        break;
                    }
                }
            }
        }
        catch (System.Exception)
        {
            // Vivox might not be ready
        }

        if (_energyBar != null)
        {
            _energyBar.fillAmount = (float)energy;
            _energyBar.color = Color.Lerp(Color.green, Color.red, (float)energy);
        }
        
        if (_energyValueText != null) _energyValueText.text = $"{(energy * 100):F0}%";
    }
}
