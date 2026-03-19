using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Handles UI interactions on the Scoreboard scene
/// </summary>
public class ScoreboardUIController : MonoBehaviour
{
    [Header("UI Buttons")]
    [SerializeField] private Button returnToMenuButton;
    [SerializeField] private Button shareScoreButton;

    [Header("Score Display")]
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI finalScoreText;

    [Header("Feedback")]
    [SerializeField] private TextMeshProUGUI feedbackText;

    private void Start()
    {
        SetupButtons();
    }

    private void SetupButtons()
    {
        if (returnToMenuButton != null)
        {
            returnToMenuButton.onClick.RemoveAllListeners();
            returnToMenuButton.onClick.AddListener(OnReturnToMenuClicked);
        }

        if (shareScoreButton != null)
        {
            shareScoreButton.onClick.RemoveAllListeners();
            shareScoreButton.onClick.AddListener(OnShareScoreClicked);
        }
    }

    private void OnReturnToMenuClicked()
    {
        Debug.Log("[ScoreboardUI] Return to Menu button clicked");

        if (GameManager.Instance != null)
        {
            GameManager.Instance.LeaveScoreboard();
        }
        else
        {
            Debug.LogError("[ScoreboardUI] GameManager.Instance is null!");
            UnityEngine.SceneManagement.SceneManager.LoadScene("MenuScene");
        }
    }

    private void OnShareScoreClicked()
    {
        Debug.Log("[ScoreboardUI] Share Score button clicked");

        if (ScoreManager.Instance != null)
        {
            string playerName = ScoreManager.Instance.GetPlayerName();
            int score = ScoreManager.Instance.GetScore();
            string message = $"I scored {score} points in UT ARHunt as {playerName} 🎮";

            // Copy to clipboard
            GUIUtility.systemCopyBuffer = message;
            ShowFeedback("Score copied to clipboard! 📋");
            Debug.Log($"[ScoreboardUI] Message copied: {message}");
        }
    }

    private void ShowFeedback(string message)
    {
        if (feedbackText != null)
        {
            feedbackText.text = message;
            Invoke(nameof(ClearFeedback), 3f);
        }
    }

    private void ClearFeedback()
    {
        if (feedbackText != null)
            feedbackText.text = "";
    }
}