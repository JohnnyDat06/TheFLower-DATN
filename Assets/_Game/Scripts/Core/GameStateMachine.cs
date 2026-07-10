using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Các trạng thái toàn cục của game. SRS §13.1
/// </summary>
public enum GameState
{
    MainMenu,
    Lobby,
    Loading,
    Playing,
    Paused,
    Respawning,
    GameOver
}

/// <summary>
/// GameStateMachine — Quản lý trạng thái toàn cục của game.
/// Mọi thay đổi state phải qua class này. Attach vào một persistent GameObject (DontDestroyOnLoad).
/// SRS §13.1
/// </summary>
public class GameStateMachine : MonoBehaviour
{
    /// <summary>Trạng thái game hiện tại — chỉ đọc từ bên ngoài.</summary>
    public GameState CurrentState { get; private set; } = GameState.MainMenu;

    // Valid transitions: key = fromState, value = set of allowed toStates
    private static readonly Dictionary<GameState, HashSet<GameState>> _validTransitions
        = new Dictionary<GameState, HashSet<GameState>>
        {
            { GameState.MainMenu,   new HashSet<GameState> { GameState.Lobby } },
            { GameState.Lobby,      new HashSet<GameState> { GameState.Loading, GameState.MainMenu } },
            { GameState.Loading,    new HashSet<GameState> { GameState.Playing } },
            { GameState.Playing,    new HashSet<GameState> { GameState.Paused, GameState.Respawning, GameState.GameOver, GameState.Loading } },
            { GameState.Paused,     new HashSet<GameState> { GameState.Playing } },
            { GameState.Respawning, new HashSet<GameState> { GameState.Playing } },
            { GameState.GameOver,   new HashSet<GameState> { GameState.MainMenu } },
        };

    private void Awake()
    {
        Transform root = transform;
        while (root.parent != null && !root.parent.name.Contains("SYSTEM & MANAGERS"))
        {
            root = root.parent;
        }
        DontDestroyOnLoad(root.gameObject);
    }

    /// <summary>
    /// Method duy nhất để thay đổi game state.
    /// Guard invalid transitions — log warning và KHÔNG thay đổi state nếu transition không hợp lệ.
    /// </summary>
    /// <param name="newState">Trạng thái muốn chuyển sang.</param>
    public void TransitionTo(GameState newState)
    {
        // Guard: đã ở state này rồi
        if (newState == CurrentState)
        {
            Debug.Log($"[GameFSM] Already in state {CurrentState}, skipping.");
            return;
        }

        // Guard: invalid transition
        if (!_validTransitions.TryGetValue(CurrentState, out var allowedStates)
            || !allowedStates.Contains(newState))
        {
            Debug.LogWarning($"[GameFSM] Invalid transition: {CurrentState} → {newState}");
            return;
        }

        var previousState = CurrentState;
        CurrentState = newState;
        Debug.Log($"[GameFSM] {previousState} → {newState}");

        HandleTransitionSideEffects(previousState, newState);
    }

    /// <summary>Kích hoạt EventBus tương ứng khi transition.</summary>
    private void HandleTransitionSideEffects(GameState from, GameState to)
    {
        switch (to)
        {
            case GameState.Paused:
                EventBus.RaiseGamePaused();
                break;

            case GameState.Playing when from == GameState.Paused:
                EventBus.RaiseGameResumed();
                break;
        }
    }
}
