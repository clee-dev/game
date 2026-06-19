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

    /// <summary>Fired once per level by BuildSystem.EvaluateCompletion -- either natural
    /// (100% built) or forced (LevelTimer expiry). success/completionPercent let listeners
    /// (currently just LevelTimerHUD) react without depending on BuildSystem directly.</summary>
    public static event Action<bool, float> OnLevelEnded;

    public static void FireGameStarted() => OnGameStarted?.Invoke();
    public static void FireGamePaused()  => OnGamePaused?.Invoke();
    public static void FireGameResumed() => OnGameResumed?.Invoke();
    public static void FireLevelEnded(bool success, float completionPercent) => OnLevelEnded?.Invoke(success, completionPercent);
}
