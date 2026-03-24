using UnityEngine;

public class MenuManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject playerPanel;
    public GameObject creatorPanel;
    public GameObject usernamePanel;
    public GameObject levelBrowserPanel;    // ← NEW
    public GameObject levelUploadPanel;     // ← NEW

    public GameObject joinRoomPanel;
    public GameObject createRoomPanel;
    public GameObject hostLobbyPanel;
    public GameObject roomLobbyPanel;

    [Header("Managers")]
    [SerializeField] private LevelBrowserManager levelBrowserManager;  // ← NEW
    [SerializeField] private LevelUploadManager levelUploadManager;    // ← NEW
    [SerializeField] private LevelSetSelector levelSetSelector;      // ← NEW

    [HideInInspector] public GameObject activePanel;

    void Start()
    {
        ShowPanel(mainMenuPanel);

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