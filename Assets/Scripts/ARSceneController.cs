using UnityEngine;

public class ARSceneController : MonoBehaviour
{
    [Header("Mode-Specific UI Panels")]
    [SerializeField] private GameObject creatorUIPanel; // UI with "Place Treasure" and "Save Set" buttons
    [SerializeField] private GameObject playerUIPanel;  // UI with the arrow, distance, and collect button

    // References to your managers
    private GameManager gameManager;
    private TreasureManagerGPS_Multiplayer treasureManager;

    // In ARSceneController.cs

    void Start()
    {
        // Get the instances of our persistent managers
        gameManager = GameManager.Instance;
        treasureManager = FindObjectOfType<TreasureManagerGPS_Multiplayer>();

        if (gameManager == null)
        {
            Debug.LogError("FATAL: GameManager not found! Returning to menu.");
            // In a real build, you might load the Boot scene here.
            // For now, this prevents a crash.
            return;
        }

        // Check the game mode and enable the correct UI and logic
        switch (gameManager.CurrentMode)
        {
            // FIXED: Access the enum directly, not through the GameManager class.
            case GameMode.CreatingSet:
                SetupForCreatorMode();
                break;

            // FIXED: Access the enum directly here as well.
            case GameMode.PlayingInRoom:
                SetupForPlayerMode();
                break;

            default: // Includes InMenu, which shouldn't happen here
                Debug.LogWarning("ARScene loaded in an invalid mode. Returning to menu.");
                gameManager.ReturnToMenu();
                break;
        }
    }

    private void SetupForCreatorMode()
    {
        Debug.Log("AR Scene: Setting up for Creator Mode.");
        creatorUIPanel.SetActive(true);
        playerUIPanel.SetActive(false);

        // Disable the TreasureManager's player logic
        if (treasureManager != null)
        {
            treasureManager.enabled = false;
        }

        // Your logic for placing treasures would be controlled from here.
        // For example, a button on the creatorUIPanel would call a function
        // to add the current GPS location to the GameManager's list.
    }

    private void SetupForPlayerMode()
    {
        Debug.Log("AR Scene: Setting up for Player Mode.");

        if (creatorUIPanel != null) creatorUIPanel.SetActive(false);
        if (playerUIPanel != null) playerUIPanel.SetActive(true);

        if (string.IsNullOrEmpty(gameManager.CurrentRoomId))
        {
            Debug.LogError("[ARSceneController] CurrentRoomId is null/empty in PlayingInRoom mode. Returning to menu.");
            gameManager.ReturnToMenu();
            return;
        }

        if (treasureManager == null)
        {
            Debug.LogError("[ARSceneController] TreasureManagerGPS_Multiplayer not found in ARScene. Returning to menu.");
            gameManager.ReturnToMenu();
            return;
        }

        treasureManager.enabled = true;
        treasureManager.InitializeForRoom(gameManager.CurrentRoomId);
    }
}