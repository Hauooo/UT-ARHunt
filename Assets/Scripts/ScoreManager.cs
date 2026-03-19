using UnityEngine;
using TMPro;
using Firebase.Auth;

/// <summary>
/// Manages player score from treasure collection.
/// Displays player username + score on scoreboard.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI scoreText;

    [Header("Score Settings")]
    [SerializeField] private int pointsPerTreasure = 10;

    private int currentScore = 0;
    private string playerName = "Player";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        LoadPlayerName();
        UpdateScoreboardUI();
    }

    /// <summary>
    /// Get player name from Firebase DisplayName
    /// </summary>
    private void LoadPlayerName()
    {
        var user = FirebaseAuth.DefaultInstance?.CurrentUser;

        if (user != null && !string.IsNullOrEmpty(user.DisplayName))
        {
            // Use the username set by UsernameSetupController
            playerName = user.DisplayName;
            Debug.Log($"[ScoreManager] Player name loaded: {playerName}");
        }
        else if (user != null)
        {
            // Fallback to shortened UserId if no DisplayName set
            playerName = "Player_" + user.UserId.Substring(0, Mathf.Min(5, user.UserId.Length));
            Debug.Log($"[ScoreManager] No DisplayName found. Using fallback: {playerName}");
        }
        else
        {
            Debug.LogWarning("[ScoreManager] No Firebase user found!");
            playerName = "Player";
        }
    }

    /// <summary>
    /// Called when a treasure is collected
    /// </summary>
    public void AddTreasurePoints()
    {
        currentScore += pointsPerTreasure;
        Debug.Log($"[ScoreManager] +{pointsPerTreasure} points. Total: {currentScore}");
        UpdateScoreboardUI();
    }

    /// <summary>
    /// Get current score
    /// </summary>
    public int GetScore()
    {
        return currentScore;
    }

    /// <summary>
    /// Get player name
    /// </summary>
    public string GetPlayerName()
    {
        return playerName;
    }

    /// <summary>
    /// Reset score (for new game)
    /// </summary>
    public void ResetScore()
    {
        currentScore = 0;
        UpdateScoreboardUI();
        Debug.Log("[ScoreManager] Score reset");
    }

    private void UpdateScoreboardUI()
    {
        if (playerNameText != null)
            playerNameText.text = playerName;

        if (scoreText != null)
            scoreText.text = currentScore.ToString();

        Debug.Log($"[ScoreManager] Updated scoreboard: {playerName} - {currentScore}");
    }
}