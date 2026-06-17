using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Manages all main menu panels.
/// Panels: Main → Host → New Game / Load Save, or Main → Join.
/// After lobby is created or joined, host loads Hub. Client is transported automatically.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject hostPanel;
    [SerializeField] private GameObject newGamePanel;
    [SerializeField] private GameObject loadSavePanel;
    [SerializeField] private GameObject joinPanel;
    [SerializeField] private GameObject optionsPanel;

    [Header("Main Panel")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button exitButton;

    [Header("Host Panel")]
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button loadSaveButton;
    [SerializeField] private Button backFromHostButton;

    [Header("New Game Panel")]
    [SerializeField] private TMP_InputField saveNameInput;
    [SerializeField] private Button[]       playerCountButtons; // assign 3 buttons: 2, 3, 4 players
    [SerializeField] private TextMeshProUGUI playerCountLabel;
    [SerializeField] private Button          createButton;
    [SerializeField] private Button          backFromNewGameButton;

    [Header("Load Save Panel")]
    [SerializeField] private GameObject[]    saveCards;          // 3 card root GameObjects
    [SerializeField] private TextMeshProUGUI[] saveCardNames;    // name label per card
    [SerializeField] private TextMeshProUGUI[] saveCardDates;    // last played label per card
    [SerializeField] private Button[]        saveCardPlayButtons;
    [SerializeField] private Button[]        saveCardDeleteButtons;
    [SerializeField] private Button          prevPageButton;
    [SerializeField] private Button          nextPageButton;
    [SerializeField] private TextMeshProUGUI pageLabel;
    [SerializeField] private Button          backFromLoadButton;

    [Header("Join Panel")]
    [SerializeField] private TMP_InputField  codeInput;
    [SerializeField] private Button          joinConfirmButton;
    [SerializeField] private Button          backFromJoinButton;
    [SerializeField] private TextMeshProUGUI joinErrorText;

    [Header("Shared")]
    [SerializeField] private TextMeshProUGUI statusText;

    private int _selectedPlayerCount = 4;
    private int _currentPage         = 0;
    private const int CARDS_PER_PAGE = 3;

    private void Start()
    {
        // Main panel
        hostButton.onClick.AddListener(()    => ShowPanel(hostPanel));
        joinButton.onClick.AddListener(()    => ShowPanel(joinPanel));
        optionsButton.onClick.AddListener(() => ShowPanel(optionsPanel));
        exitButton.onClick.AddListener(Application.Quit);

        // Host panel
        newGameButton.onClick.AddListener(()  => ShowPanel(newGamePanel));
        loadSaveButton.onClick.AddListener(OpenLoadSave);
        backFromHostButton.onClick.AddListener(() => ShowPanel(mainPanel));

        // New game panel
        for (int i = 0; i < playerCountButtons.Length; i++)
        {
            int count = i + 2; // maps to 2, 3, 4
            playerCountButtons[i].onClick.AddListener(() => SetPlayerCount(count));
        }
        createButton.onClick.AddListener(OnCreateClicked);
        backFromNewGameButton.onClick.AddListener(() => ShowPanel(hostPanel));

        // Load save panel
        for (int i = 0; i < saveCardPlayButtons.Length; i++)
        {
            int idx = i;
            saveCardPlayButtons[idx].onClick.AddListener(()   => OnPlaySaveClicked(idx));
            saveCardDeleteButtons[idx].onClick.AddListener(() => OnDeleteSaveClicked(idx));
        }
        prevPageButton.onClick.AddListener(() => ChangePage(-1));
        nextPageButton.onClick.AddListener(() => ChangePage(1));
        backFromLoadButton.onClick.AddListener(() => ShowPanel(hostPanel));

        // Join panel
        codeInput.characterLimit      = 6;
        codeInput.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
        joinConfirmButton.onClick.AddListener(OnJoinClicked);
        backFromJoinButton.onClick.AddListener(() => ShowPanel(mainPanel));

        // Lobby events
        SteamLobbyManager.OnLobbyCreated += OnLobbyCreated;
        SteamLobbyManager.OnLobbyJoined  += OnLobbyJoined;
        SteamLobbyManager.OnJoinFailed   += OnJoinFailed;

        SetPlayerCount(4);
        ShowPanel(mainPanel);
        UnlockCursor();
    }

    private void OnDestroy()
    {
        SteamLobbyManager.OnLobbyCreated -= OnLobbyCreated;
        SteamLobbyManager.OnLobbyJoined  -= OnLobbyJoined;
        SteamLobbyManager.OnJoinFailed   -= OnJoinFailed;
    }

    // -------------------------------------------------------------------------
    // New Game
    // -------------------------------------------------------------------------

    private void SetPlayerCount(int count)
    {
        _selectedPlayerCount = count;
        if (playerCountLabel != null) playerCountLabel.text = count.ToString();
    }

    private void OnCreateClicked()
    {
        string name = saveNameInput.text.Trim();
        if (string.IsNullOrEmpty(name)) { SetStatus("Enter a name for your game."); return; }

        var slot = new SaveSlot { slotName = name };
        slot.UpdateLastPlayed();
        SaveManager.Instance.Data.saveSlots.Add(slot);
        SaveManager.Instance.Data.activeSlotIndex = SaveManager.Instance.Data.saveSlots.Count - 1;
        SaveManager.Instance.Save();

        SetStatus("Creating lobby...");
        SetInteractable(false);
        SteamLobbyManager.Instance.CreateLobby();
    }

    // -------------------------------------------------------------------------
    // Load Save
    // -------------------------------------------------------------------------

    private void OpenLoadSave()
    {
        _currentPage = 0;
        RefreshSaveCards();
        ShowPanel(loadSavePanel);
    }

    private void RefreshSaveCards()
    {
        var slots      = SaveManager.Instance.Data.saveSlots;
        int totalPages = Mathf.Max(1, Mathf.CeilToInt(slots.Count / (float)CARDS_PER_PAGE));
        _currentPage   = Mathf.Clamp(_currentPage, 0, totalPages - 1);

        for (int i = 0; i < saveCards.Length; i++)
        {
            int slotIndex = _currentPage * CARDS_PER_PAGE + i;
            bool hasSlot  = slotIndex < slots.Count;

            saveCards[i].SetActive(hasSlot);
            if (!hasSlot) continue;

            saveCardNames[i].text = slots[slotIndex].slotName;
            saveCardDates[i].text = slots[slotIndex].lastPlayed;
        }

        prevPageButton.interactable = _currentPage > 0;
        nextPageButton.interactable = (_currentPage + 1) * CARDS_PER_PAGE < slots.Count;
        if (pageLabel != null)
            pageLabel.text = totalPages > 1 ? $"{_currentPage + 1} / {totalPages}" : "";
    }

    private void OnPlaySaveClicked(int cardIndex)
    {
        int slotIndex = _currentPage * CARDS_PER_PAGE + cardIndex;
        var slots     = SaveManager.Instance.Data.saveSlots;
        if (slotIndex >= slots.Count) return;

        slots[slotIndex].UpdateLastPlayed();
        SaveManager.Instance.Data.activeSlotIndex = slotIndex;
        SaveManager.Instance.Save();

        SetStatus("Creating lobby...");
        SetInteractable(false);
        SteamLobbyManager.Instance.CreateLobby();
    }

    private void OnDeleteSaveClicked(int cardIndex)
    {
        int slotIndex = _currentPage * CARDS_PER_PAGE + cardIndex;
        var slots     = SaveManager.Instance.Data.saveSlots;
        if (slotIndex >= slots.Count) return;

        slots.RemoveAt(slotIndex);
        if (SaveManager.Instance.Data.activeSlotIndex >= slots.Count)
            SaveManager.Instance.Data.activeSlotIndex = -1;

        SaveManager.Instance.Save();
        RefreshSaveCards();
    }

    private void ChangePage(int dir)
    {
        _currentPage += dir;
        RefreshSaveCards();
    }

    // -------------------------------------------------------------------------
    // Join
    // -------------------------------------------------------------------------

    private void OnJoinClicked()
    {
        string code = codeInput.text.Trim().ToUpper();
        if (code.Length != 6)
        {
            if (joinErrorText != null) joinErrorText.text = "Code must be 6 characters.";
            return;
        }

        if (joinErrorText != null) joinErrorText.text = "";
        SetStatus("Joining...");
        SetInteractable(false);
        SteamLobbyManager.Instance.JoinByCode(code);
    }

    // -------------------------------------------------------------------------
    // Lobby callbacks
    // -------------------------------------------------------------------------

    private void OnLobbyCreated(string code)
    {
        // Host immediately loads hub -- NGO scene manager brings clients along
        NetworkManager.Singleton.SceneManager.LoadScene("Hub", LoadSceneMode.Single);
    }

    private void OnLobbyJoined()
    {
        // Client just waits -- host's scene manager loads hub for everyone
        SetStatus("Joined! Loading hub...");
    }

    private void OnJoinFailed(string reason)
    {
        SetStatus("");
        if (joinErrorText != null) joinErrorText.text = reason;
        SetInteractable(true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void ShowPanel(GameObject target)
    {
        mainPanel.SetActive(target == mainPanel);
        hostPanel.SetActive(target == hostPanel);
        newGamePanel.SetActive(target == newGamePanel);
        loadSavePanel.SetActive(target == loadSavePanel);
        joinPanel.SetActive(target == joinPanel);
        if (optionsPanel != null) optionsPanel.SetActive(target == optionsPanel);
        SetStatus("");
        if (joinErrorText != null) joinErrorText.text = "";
    }

    private void SetStatus(string msg) { if (statusText != null) statusText.text = msg; }

    private void SetInteractable(bool state)
    {
        hostButton.interactable        = state;
        joinButton.interactable        = state;
        createButton.interactable      = state;
        joinConfirmButton.interactable = state;
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }
}
