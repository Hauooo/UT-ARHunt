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
    [SerializeField] private AuthManager authManager;

    private int currentScore = 0;
    private string playerName = "Player";
    private DatabaseReference dbRef;
    private string cachedUserId = "";

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

        cachedUserId = authManager.UserId;
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
        if (dbRef == null || string.IsNullOrEmpty(cachedUserId) || GameManager.Instance == null)
        {
            Debug.LogWarning("[ScoreManager] Missing refs for loading score/time.");
            UpdateScoreboardUI();
            return;
        }

        string roomId = GameManager.Instance.CurrentRoomId;

        if (string.IsNullOrEmpty(roomId))
        {
            Debug.LogWarning("[ScoreManager] Missing roomId.");
            UpdateScoreboardUI();
            return;
        }

        bool isSinglePlayer = roomId.StartsWith("-");

        DatabaseReference scoreRef = isSinglePlayer
            ? dbRef.Child("levels").Child(roomId).Child("scores").Child(cachedUserId)
            : dbRef.Child("rooms").Child(roomId).Child("scores").Child(cachedUserId);

        scoreRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogWarning("[ScoreManager] Error loading score: " + task.Exception);
                LoadTimeFromFirebase();
                return;
            }

            if (task.Result != null && task.Result.Exists &&
                long.TryParse(task.Result.Value?.ToString(), out long loadedScore))
            {
                currentScore = (int)loadedScore;
                Debug.Log($"[ScoreManager] Score loaded: {currentScore}");
            }
            else
            {
                Debug.Log("[ScoreManager] No score found. Using 0.");
                currentScore = 0;
            }

            LoadTimeFromFirebase();
        });
    }

    private void LoadTimeFromFirebase()
    {
        if (string.IsNullOrEmpty(cachedUserId))
        {
            Debug.LogWarning("[ScoreManager] No user ID found");
            UpdateScoreboardUI();
            return;
        }

        string gameRoomId = GameManager.Instance?.CurrentRoomId;

        if (string.IsNullOrEmpty(gameRoomId))
        {
            Debug.LogWarning("[ScoreManager] No room ID found");
            UpdateScoreboardUI();
            return;
        }

        bool isSinglePlayer = gameRoomId.StartsWith("-");

        DatabaseReference playerRef = isSinglePlayer
            ? dbRef.Child("levels").Child(gameRoomId).Child("players").Child(cachedUserId)
            : dbRef.Child("rooms").Child(gameRoomId).Child("players").Child(cachedUserId);

        // 1) Try new schema first: timeTakenMs
        playerRef.Child("timeTakenMs").GetValueAsync().ContinueWithOnMainThread(taskMs =>
        {
            if (!taskMs.IsFaulted && taskMs.Result.Exists &&
                long.TryParse(taskMs.Result.Value.ToString(), out long timeMs))
            {
                secondsTaken = Mathf.Max(0, (int)(timeMs / 1000L));
                Debug.Log($"[ScoreManager] Time loaded from timeTakenMs: {timeMs}ms ({secondsTaken}s)");
                UpdateScoreboardUI();
                return;
            }

            if (taskMs.IsFaulted)
            {
                Debug.LogWarning("[ScoreManager] Error loading timeTakenMs: " + taskMs.Exception);
            }

            // 2) Fallback to legacy schema: elapsedTime (seconds)
            playerRef.Child("elapsedTime").GetValueAsync().ContinueWithOnMainThread(taskLegacy =>
            {
                if (taskLegacy.IsFaulted)
                {
                    Debug.LogWarning("[ScoreManager] Error loading elapsedTime: " + taskLegacy.Exception);
                    UpdateScoreboardUI();
                    return;
                }

                if (!taskLegacy.Result.Exists)
                {
                    Debug.Log("[ScoreManager] No time found (timeTakenMs/elapsedTime). Using default: 0");
                    UpdateScoreboardUI();
                    return;
                }

                if (long.TryParse(taskLegacy.Result.Value.ToString(), out long legacySeconds))
                {
                    secondsTaken = Mathf.Max(0, (int)legacySeconds);
                    Debug.Log($"[ScoreManager] Time loaded from elapsedTime (legacy): {secondsTaken}s");
                }

                UpdateScoreboardUI();
            });
        });
    }

    /// <summary>
    /// Called when a treasure is collected (during gameplay)
    /// </summary>
    public void AddTreasurePoints()
    {
        currentScore += pointsPerTreasure;
        SaveScoreToFirebase();
        Debug.Log($"[ScoreManager] +{pointsPerTreasure} points. Total: {currentScore}");
        UpdateScoreboardUI();
    }

    /// <summary>
    /// Add challenge bonus points (from MCQ/Minigame completion)
    /// </summary>
    public void AddChallengeBonus(int bonusPoints)
    {
        if (bonusPoints <= 0)
        {
            Debug.LogWarning("[ScoreManager] Invalid bonus points: " + bonusPoints);
            return;
        }

        currentScore += bonusPoints;
        SaveScoreToFirebase();
        Debug.Log($"[ScoreManager] +{bonusPoints} challenge bonus. Total: {currentScore}");
        UpdateScoreboardUI();
    }

    /// <summary>
    /// Save score to Firebase after treasure/challenge completion
    /// </summary>
    private void SaveScoreToFirebase()
    {
        if (dbRef == null || string.IsNullOrEmpty(cachedUserId))
        {
            Debug.LogWarning("[ScoreManager] Cannot save score - Firebase or userId not ready");
            return;
        }

        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[ScoreManager] Cannot save score - GameManager not found");
            return;
        }

        string roomId = GameManager.Instance.CurrentRoomId;
        bool isSinglePlayer = roomId.StartsWith("-");

        if (string.IsNullOrEmpty(roomId))
        {
            Debug.LogWarning("[ScoreManager] Cannot save score - No room ID");
            return;
        }

        DatabaseReference scoreRef = isSinglePlayer
            ? dbRef.Child("levels").Child(roomId).Child("scores").Child(cachedUserId)
            : dbRef.Child("rooms").Child(roomId).Child("scores").Child(cachedUserId);

        scoreRef.SetValueAsync(currentScore)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCompleted && !task.IsFaulted)
                {
                    Debug.Log($"[ScoreManager] Score saved to Firebase: {currentScore}");
                }
                else if (task.IsFaulted)
                {
                    Debug.LogError("[ScoreManager] Failed to save score: " + task.Exception);
                }
            });
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
        if (gameStartTime <= 0)
        {
            Debug.LogWarning("[ScoreManager] Game timer was never started!");
            return;
        }

        secondsTaken = Mathf.Max(0, (int)(Time.time - gameStartTime));
        Debug.Log($"[ScoreManager] Game completed in {secondsTaken} seconds");
        SaveTimeToFirebase();
        UpdateScoreboardUI();
    }

    /// <summary>
    /// Save time to Firebase using new schema (timeTakenMs)
    /// </summary>
    private void SaveTimeToFirebase()
    {
        if (dbRef == null || string.IsNullOrEmpty(cachedUserId))
        {
            Debug.LogWarning("[ScoreManager] Cannot save time - Firebase or userId not ready");
            return;
        }

        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[ScoreManager] Cannot save time - GameManager not found");
            return;
        }

        string roomId = GameManager.Instance.CurrentRoomId;
        bool isSinglePlayer = roomId.StartsWith("-");

        if (string.IsNullOrEmpty(roomId))
        {
            Debug.LogWarning("[ScoreManager] Cannot save time - No room ID");
            return;
        }

        long timeMs = (long)secondsTaken * 1000L;

        DatabaseReference timeRef = isSinglePlayer
            ? dbRef.Child("levels").Child(roomId).Child("players").Child(cachedUserId).Child("timeTakenMs")
            : dbRef.Child("rooms").Child(roomId).Child("players").Child(cachedUserId).Child("timeTakenMs");

        timeRef.SetValueAsync(timeMs)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCompleted && !task.IsFaulted)
                {
                    Debug.Log($"[ScoreManager] Time saved to Firebase: {timeMs}ms ({secondsTaken}s)");
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