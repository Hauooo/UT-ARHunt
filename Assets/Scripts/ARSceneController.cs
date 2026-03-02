using UnityEngine;
using UnityEngine.UI;

public class ARSceneController : MonoBehaviour
{
    [Header("Mode-Specific UI Panels")]
    [SerializeField] private GameObject creatorUIPanel;
    [SerializeField] private GameObject playerUIPanel;

    [Header("AR Scene Buttons")]
    [SerializeField] private Button endGameButton; // drag your End Game button here

    private GameManager gameManager;
    private TreasureManagerGPS_Multiplayer treasureManager;

    void Start()
    {
        gameManager = GameManager.Instance;
        treasureManager = FindObjectOfType<TreasureManagerGPS_Multiplayer>();

        if (gameManager == null)
        {
            Debug.LogError("FATAL: GameManager not found! Returning to menu.");
            return;
        }

        // Wire End Game button once
        SetupEndGameButton();

        switch (gameManager.CurrentMode)
        {
            case GameMode.CreatingSet:
                SetupForCreatorMode();
                break;
            case GameMode.PlayingInRoom:
                SetupForPlayerMode();
                break;
            default:
                Debug.LogWarning("ARScene loaded in an invalid mode. Returning to menu.");
                gameManager.ReturnToMenu();
                break;
        }
    }

    private void SetupEndGameButton()
    {
        if (endGameButton == null) return;

        // Only host should see/use this
        endGameButton.gameObject.SetActive(gameManager.IsHost);

        endGameButton.onClick.RemoveAllListeners();
        endGameButton.onClick.AddListener(OnEndGameClicked);
    }

    public void OnEndGameClicked()
    {
        if (gameManager == null) gameManager = GameManager.Instance;
        if (gameManager == null) return;

        gameManager.EndGame();
    }

    private void SetupForCreatorMode()
    {
        Debug.Log("AR Scene: Setting up for Creator Mode.");
        if (creatorUIPanel != null) creatorUIPanel.SetActive(true);
        if (playerUIPanel != null) playerUIPanel.SetActive(false);

        if (treasureManager != null) treasureManager.enabled = false;
    }

    private void SetupForPlayerMode()
    {
        Debug.Log("AR Scene: Setting up for Player Mode.");
        if (creatorUIPanel != null) creatorUIPanel.SetActive(false);
        if (playerUIPanel != null) playerUIPanel.SetActive(true);

        if (string.IsNullOrEmpty(gameManager.CurrentRoomId))
        {
            Debug.LogError("[ARSceneController] CurrentRoomId is null/empty. Returning to menu.");
            gameManager.ReturnToMenu();
            return;
        }

        if (treasureManager == null)
        {
            Debug.LogError("[ARSceneController] TreasureManagerGPS_Multiplayer not found. Returning to menu.");
            gameManager.ReturnToMenu();
            return;
        }

        treasureManager.enabled = true;
        treasureManager.InitializeForRoom(gameManager.CurrentRoomId);
    }
}