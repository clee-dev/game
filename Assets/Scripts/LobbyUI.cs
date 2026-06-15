using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Controls all lobby and game UI.
///
/// Three panels:
///   PreLobbyPanel  -- create lobby / join by code
///   InLobbyPanel   -- waiting room, host sees Start Game button
///   PausePanel     -- shown when ESC is pressed during gameplay
///
/// Cursor is always unlocked except when the game is actively running.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [Header("Pre-Lobby Panel")]
    [SerializeField] private GameObject      preLobbyPanel;
    [SerializeField] private Button          createButton;
    [SerializeField] private TMP_InputField  codeInputField;
    [SerializeField] private Button          joinButton;
    [SerializeField] private TextMeshProUGUI errorText;

    [Header("In-Lobby Panel")]
    [SerializeField] private GameObject      inLobbyPanel;
    [SerializeField] private TextMeshProUGUI roomCodeDisplay;
    [SerializeField] private Button          copyCodeButton;
    [SerializeField] private TextMeshProUGUI playerListText;
    [SerializeField] private Button          startGameButton;
    [SerializeField] private TextMeshProUGUI waitingForHostText;
    [SerializeField] private Button          leaveFromLobbyButton;

    [Header("Pause Panel")]
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button     resumeButton;
    [SerializeField] private Button     leaveFromPauseButton;

    private bool _gameActive;

    private void OnEnable()
    {
        SteamLobbyManager.OnLobbyCreated += HandleLobbyCreated;
        SteamLobbyManager.OnLobbyJoined  += HandleLobbyJoined;
        SteamLobbyManager.OnJoinFailed   += HandleJoinFailed;
        SteamLobbyManager.OnPlayerJoined += HandlePlayerJoined;
        SteamLobbyManager.OnPlayerLeft   += HandlePlayerLeft;
        GameEvents.OnGameStarted         += HandleGameStarted;
    }

    private void OnDisable()
    {
        SteamLobbyManager.OnLobbyCreated -= HandleLobbyCreated;
        SteamLobbyManager.OnLobbyJoined  -= HandleLobbyJoined;
        SteamLobbyManager.OnJoinFailed   -= HandleJoinFailed;
        SteamLobbyManager.OnPlayerJoined -= HandlePlayerJoined;
        SteamLobbyManager.OnPlayerLeft   -= HandlePlayerLeft;
        GameEvents.OnGameStarted         -= HandleGameStarted;
    }

    private void Start()
    {
        createButton.onClick.AddListener(OnCreateClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        copyCodeButton.onClick.AddListener(OnCopyCodeClicked);
        startGameButton.onClick.AddListener(OnStartGameClicked);
        leaveFromLobbyButton.onClick.AddListener(OnLeaveFromLobbyClicked);
        resumeButton.onClick.AddListener(OnResumeClicked);
        leaveFromPauseButton.onClick.AddListener(OnLeaveFromPauseClicked);

        codeInputField.characterLimit      = 6;
        codeInputField.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;

        ShowPreLobby();
    }

    private void Update()
    {
        if (_gameActive && (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false))
        {
            if (pausePanel.activeSelf)
                OnResumeClicked();
            else
                ShowPause();
        }
    }

    private void OnCreateClicked()
    {
        ClearError();
        SetButtonsInteractable(false);
        SteamLobbyManager.Instance.CreateLobby();
    }

    private void OnJoinClicked()
    {
        ClearError();
        string code = codeInputField.text.Trim().ToUpper();
        if (string.IsNullOrEmpty(code)) { ShowError("Enter a room code first."); return; }
        SetButtonsInteractable(false);
        SteamLobbyManager.Instance.JoinByCode(code);
    }

    private void OnCopyCodeClicked()
    {
        GUIUtility.systemCopyBuffer = roomCodeDisplay.text;
        copyCodeButton.GetComponentInChildren<TextMeshProUGUI>().text = "Copied!";
        Invoke(nameof(ResetCopyText), 2f);
    }

    private void OnStartGameClicked()
    {
        ReadyManager.Instance?.StartGame();
    }

    private void OnLeaveFromLobbyClicked()
    {
        SteamLobbyManager.Instance.LeaveLobby();
        ShowPreLobby();
    }

    private void OnResumeClicked()
    {
        pausePanel.SetActive(false);
        GameEvents.FireGameResumed();
    }

    private void OnLeaveFromPauseClicked()
    {
        _gameActive = false;
        pausePanel.SetActive(false);
        SteamLobbyManager.Instance.LeaveLobby();
        ShowPreLobby();
    }

    private void HandleLobbyCreated(string code)
    {
        roomCodeDisplay.text = code;
        playerListText.text  = "Waiting for players...";
        ShowInLobby(isHost: true);
    }

    private void HandleLobbyJoined()
    {
        roomCodeDisplay.text = "";
        playerListText.text  = "Joined! Waiting for host to start...";
        ShowInLobby(isHost: false);
    }

    private void HandleJoinFailed(string reason)
    {
        ShowError(reason);
        SetButtonsInteractable(true);
    }

    private void HandlePlayerJoined(string playerName)
        => playerListText.text += $"\n{playerName} joined";

    private void HandlePlayerLeft(string playerName)
        => playerListText.text += $"\n{playerName} left";

    private void HandleGameStarted()
    {
        _gameActive = true;
        preLobbyPanel.SetActive(false);
        inLobbyPanel.SetActive(false);
        pausePanel.SetActive(false);
    }

    private void ShowPreLobby()
    {
        _gameActive = false;
        UnlockCursor();
        preLobbyPanel.SetActive(true);
        inLobbyPanel.SetActive(false);
        pausePanel.SetActive(false);
        SetButtonsInteractable(true);
        codeInputField.text = "";
        ClearError();
    }

    private void ShowInLobby(bool isHost)
    {
        UnlockCursor();
        preLobbyPanel.SetActive(false);
        inLobbyPanel.SetActive(true);
        pausePanel.SetActive(false);
        startGameButton.gameObject.SetActive(isHost);
        if (waitingForHostText != null)
            waitingForHostText.gameObject.SetActive(!isHost);
    }

    private void ShowPause()
    {
        pausePanel.SetActive(true);
        GameEvents.FireGamePaused();
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    private void ShowError(string msg)
    {
        if (errorText != null) errorText.text = msg;
    }

    private void ClearError()
    {
        if (errorText != null) errorText.text = "";
    }

    private void SetButtonsInteractable(bool state)
    {
        createButton.interactable = state;
        joinButton.interactable   = state;
    }

    private void ResetCopyText()
        => copyCodeButton.GetComponentInChildren<TextMeshProUGUI>().text = "Copy Code";
}