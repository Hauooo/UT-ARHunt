using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Allows creators to select a treasure set to upload
/// </summary>
public class LevelSetSelector : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject selectorPanel;
    [SerializeField] private TMP_Dropdown setDropdown;
    [SerializeField] private Button selectButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text treasureCountText;
    [SerializeField] private TMP_Text feedbackText;

    private TreasureManagerGPS_Multiplayer treasureManager;
    private Dictionary<string, List<TreasureManagerGPS_Multiplayer.TreasureData>> availableSets;

    private void Start()
    {
        treasureManager = FindObjectOfType<TreasureManagerGPS_Multiplayer>();
        SetupButtons();
    }

    private void SetupButtons()
    {
        if (selectButton != null)
        {
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(OnSelectClicked);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(OnCancelClicked);
        }

        if (setDropdown != null)
        {
            setDropdown.onValueChanged.RemoveAllListeners();
            setDropdown.onValueChanged.AddListener(OnSetSelected);
        }
    }

    public void OpenSelector()
    {
        if (selectorPanel != null)
            selectorPanel.SetActive(true);

        LoadAvailableSets();
        Debug.Log("[LevelSetSelector] Selector opened");
    }

    private void LoadAvailableSets()
    {
        // For now, we only have one set available (the current one being edited)
        if (treasureManager != null)
        {
            var treasures = treasureManager.GetAllTreasures();

            if (treasures.Count > 0)
            {
                if (setDropdown != null)
                {
                    setDropdown.ClearOptions();
                    setDropdown.AddOptions(new List<string> { "Current Treasure Set" });
                }

                UpdateTreasureCount(treasures.Count);
                ShowFeedback($"Ready to upload {treasures.Count} treasures");
                Debug.Log($"[LevelSetSelector] Loaded {treasures.Count} treasures");
            }
            else
            {
                ShowFeedback("No treasures in current set");
                Debug.LogWarning("[LevelSetSelector] No treasures available");
            }
        }
        else
        {
            ShowFeedback("Treasure manager not found");
            Debug.LogError("[LevelSetSelector] TreasureManager not found");
        }
    }

    private void OnSetSelected(int index)
    {
        if (treasureManager != null)
        {
            var treasures = treasureManager.GetAllTreasures();
            UpdateTreasureCount(treasures.Count);
            Debug.Log($"[LevelSetSelector] Selected set with {treasures.Count} treasures");
        }
    }

    private void UpdateTreasureCount(int count)
    {
        if (treasureCountText != null)
        {
            treasureCountText.text = $"Treasures in set: {count}";
        }
    }

    private void OnSelectClicked()
    {
        if (treasureManager == null)
        {
            ShowFeedback("Treasure manager not found");
            return;
        }

        var treasures = treasureManager.GetAllTreasures();
        if (treasures.Count == 0)
        {
            ShowFeedback("No treasures to upload");
            return;
        }

        // Close this panel and open upload panel
        CloseSelector();

        // Get the LevelUploadManager and open it with the treasures
        var uploadManager = FindObjectOfType<LevelUploadManager>();
        if (uploadManager != null)
        {
            uploadManager.OpenUploadPanel(treasures);
            Debug.Log($"[LevelSetSelector] Opening upload panel with {treasures.Count} treasures");
        }
        else
        {
            Debug.LogError("[LevelSetSelector] LevelUploadManager not found");
        }
    }

    private void OnCancelClicked()
    {
        CloseSelector();
    }

    private void CloseSelector()
    {
        if (selectorPanel != null)
            selectorPanel.SetActive(false);

        Debug.Log("[LevelSetSelector] Selector closed");
    }

    private void ShowFeedback(string message)
    {
        if (feedbackText != null)
            feedbackText.text = message;

        Debug.Log("[LevelSetSelector] " + message);
    }
}