using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Pause overlay for Game1 and LevelEditor (Escape toggles it, "Leave to Hub" returns).
/// Mirrors LobbyUI's pause-panel shape but the leave action differs: LobbyUI's "leave"
/// exits the session entirely (pre-game), while this "leave to Hub" sends the whole
/// connected party back into the Hub scene via NGO's NetworkSceneManager -- same
/// mechanism StartingAreaTrigger/LevelEditorAccessPoint use, since there's no
/// personal/solo scene concept in this game.
///
/// NetworkSceneManager.LoadScene is server-only, so the request is relayed through the
/// local player's own NetworkPlayer RPC rather than called directly here.
///
/// LevelEditor.unity can also be opened directly in the Editor (bypassing Boot/Hub
/// entirely) for solo dev work -- in that case there's no NetworkManager session to
/// return to, so "Leave to Hub" falls back to loading MainMenu instead.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button     resumeButton;
    [SerializeField] private Button     leaveToHubButton;

    private bool _paused;

    private void Awake()
    {
        if (resumeButton     != null) resumeButton.onClick.AddListener(Resume);
        if (leaveToHubButton != null) leaveToHubButton.onClick.AddListener(LeaveToHub);
        if (pausePanel != null) pausePanel.SetActive(false);
    }

    private void Update()
    {
        if (!(Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)) return;

        if (_paused) Resume();
        else Pause();
    }

    private void Pause()
    {
        _paused = true;
        if (pausePanel != null) pausePanel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        GameEvents.FireGamePaused();
    }

    private void Resume()
    {
        _paused = false;
        if (pausePanel != null) pausePanel.SetActive(false);
        GameEvents.FireGameResumed();
    }

    private void LeaveToHub()
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
