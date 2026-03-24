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
    [SerializeField] private Button playButton;

    private string levelId;
    private Action<string> onPlayClicked;

    public void Setup(LevelBrowserManager.LevelData levelData, Action<string> playCallback)
    {
        levelId = levelData.levelId;
        onPlayClicked = playCallback;

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

        // Wire play button
        if (playButton != null)
        {
            playButton.onClick.RemoveAllListeners();
            playButton.onClick.AddListener(OnPlayButtonClicked);
        }

        Debug.Log($"[LevelItem] Setup level: {levelData.name}");
    }

    private void OnPlayButtonClicked()
    {
        Debug.Log($"[LevelItem] Play button clicked for level: {levelId}");
        onPlayClicked?.Invoke(levelId);
    }
}