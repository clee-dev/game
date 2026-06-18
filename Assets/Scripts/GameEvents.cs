using System;

/// <summary>
/// Static event bus for game state changes.
/// LobbyUI fires these. PlayerCamera and InputReader listen.
/// Keeps systems decoupled -- no direct references between them.
/// </summary>
public static class GameEvents
{
    public static event Action OnGameStarted;
    public static event Action OnGamePaused;
    public static event Action OnGameResumed;

    public static void FireGameStarted() => OnGameStarted?.Invoke();
    public static void FireGamePaused()  => OnGamePaused?.Invoke();
    public static void FireGameResumed() => OnGameResumed?.Invoke();
}
