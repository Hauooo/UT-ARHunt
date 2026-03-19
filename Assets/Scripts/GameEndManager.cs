using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using Firebase.Auth;

/// <summary>
/// Handles game end state - shows when all treasures are collected
/// </summary>
public class GameEndManager : MonoBehaviour
{
    public static GameEndManager Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private GameObject gameEndPanel;
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI finalScoreText;
    [SerializeField] private TextMeshProUGUI treasureCountText;
    [SerializeField] private Button exitButton;
    [SerializeField] private Button leaderboardButton;

    [Header("Settings")]
    [SerializeField] private float delayBeforeShow = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (gameEndPanel != null)
            gameEndPanel.SetActive(false);

        if (exitButton != null)
            exitButton.onClick.AddListener(OnExitClicked);

        if (leaderboardButton != null)
            leaderboardButton.onClick.AddListener(OnLeaderboardClicked);
    }

    /// <summary>
    /// Call this when all treasures are collected
    /// </summary>
    public void EndGame(int finalScore, int treasuresCollected, int totalTreasures)
    {
        StartCoroutine(ShowEndGameScreen(finalScore, treasuresCollected, totalTreasures));
    }

    private IEnumerator ShowEndGameScreen(int finalScore, int treasuresCollected, int totalTreasures)
    {
        yield return new WaitForSeconds(delayBeforeShow);

        // Get player name from Firebase or ScoreManager
        string displayName = "Player";

        var user = FirebaseAuth.DefaultInstance?.CurrentUser;
        if (user != null && !string.IsNullOrEmpty(user.DisplayName))
        {
            displayName = user.DisplayName;
        }
        else if (ScoreManager.Instance != null)
        {
            displayName = ScoreManager.Instance.GetPlayerName();
        }

        if (playerNameText != null)
            playerNameText.text = displayName;

        if (finalScoreText != null)
            finalScoreText.text = finalScore.ToString();

        if (treasureCountText != null)
            treasureCountText.text = $"{treasuresCollected}/{totalTreasures}";

        if (gameEndPanel != null)
            gameEndPanel.SetActive(true);

        Debug.Log($"[GameEndManager] Game ended! Player: {displayName}, Score: {finalScore}, Treasures: {treasuresCollected}/{totalTreasures}");
    }

    private void OnExitClicked()
    {
        Debug.Log("[GameEndManager] Exit button clicked");
        // Return to main menu or lobby
        UnityEngine.SceneManagement.SceneManager.LoadScene("MenuScene");
    }

    private void OnLeaderboardClicked()
    {
        Debug.Log("[GameEndManager] Leaderboard button clicked");
        // TODO: Show leaderboard UI
    }

    public void HidePanel()
    {
        if (gameEndPanel != null)
            gameEndPanel.SetActive(false);
    }
}