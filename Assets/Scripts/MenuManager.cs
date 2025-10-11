using UnityEngine;

public class MenuManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject joinRoomPanel;
    public GameObject createRoomPanel;
    public GameObject lobbyPanel;
    public GameObject creatorPanel;

    

    [HideInInspector] public GameObject activePanel;

    void Start()
    {
        ShowPanel(mainMenuPanel);
        GameManager.Instance.OnLobbyReady += HandleLobbyReady;
        GameManager.Instance.OnJoinFailed += HandleJoinFailed;
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
        panel.SetActive(true);
    }

    public void OnJoinRoomButton() => ShowPanel(joinRoomPanel);
    public void OnCreateRoomButton() => ShowPanel(createRoomPanel);
    public void OnBackButton() => ShowPanel(mainMenuPanel);
    public void OnLobbyButton() => ShowPanel(lobbyPanel);

    public void OnCreatorButton() => GameManager.Instance.StartCreatorMode();

    // --- Handlers for GameManager events ---
    private void HandleLobbyReady(string roomId, bool isHost)
    {
        Debug.Log($"Lobby ready! Room ID: {roomId}");
        ShowPanel(lobbyPanel);
    }

    private void HandleJoinFailed(string reason)
    {
        Debug.LogWarning($"Join failed: {reason}");
        ShowPanel(mainMenuPanel);
    }
}
