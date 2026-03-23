using UnityEngine;
using TMPro;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;

/// <summary>
/// Manages player score and time from treasure collection.
/// Displays player username + score + time taken on scoreboard.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI timeTakenText;  // ← NEW

    [Header("Score Settings")]
    [SerializeField] private int pointsPerTreasure = 10;

    private int currentScore = 0;
    private string playerName = "Player";
    private DatabaseReference dbRef;

    private float gameStartTime = 0f;  // ← NEW: Track when game started
    private int secondsTaken = 0;       // ← NEW: Time to complete

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
        InitializeFirebase();
        LoadScoreAndTimeFromFirebase();
    }

    private void InitializeFirebase()
    {
        try
        {
            dbRef = FirebaseDatabase.GetInstance(
                "https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/")
                .RootReference;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[ScoreManager] Failed to initialize Firebase: " + ex);
        }
    }

    /// <summary>
    /// Get player name from Firebase DisplayName
    /// </summary>
    private void LoadPlayerName()
    {
        var user = FirebaseAuth.DefaultInstance?.CurrentUser;

        if (user != null && !string.IsNullOrEmpty(user.DisplayName))
        {
            playerName = user.DisplayName;
            Debug.Log($"[ScoreManager] Player name loaded: {playerName}");
        }
        else if (user != null)
        {
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
    /// Load score and time from Firebase asynchronously
    /// Score: rooms/{roomId}/scores/{userId}
    /// Time: rooms/{roomId}/players/{userId}/timeTakenSeconds
    /// </summary>
    private void LoadScoreAndTimeFromFirebase()
    {
        var user = FirebaseAuth.DefaultInstance?.CurrentUser;
        if (user == null)
        {
            Debug.LogWarning("[ScoreManager] No Firebase user. Using defaults.");
            UpdateScoreboardUI();
            return;
        }

        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[ScoreManager] GameManager not available.");
            UpdateScoreboardUI();
            return;
        }

        string roomId = GameManager.Instance.CurrentRoomId;
        if (string.IsNullOrEmpty(roomId))
        {
            Debug.LogWarning("[ScoreManager] No room ID.");
            UpdateScoreboardUI();
            return;
        }

        if (dbRef == null)
        {
            Debug.LogWarning("[ScoreManager] Firebase not initialized.");
            UpdateScoreboardUI();
            return;
        }

        Debug.Log($"[ScoreManager] Loading score from rooms/{roomId}/scores/{user.UserId}");
        Debug.Log($"[ScoreManager] Loading time from rooms/{roomId}/players/{user.UserId}");

        // ← LOAD SCORE from scores node (not players node!)
        dbRef.Child("rooms").Child(roomId)
             .Child("scores").Child(user.UserId)
             .GetValueAsync()
             .ContinueWithOnMainThread(scoreTask =>
             {
                 if (scoreTask.IsFaulted)
                 {
                     Debug.LogError("[ScoreManager] Failed to load score: " + scoreTask.Exception);
                 }
                 else if (scoreTask.IsCompleted && scoreTask.Result.Exists)
                 {
                     if (long.TryParse(scoreTask.Result.Value.ToString(), out long score))
                     {
                         currentScore = (int)score;
                         Debug.Log($"[ScoreManager] ✓ Loaded score from Firebase: {currentScore}");
                     }
                 }
                 else
                 {
                     Debug.Log("[ScoreManager] No score found in Firebase. Using default: 0");
                 }

                 // After loading score, load time
                 LoadTimeFromFirebase(user.UserId, roomId);
             });
    }

    /// <summary>
    /// Load time taken from Firebase
    /// </summary>
    private void LoadTimeFromFirebase(string userId, string roomId)
    {
        if (dbRef == null) return;

        // ← LOAD TIME from players node
        dbRef.Child("rooms").Child(roomId)
             .Child("players").Child(userId)
             .Child("timeTakenSeconds")
             .GetValueAsync()
             .ContinueWithOnMainThread(timeTask =>
             {
                 if (timeTask.IsFaulted)
                 {
                     Debug.LogError("[ScoreManager] Failed to load time: " + timeTask.Exception);
                 }
                 else if (timeTask.IsCompleted && timeTask.Result.Exists)
                 {
                     if (long.TryParse(timeTask.Result.Value.ToString(), out long time))
                     {
                         secondsTaken = (int)time;
                         Debug.Log($"[ScoreManager] ✓ Loaded time from Firebase: {secondsTaken}s");
                     }
                 }
                 else
                 {
                     Debug.Log("[ScoreManager] No time found in Firebase. Using default: 0");
                 }

                 // Now update UI with both score and time
                 UpdateScoreboardUI();
             });
    }

    /// <summary>
    /// Called when a treasure is collected (during gameplay)
    /// </summary>
    public void AddTreasurePoints()
    {
        currentScore += pointsPerTreasure;
        Debug.Log($"[ScoreManager] +{pointsPerTreasure} points. Total: {currentScore}");
        UpdateScoreboardUI();
    }

    /// <summary>
    /// Start the game timer (call this when game begins)
    /// </summary>
    public void StartGameTimer()
    {
        gameStartTime = Time.time;
        Debug.Log("[ScoreManager] Game timer started");
    }

    /// <summary>
    /// End the game timer and save time to Firebase (call this when all treasures collected)
    /// </summary>
    public void EndGameTimer()
    {
        if (gameStartTime > 0)
        {
            secondsTaken = (int)(Time.time - gameStartTime);
            Debug.Log($"[ScoreManager] Game completed in {secondsTaken} seconds");
            SaveTimeToFirebase();
            UpdateScoreboardUI();
        }
    }

    private void SaveTimeToFirebase()
    {
        if (dbRef == null) return;

        var user = FirebaseAuth.DefaultInstance?.CurrentUser;
        if (user == null) return;

        if (GameManager.Instance == null) return;

        string roomId = GameManager.Instance.CurrentRoomId;
        if (string.IsNullOrEmpty(roomId)) return;

        dbRef.Child("rooms").Child(roomId)
             .Child("players").Child(user.UserId)
             .Child("timeTakenSeconds")
             .SetValueAsync(secondsTaken)
             .ContinueWithOnMainThread(task =>
             {
                 if (task.IsCompleted)
                 {
                     Debug.Log($"[ScoreManager] Time saved to Firebase: {secondsTaken}s");
                 }
                 else if (task.IsFaulted)
                 {
                     Debug.LogError("[ScoreManager] Failed to save time: " + task.Exception);
                 }
             });
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
    /// Get time taken in seconds
    /// </summary>
    public int GetTimeTakenSeconds()
    {
        return secondsTaken;
    }

    /// <summary>
    /// Format time as MM:SS
    /// </summary>
    public string GetFormattedTime()
    {
        int minutes = secondsTaken / 60;
        int seconds = secondsTaken % 60;
        return $"{minutes:D2}:{seconds:D2}";
    }

    /// <summary>
    /// Reset score and time (for new game)
    /// </summary>
    public void ResetScore()
    {
        currentScore = 0;
        secondsTaken = 0;
        gameStartTime = 0f;
        UpdateScoreboardUI();
        Debug.Log("[ScoreManager] Score and time reset");
    }

    private void UpdateScoreboardUI()
    {
        if (playerNameText != null)
            playerNameText.text = playerName;

        if (scoreText != null)
            scoreText.text = currentScore.ToString();

        // ← NEW: Update time display
        if (timeTakenText != null)
            timeTakenText.text = GetFormattedTime();

        Debug.Log($"[ScoreManager] Updated scoreboard: {playerName} - Score: {currentScore} - Time: {GetFormattedTime()}");
    }
}