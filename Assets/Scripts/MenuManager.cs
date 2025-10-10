using UnityEngine;

public class MenuManager : MonoBehaviour
{

    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject joinRoomPanel;
    public GameObject createRoomPanel;
    public GameObject lobbyPanel;
    public GameObject creatorPanel;


    public GameObject activePanel;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ShowPanel(mainMenuPanel);
    }

    void ShowPanel(GameObject panel)
    {
        if (activePanel != null)
        {
            activePanel.SetActive(false);
        }
        
        activePanel = panel;
        panel.SetActive(true);
    }

    public void OnJoinRoomButton()
    {
        ShowPanel(joinRoomPanel);
    }
    public void OnCreateRoomButton()
    {
        ShowPanel(createRoomPanel);
    }
    public void OnBackButton()
    {
        ShowPanel(mainMenuPanel);
    }
    public void OnLobbyButton()
    {
        ShowPanel(lobbyPanel);
    }
    public void OnCreatorButton()
    {
        GameManager.Instance.StartCreatorMode();
    }
    
}
