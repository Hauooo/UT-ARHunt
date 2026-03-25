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
    [SerializeField] private TextMeshProUGUI timeTakenText;

    [Header("Score Settings")]
    [SerializeField] private int pointsPerTreasure = 10;
    [SerializeField] private AuthManager authManager;  // ← Fixed: AuthManager (not Authmanager)

    private int currentScore = 0;
    private string playerName = "Player";
    private DatabaseReference dbRef;

    private float gameStartTime = 0f;
    private int secondsTaken = 0;

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
        if (authManager == null)
        {
            Debug.LogError("[ScoreManager] AuthManager reference is missing!");
            authManager = FindObjectOfType<AuthManager>();
        }

        if (authManager == null)
        {
            Debug.LogError("[ScoreManager] Could not find AuthManager in scene!");
            return;
        }

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
    /// </summary>
    private void LoadScoreAndTimeFromFirebase()
    {
        string userId = authManager.UserId;
        string gameRoomId = GameManager.Instance?.CurrentRoomId;

        if (string.IsNullOrEmpty(gameRoomId))
        {
            Debug.LogWarning("[ScoreManager] No room ID found");
            return;
        }

        // Check if it's single-player or multiplayer
        bool isSinglePlayer = gameRoomId.StartsWith("-");

        DatabaseReference scoreRef = isSinglePlayer
            ? dbRef.Child("levels").Child(gameRoomId).Child("scores").Child(userId)
            : dbRef.Child("rooms").Child(gameRoomId).Child("scores").Child(userId);

        string scorePath = isSinglePlayer
            ? $"levels/{gameRoomId}/scores/{userId}"
            : $"rooms/{gameRoomId}/scores/{userId}";

        Debug.Log($"[ScoreManager] Loading score from {(isSinglePlayer ? "level" : "room")}: {scorePath}");

        scoreRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogWarning("[ScoreManager] Error loading score: " + task.Exception);
                LoadTimeFromFirebase();
                return;
            }

            if (!task.Result.Exists)
            {
                Debug.Log("[ScoreManager] No score found in Firebase. Using default: 0");
                LoadTimeFromFirebase();
                return;
            }

            if (long.TryParse(task.Result.Value.ToString(), out long score))
            {
                currentScore = (int)score;
                Debug.Log($"[ScoreManager] Score loaded: {currentScore}");
            }

            LoadTimeFromFirebase();
        });
    }

    /// <summary>
    /// Load time taken from Firebase
    /// </summary>
    private void LoadTimeFromFirebase()
    {
        string userId = authManager.UserId;
        string gameRoomId = GameManager.Instance?.CurrentRoomId;

        if (string.IsNullOrEmpty(gameRoomId))
        {
            Debug.LogWarning("[ScoreManager] No room ID found");
            UpdateScoreboardUI();
            return;
        }

        bool isSinglePlayer = gameRoomId.StartsWith("-");

        DatabaseReference timeRef = isSinglePlayer
            ? dbRef.Child("levels").Child(gameRoomId).Child("players").Child(userId).Child("elapsedTime")
            : dbRef.Child("rooms").Child(gameRoomId).Child("players").Child(userId).Child("elapsedTime");

        timeRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogWarning("[ScoreManager] Error loading time: " + task.Exception);
                UpdateScoreboardUI();
                return;
            }

            if (!task.Result.Exists)
            {
                Debug.Log("[ScoreManager] No time found in Firebase. Using default: 0");
                UpdateScoreboardUI();
                return;
            }

            if (long.TryParse(task.Result.Value.ToString(), out long timeSeconds))
            {
                secondsTaken = (int)timeSeconds;
                Debug.Log($"[ScoreManager] Time loaded: {secondsTaken}s");
            }

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
    /// End the game timer and save time to Firebase
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
        bool isSinglePlayer = roomId.StartsWith("-");

        if (string.IsNullOrEmpty(roomId)) return;

        DatabaseReference timeRef = isSinglePlayer
            ? dbRef.Child("levels").Child(roomId).Child("players").Child(user.UserId).Child("elapsedTime")
            : dbRef.Child("rooms").Child(roomId).Child("players").Child(user.UserId).Child("elapsedTime");

        timeRef.SetValueAsync(secondsTaken)
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

        if (timeTakenText != null)
            timeTakenText.text = GetFormattedTime();

        Debug.Log($"[ScoreManager] Updated scoreboard: {playerName} - Score: {currentScore} - Time: {GetFormattedTime()}");
    }
}