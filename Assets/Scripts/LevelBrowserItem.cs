using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// Represents a single level item in the browser list
/// </summary>
public class LevelBrowserItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI levelNameText;
    [SerializeField] private TextMeshProUGUI creatorNameText;
    [SerializeField] private TextMeshProUGUI treasureCountText;
    [SerializeField] private TextMeshProUGUI difficultyText;
    [SerializeField] private TextMeshProUGUI playsText;
    [SerializeField] private Button playButton;

    private LevelBrowserManager.LevelData levelData;
    private Action<string> onPlayCallback;

    public void Setup(LevelBrowserManager.LevelData data, Action<string> playCallback)
    {
        levelData = data;
        onPlayCallback = playCallback;

        // Update UI
        if (levelNameText != null)
            levelNameText.text = data.name;

        if (creatorNameText != null)
            creatorNameText.text = $"by {data.creatorName}";

        if (treasureCountText != null)
            treasureCountText.text = $"💎 {data.treasureCount}";

        if (difficultyText != null)
            difficultyText.text = data.difficulty;

        if (playsText != null)
            playsText.text = $"▶️ {data.plays}";

        if (playButton != null)
        {
            playButton.onClick.RemoveAllListeners();
            playButton.onClick.AddListener(OnPlayClicked);
        }

        Debug.Log($"[LevelItem] Setup level: {data.name}");
    }

    private void OnPlayClicked()
    {
        if (onPlayCallback != null)
        {
            onPlayCallback.Invoke(levelData.levelId);
        }
    }
}