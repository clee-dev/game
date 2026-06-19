using TMPro;
using UnityEngine;

/// <summary>
/// Screen-space MM:SS readout bound to LevelTimer's replicated countdown
/// (PLANNED_FEATURES.md, Phase B -- Timer System, "client-side display"). Plain
/// MonoBehaviour, not networked -- LevelTimer.Instance is the single source of truth,
/// this only formats and displays it locally, identically on every machine.
/// </summary>
public class LevelTimerHUD : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI timerText;

    private void OnEnable()  => GameEvents.OnLevelEnded += HandleLevelEnded;
    private void OnDisable() => GameEvents.OnLevelEnded -= HandleLevelEnded;

    private void Update()
    {
        if (timerText == null || LevelTimer.Instance == null) return;

        float remaining = LevelTimer.Instance.RemainingTime;
        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);
        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    private void HandleLevelEnded(bool success, float completionPercent)
    {
        if (timerText == null) return;
        timerText.text = success ? "Complete!" : $"Time's Up -- {Mathf.RoundToInt(completionPercent * 100f)}%";
    }
}
