using UnityEngine;

public class MenuManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainMenuPanel;

    // NEW panels
    public GameObject playerPanel;
    public GameObject creatorPanel;

    // Existing panels (keep if still used elsewhere)
    public GameObject joinRoomPanel;
    public GameObject createRoomPanel;
    public GameObject hostLobbyPanel;
    public GameObject roomLobbyPanel;

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

    // MAIN MENU BUTTONS
    public void OnPlayButton() => ShowPanel(playerPanel);
    public void OnCreatorButton() => ShowPanel(creatorPanel);

    // COMMON
    public void OnBackButton() => ShowPanel(mainMenuPanel);

    // OPTIONAL: if playerPanel still has these buttons
    public void OnJoinRoomButton() => ShowPanel(joinRoomPanel);
    public void OnCreateRoomButton() => ShowPanel(createRoomPanel);

    public void OnCreateButton() => GameManager.Instance.StartCreatorMode();

    private void HandleLobbyReady(string roomId, bool isHost)
    {
        Debug.Log($"Lobby ready! Room ID: {roomId} isHost={isHost}");
        ShowPanel(roomLobbyPanel);
    }

    private void HandleJoinFailed(string reason)
    {
        Debug.LogWarning($"Join failed: {reason}");
        ShowPanel(playerPanel != null ? playerPanel : mainMenuPanel);
    }
}