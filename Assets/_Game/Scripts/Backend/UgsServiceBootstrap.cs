using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Initializes Unity Gaming Services once and gives every concurrently running local
/// game process a separate Authentication profile.
///
/// Authentication stores its anonymous session in PlayerPrefs under the profile name.
/// Without separate profiles, two builds on one PC reuse the same UGS PlayerId and the
/// second process receives HTTP 409 when it tries to join the first process's lobby.
/// </summary>
public static class UgsServiceBootstrap
{
    private const int MaxLocalProfileSlots = 16;
    private const string ProfileArgument = "-ugs-profile";

    private static readonly object InitializationLock = new();
    private static Task _initializationTask;
    private static Mutex _profileLease;
    private static string _profile;

    public static string Profile => _profile;

    public static async Task InitializeAsync()
    {
        Task task;
        lock (InitializationLock)
        {
            if (_initializationTask == null ||
                _initializationTask.IsFaulted ||
                _initializationTask.IsCanceled)
            {
                _initializationTask = InitializeInternalAsync();
            }

            task = _initializationTask;
        }

        await task;
    }

    private static async Task InitializeInternalAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            _profile = ResolveProfile();
            InitializationOptions options = new InitializationOptions().SetProfile(_profile);
            await UnityServices.InitializeAsync(options);
            Debug.Log($"[UGS] Initialized with Authentication profile '{_profile}'.");
            return;
        }

        while (UnityServices.State == ServicesInitializationState.Initializing)
            await Task.Yield();

        if (UnityServices.State != ServicesInitializationState.Initialized)
            throw new InvalidOperationException($"Unity Services failed to initialize. State: {UnityServices.State}");
    }

    private static string ResolveProfile()
    {
        string commandLineProfile = GetCommandLineProfile();
        if (!string.IsNullOrEmpty(commandLineProfile))
            return commandLineProfile;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string leaseKey = ShortHash($"{Application.companyName}|{Application.productName}");
        for (int slot = 0; slot < MaxLocalProfileSlots; slot++)
        {
            Mutex candidate = new Mutex(false, $@"Local\TheFlowerUGS_{leaseKey}_{slot}");
            bool acquired;
            try
            {
                acquired = candidate.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (acquired)
            {
                _profileLease = candidate;
                Application.quitting -= ReleaseProfileLease;
                Application.quitting += ReleaseProfileLease;
                return $"local_{slot}";
            }

            candidate.Dispose();
        }
#endif

        // Fallback for other platforms or an unusually high number of local processes.
        // The executable path keeps copied builds distinct while retaining their login.
        return $"build_{ShortHash(Application.dataPath)}";
    }

    private static string GetCommandLineProfile()
    {
        string[] arguments = System.Environment.GetCommandLineArgs();
        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (argument.StartsWith(ProfileArgument + "=", StringComparison.OrdinalIgnoreCase))
                return ValidateProfile(argument[(ProfileArgument.Length + 1)..]);

            if (string.Equals(argument, ProfileArgument, StringComparison.OrdinalIgnoreCase) &&
                index + 1 < arguments.Length)
            {
                return ValidateProfile(arguments[index + 1]);
            }
        }

        return null;
    }

    private static string ValidateProfile(string profile)
    {
        string value = profile?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 30)
            throw new ArgumentException("UGS profile must contain 1-30 characters.", ProfileArgument);

        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (!char.IsLetterOrDigit(character) && character != '-' && character != '_')
            {
                throw new ArgumentException(
                    "UGS profile may only contain letters, numbers, '-' and '_'.",
                    ProfileArgument);
            }
        }

        return value;
    }

    private static string ShortHash(string value)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
        StringBuilder result = new StringBuilder(12);
        for (int index = 0; index < 6; index++)
            result.Append(hash[index].ToString("x2"));
        return result.ToString();
    }

    private static void ReleaseProfileLease()
    {
        Application.quitting -= ReleaseProfileLease;
        if (_profileLease == null) return;

        try
        {
            _profileLease.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // The operating system already released an abandoned lease.
        }
        finally
        {
            _profileLease.Dispose();
            _profileLease = null;
        }
    }
}
