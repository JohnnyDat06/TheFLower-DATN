using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// AuthManager — Khởi tạo Unity Gaming Services và xác thực Anonymous.
/// Không Singleton — inject vào GameBootstrapper.
/// Phải được gọi trước bất kỳ UGS service nào khác.
/// SRS §6.1 · §6.4
/// </summary>
public class AuthManager : MonoBehaviour
{
    // ─── Properties ───────────────────────────────────────────────────────────

    /// <summary>True nếu đã xác thực UGS thành công.</summary>
    public bool IsAuthenticated { get; private set; }

    /// <summary>PlayerId từ UGS Authentication.</summary>
    public string PlayerId { get; private set; }

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>Được gọi khi xác thực thành công.</summary>
    public event Action OnAuthSuccess;

    /// <summary>Được gọi khi xác thực thất bại. string = error message.</summary>
    public event Action<string> OnAuthFailed;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        DontDestroyOnLoad(transform.root.gameObject);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Khởi tạo UGS và sign in anonymous. Gọi 1 lần khi game start.
    /// Throws nếu không có internet hoặc UGS không phản hồi.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            PlayerId = AuthenticationService.Instance.PlayerId;
            PlayerPrefs.SetString(Constants.PlayerPrefsKeys.PLAYER_ID, PlayerId);
            IsAuthenticated = true;

            OnAuthSuccess?.Invoke();
            Debug.Log($"[AuthManager] Signed in. PlayerId: {PlayerId}");
        }
        catch (Exception e)
        {
            IsAuthenticated = false;
            OnAuthFailed?.Invoke(e.Message);
            Debug.LogError($"[AuthManager] Auth failed: {e.Message}");
            throw; // re-throw để caller xử lý
        }
    }
}
