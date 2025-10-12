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
    [SerializeField] private Button cancelButton;

    [SerializeField] private TMP_Text treasureCountText;
    [SerializeField] private GameObject savePanel;
    [SerializeField] private TMP_InputField setNameInput;

    // This is our safe, private reference. The "Airlock".
    private GameManager gameManager;
    private LocationManager locationManager;

    void Start()
    {
        gameManager = GameManager.Instance;
        locationManager = LocationManager.Instance;

        if (gameManager == null)
        {
            Debug.LogError("No GameManager instance found in scene!");
            return;
        }

        Debug.Log($"CreatorManager linked to GameManager ID: {gameManager.GetInstanceID()} (Scene: {gameManager.gameObject.scene.name})");

        if (gameManager.CurrentMode != GameMode.CreatingSet)
        {
            Debug.LogWarning("CreatorScene loaded in wrong mode. Returning to menu...");
            gameManager.ReturnToMenu();
            return;
        }

        // Wire up buttons
        placeTreasureButton.onClick.AddListener(PlaceTreasure);
        saveSetButton.onClick.AddListener(ShowSavePanel);
        confirmSaveButton.onClick.AddListener(ConfirmSave);
        backToMenuButton.onClick.AddListener(gameManager.ExitCreatorMode);
        cancelButton.onClick.AddListener(CancelSave);

        savePanel.SetActive(false);
        UpdateUI();
    }


    private void PlaceTreasure()
    {
        if (locationManager.Status != LocationManager.LocationStatus.Ready)
        {
            Debug.LogWarning("Location is not ready yet.");
            return;
        }

        var newTreasure = new TreasureManagerGPS_Multiplayer.TreasureData
        {
            // Use the safe, cached reference
            name = $"Treasure #{gameManager.newSetTreasure.Count + 1}",
            lat = locationManager.Latitude,
            lon = locationManager.Longitude,
            points = 100
        };

        // Use the safe, cached reference
        gameManager.newSetTreasure.Add(newTreasure);

        Debug.Log($"Placed treasure at ({newTreasure.lat}, {newTreasure.lon})");
        UpdateUI();
    }

    private void ShowSavePanel()
    {
        // Use the safe, cached reference
        if (gameManager.newSetTreasure.Count == 0)
        {
            Debug.LogWarning("Cannot save an empty set. Place at least one treasure.");
            return;
        }
        savePanel.SetActive(true);
    }

    private void ConfirmSave()
    {
        // Final verification log
        Debug.LogWarning($"--- ConfirmSave called. Using cached gameManager with ID: {gameManager.GetInstanceID()} ---");

        string setName = setNameInput.text;
        if (string.IsNullOrWhiteSpace(setName))
        {
            Debug.LogWarning("Please enter a name for the treasure set.");
            return;
        }

        gameManager.SaveNewTreasureSet(setName);
        confirmSaveButton.interactable = false;
    }

    public void CancelSave()
    {
        savePanel.SetActive(false);
        confirmSaveButton.interactable = true;
    }

    private void UpdateUI()
    {
        // Use the safe, cached reference
        treasureCountText.text = $"Treasures Placed: {gameManager.newSetTreasure.Count}";
    }

    // Add this entire method to your GameManager.cs script

    
}