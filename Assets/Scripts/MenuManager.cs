using UnityEngine;
using UnityEngine.UI;

public class MenuManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject playerPanel;
    public GameObject creatorPanel;
    public GameObject usernamePanel;
    public GameObject levelBrowserPanel;
    public GameObject levelUploadPanel;
    public GameObject myLevelsPanel;           // ← ADD THIS

    public GameObject joinRoomPanel;
    public GameObject createRoomPanel;
    public GameObject hostLobbyPanel;
    public GameObject roomLobbyPanel;

    [Header("Managers")]
    [SerializeField] private LevelBrowserManager levelBrowserManager;
    [SerializeField] private LevelUploadManager levelUploadManager;
    [SerializeField] private LevelSetSelector levelSetSelector;
    [SerializeField] private MyLevelsManager myLevelsManager;       // ← ALREADY HERE

    [Header("Buttons")]
    [SerializeField] private Button myLevelsButton;

    [HideInInspector] public GameObject activePanel;

    void Start()
    {
        ShowPanel(mainMenuPanel);

        // Setup My Levels button
        if (myLevelsButton != null && myLevelsManager != null)
        {
            myLevelsButton.onClick.AddListener(OnMyLevelsButton);
            Debug.Log("[MenuManager] My Levels button setup");
        }
        else
        {
            Debug.LogError("[MenuManager] myLevelsButton or myLevelsManager not assigned!");
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnLobbyReady += HandleLobbyReady;
            GameManager.Instance.OnJoinFailed += HandleJoinFailed;
        }
        else
        {
            Debug.LogError("[MenuManager] GameManager.Instance is null in Start().");
        }
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnLobbyReady -= HandleLobbyReady;
            GameManager.Instance.OnJoinFailed -= HandleJoinFailed;
        }
    }

    public void ShowPanel(GameObject panel)
    {
        if (activePanel != null)
            activePanel.SetActive(false);

        activePanel = panel;

        if (activePanel == null)
        {
            Debug.LogError("[MenuManager] Tried to show a NULL panel. Check Inspector references.");
            return;
        }

        activePanel.SetActive(true);
        Debug.Log($"[MenuManager] Showing panel: {activePanel.name}");
    }

    // ==================== MAIN MENU ====================
    public void OnPlayButton() => ShowPanel(playerPanel);
    public void OnCreatorButton() => ShowPanel(creatorPanel);
    public void OnBackButton() => ShowPanel(mainMenuPanel);
    public void OnChangeUsernameButton() => ShowPanel(usernamePanel);

    // ==================== PLAYER SECTION ====================
    public void OnHostGameButton() => ShowPanel(createRoomPanel);

    public void OnBrowseLevelsButton()
    {
        ShowPanel(levelBrowserPanel);
        if (levelBrowserManager != null)
        {
            levelBrowserManager.OpenBrowser();
            Debug.Log("[MenuManager] Opened level browser");
        }
    }

    /// <summary>
    /// Open My Levels panel (NEW)
    /// </summary>
    public void OnMyLevelsButton()
    {
        ShowPanel(myLevelsPanel);
        if (myLevelsManager != null)
        {
            myLevelsManager.OpenMyLevels();
            Debug.Log("[MenuManager] Opened My Levels");
        }
        else
        {
            Debug.LogError("[MenuManager] MyLevelsManager not found");
        }
    }

    // ==================== CREATOR SECTION ====================
    public void OnUploadLevelButton()
    {
        if (levelSetSelector != null)
        {
            levelSetSelector.OpenSelector();
            Debug.Log("[MenuManager] Opened level set selector");
        }
        else
        {
            Debug.LogError("[MenuManager] LevelSetSelector not found");
        }
    }

    public void OnCreateButton()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.StartCreatorMode();
        }
    }

    // ==================== ROOM BUTTONS ====================
    public void OnJoinRoomButton() => ShowPanel(joinRoomPanel);
    public void OnCreateRoomButton() => ShowPanel(createRoomPanel);

    // ==================== CALLBACKS ====================
    private void HandleLobbyReady(string roomId, bool isHost)
    {
        Debug.Log($"[MenuManager] Lobby ready! Room ID: {roomId} isHost={isHost}");
        ShowPanel(roomLobbyPanel);
    }

    private void HandleJoinFailed(string reason)
    {
        Debug.LogWarning($"[MenuManager] Join failed: {reason}");
        ShowPanel(playerPanel != null ? playerPanel : mainMenuPanel);
    }
}