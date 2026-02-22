using UnityEngine;

public class MenuManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject joinRoomPanel;
    public GameObject createRoomPanel;

    public GameObject hostLobbyPanel;
    public GameObject roomLobbyPanel;

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

        if (activePanel == null)
        {
            Debug.LogError("[MenuManager] Tried to show a NULL panel. Check Inspector references.");
            return;
        }

        activePanel.SetActive(true);
    }

    public void OnJoinRoomButton() => ShowPanel(joinRoomPanel);
    public void OnCreateRoomButton() => ShowPanel(createRoomPanel);
    public void OnBackButton() => ShowPanel(mainMenuPanel);

    // If you want a button to open the room lobby explicitly:
    public void OnLobbyButton() => ShowPanel(roomLobbyPanel);

    public void OnCreatorButton() => GameManager.Instance.StartCreatorMode();

    private void HandleLobbyReady(string roomId, bool isHost)
    {
        Debug.Log($"Lobby ready! Room ID: {roomId} isHost={isHost}");
        ShowPanel(roomLobbyPanel);
    }

    private void HandleJoinFailed(string reason)
    {
        Debug.LogWarning($"Join failed: {reason}");
        ShowPanel(mainMenuPanel);
    }
}