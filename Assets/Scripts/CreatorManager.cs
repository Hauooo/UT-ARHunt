using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CreatorManager : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private Button placeTreasureButton;
    [SerializeField] private Button saveSetButton;
    [SerializeField] private Button confirmSaveButton;
    [SerializeField] private Button backToMenuButton;
    [SerializeField] private TMP_Text treasureCountText;
    [SerializeField] private GameObject savePanel;
    [SerializeField] private TMP_InputField setNameInput;

    // References to our singleton managers
    private GameManager gameManager;
    private LocationManager locationManager;

    void Start()
    {
        // Get the instances of our persistent managers
        gameManager = GameManager.Instance;
        locationManager = LocationManager.Instance;

        // Make sure we are in the correct mode
        if (gameManager == null || gameManager.CurrentMode != GameMode.CreatingSet)
        {
            Debug.LogError("CreatorScene loaded in wrong mode or GameManager is missing!");
            // Fallback to return to menu
            if (gameManager != null) gameManager.ReturnToMenu();
            return;
        }

        // Wire up the button clicks
        placeTreasureButton.onClick.AddListener(PlaceTreasure);
        saveSetButton.onClick.AddListener(ShowSavePanel);
        confirmSaveButton.onClick.AddListener(ConfirmSave);
        backToMenuButton.onClick.AddListener(gameManager.ExitCreatorMode); // Use the GameManager's exit function

        savePanel.SetActive(false);
        UpdateUI();
    }

    private void PlaceTreasure()
    {
        // Check if GPS is ready
        if (locationManager.Status != LocationManager.LocationStatus.Ready)
        {
            Debug.LogWarning("Location is not ready yet.");
            return;
        }

        // Create a new treasure data object with the current location
        var newTreasure = new TreasureManagerGPS_Multiplayer.TreasureData
        {
            name = $"Treasure #{gameManager.newSetTreasure.Count + 1}",
            lat = locationManager.Latitude,
            lon = locationManager.Longitude,
            points = 100 // Default points, can be changed later
        };

        // Add it to the temporary list held by the GameManager
        gameManager.newSetTreasure.Add(newTreasure);

        Debug.Log($"Placed treasure at ({newTreasure.lat}, {newTreasure.lon})");
        UpdateUI();
    }

    private void ShowSavePanel()
    {
        if (gameManager.newSetTreasure.Count == 0)
        {
            Debug.LogWarning("Cannot save an empty set. Place at least one treasure.");
            return;
        }
        savePanel.SetActive(true);
    }

    private void ConfirmSave()
    {
        string setName = setNameInput.text;
        if (string.IsNullOrWhiteSpace(setName))
        {
            Debug.LogWarning("Please enter a name for the treasure set.");
            return;
        }

        // Tell the GameManager to handle the Firebase save operation
        gameManager.SaveNewTreasureSet(setName);

        // The GameManager will handle returning to the menu upon successful save.
        confirmSaveButton.interactable = false; // Prevent double clicks
    }

    private void UpdateUI()
    {
        treasureCountText.text = $"Treasures Placed: {gameManager.newSetTreasure.Count}";
    }
}