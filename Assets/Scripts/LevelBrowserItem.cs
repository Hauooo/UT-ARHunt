using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// Displays a single level in the browse list
/// </summary>
public class LevelBrowserItem : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text treasureCountText;
    [SerializeField] private TMP_Text playsCountText;
    [SerializeField] private TMP_Text difficultyText;
    [SerializeField] private TMP_Text levelNameText;
    [SerializeField] private TMP_Text levelInfoText;
    [SerializeField] private Button selectButton;

    private string levelId;
    private Action<string> onSelectClicked;

    public void Setup(LevelBrowserManager.LevelData levelData, System.Action<string> selectCallback, double distanceInMeters = double.MaxValue)
    {
        levelId = levelData.levelId;
        onSelectClicked = selectCallback;  // ← Changed from playCallback to selectCallback

        // Set text values
        if (titleText != null)
            titleText.text = levelData.name;

        if (descriptionText != null)
            descriptionText.text = levelData.description;

        if (treasureCountText != null)
            treasureCountText.text = $"Treasures: {levelData.treasureCount}";

        if (playsCountText != null)
            playsCountText.text = $"Plays: {levelData.plays}";

        if (difficultyText != null)
            difficultyText.text = $"Difficulty: {levelData.difficulty}";

        // Add distance to title
        string distanceText = distanceInMeters < double.MaxValue
            ? $" • {distanceInMeters:F1}m away"
            : "";

        if (levelNameText != null)
            levelNameText.text = levelData.name + distanceText;

        if (levelInfoText != null)
            levelInfoText.text = $"By {levelData.creatorName}\n{levelData.treasureCount} treasures • {levelData.difficulty}\n👁 {levelData.plays} • ⭐ {levelData.rating}";

        // Wire select button
        if (selectButton != null)  // ← Changed from playButton to selectButton
        {
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(OnSelectButtonClicked);  // ← Changed method name
        }

        Debug.Log($"[LevelItem] Setup level: {levelData.name}");
    }

    private void OnSelectButtonClicked()  // ← Changed from OnPlayButtonClicked
    {
        Debug.Log($"[LevelItem] Select button clicked for level: {levelId}");
        onSelectClicked?.Invoke(levelId);
    }
}