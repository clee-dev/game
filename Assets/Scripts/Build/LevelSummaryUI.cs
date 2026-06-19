using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Level-end summary screen (Win/Loss Conditions, PLANNED_FEATURES.md). Shown once
/// GameEvents.OnLevelEnded fires -- natural completion (every tile Built) or forced
/// (LevelTimer expiry), both routed through BuildSystem.EvaluateCompletion. Reports
/// success/fail and completion % with a Return to Hub button, using the same
/// NetworkPlayer.RequestLoadSceneRpc("Hub") relay PauseMenu.LeaveToHub uses (NGO's
/// NetworkSceneManager.LoadScene is server-only).
///
/// blameSummaryRoot is reserved screen space for the planned blame summary (who made
/// the most mistakes, death count, etc.) -- NOT implemented yet. See
/// docs/PLANNED_FEATURES.md "Level Summary" for the open spec; this is where it goes
/// once built.
/// </summary>
public class LevelSummaryUI : MonoBehaviour
{
    [SerializeField] private GameObject summaryPanel;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private TextMeshProUGUI completionText;
    [SerializeField] private Button returnToHubButton;

    [Header("Reserved for future blame summary -- not implemented yet, see PLANNED_FEATURES.md")]
    [SerializeField] private GameObject blameSummaryRoot;

    private void Awake()
    {
        if (returnToHubButton != null) returnToHubButton.onClick.AddListener(ReturnToHub);
        if (summaryPanel != null) summaryPanel.SetActive(false);
        if (blameSummaryRoot != null) blameSummaryRoot.SetActive(false);
    }

    private void OnEnable()  => GameEvents.OnLevelEnded += HandleLevelEnded;
    private void OnDisable() => GameEvents.OnLevelEnded -= HandleLevelEnded;

    private void HandleLevelEnded(bool success, float completionPercent)
    {
        if (summaryPanel != null) summaryPanel.SetActive(true);
        if (resultText != null) resultText.text = success ? "Level Complete" : "Time's Up";
        if (completionText != null) completionText.text = $"{Mathf.RoundToInt(completionPercent * 100f)}% Built";

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ReturnToHub()
    {
        var localPlayerObject = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;

        if (localPlayerObject != null && localPlayerObject.TryGetComponent(out NetworkPlayer player))
        {
            player.RequestLoadSceneRpc("Hub");
            return;
        }

        SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
    }
}
