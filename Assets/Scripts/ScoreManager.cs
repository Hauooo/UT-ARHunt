using UnityEngine;
using TMPro;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

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
    private bool finalResultSaved = false;

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
        // ← INITIALIZE FIREBASE FIRST
        InitializeFirebase();

        // ← INITIALIZE USER ID
        var user = FirebaseAuth.DefaultInstance?.CurrentUser;
        if (user != null)
        {
            cachedUserId = user.UserId;
            Debug.Log($"[ScoreManager] User ID cached: {cachedUserId}");
        }
        else
        {
            Debug.LogError("[ScoreManager] No user logged in!");
        }

        // If scoreboard scene, load data
        if (SceneManager.GetActiveScene().name == "ScoreboardScene")
        {
            Debug.Log("[ScoreManager] Start() called in ScoreboardScene");
            InitializeScoreboard();
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "ScoreboardScene")
        {
            Debug.Log("[ScoreManager] Scoreboard scene loaded, initializing fresh...");

            // Ensure Firebase and user ID are set
            if (dbRef == null)
                InitializeFirebase();

            if (string.IsNullOrEmpty(cachedUserId))
            {
                var user = FirebaseAuth.DefaultInstance?.CurrentUser;
                if (user != null)
                    cachedUserId = user.UserId;
            }

            InitializeScoreboard();
        }
    }

    private void InitializeFirebase()
    {
        if (dbRef != null) return; // Already initialized

        try
        {
            dbRef = FirebaseDatabase.GetInstance(
                "https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/")
                .RootReference;
            Debug.Log("[ScoreManager] Firebase initialized");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[ScoreManager] Failed to initialize Firebase: " + ex);
        }
    }

    private void InitializeScoreboard()
    {
        Debug.Log("[ScoreManager] Initializing scoreboard...");

        // ← LOAD PLAYER NAME FIRST
        LoadPlayerName();

        // ← THEN load score and time
        LoadScoreAndTimeFromFirebase();
    }

    /// <summary>
    /// Load player name from Firebase user or fallback
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

        UpdateScoreboardUI();
    }

    /// <summary>
    /// Load score and time from Firebase asynchronously
    /// </summary>
    private void LoadScoreAndTimeFromFirebase()
    {
        // ← CHECK ALL PREREQUISITES
        if (dbRef == null)
        {
            Debug.LogError("[ScoreManager] Firebase not initialized");
            UpdateScoreboardUI();
            return;
        }

        if (string.IsNullOrEmpty(cachedUserId))
        {
            Debug.LogError("[ScoreManager] No user ID cached");
            UpdateScoreboardUI();
            return;
        }

        if (GameManager.Instance == null)
        {
            Debug.LogError("[ScoreManager] GameManager not found");
            UpdateScoreboardUI();
            return;
        }

        string roomId = GameManager.Instance.CurrentRoomId;

        if (string.IsNullOrEmpty(roomId))
        {
            Debug.LogError("[ScoreManager] Missing roomId");
            UpdateScoreboardUI();
            return;
        }

        bool isSinglePlayer = roomId.StartsWith("-");

        DatabaseReference scoreRef = isSinglePlayer
            ? dbRef.Child("levels").Child(roomId).Child("scores").Child(cachedUserId)
            : dbRef.Child("rooms").Child(roomId).Child("scores").Child(cachedUserId);

        Debug.Log($"[ScoreManager] Loading score from: {(isSinglePlayer ? "levels" : "rooms")}/{roomId}/scores/{cachedUserId}");

        scoreRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError("[ScoreManager] Error loading score: " + task.Exception);
                LoadTimeFromFirebase(roomId, isSinglePlayer);
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

            LoadTimeFromFirebase(roomId, isSinglePlayer);
        });
    }

    private void LoadTimeFromFirebase(string roomId, bool isSinglePlayer)
    {
        if (string.IsNullOrEmpty(cachedUserId))
        {
            Debug.LogError("[ScoreManager] No user ID");
            UpdateScoreboardUI();
            return;
        }

        if (dbRef == null)
        {
            Debug.LogError("[ScoreManager] Firebase not initialized");
            UpdateScoreboardUI();
            return;
        }

        DatabaseReference playerRef = isSinglePlayer
            ? dbRef.Child("levels").Child(roomId).Child("players").Child(cachedUserId)
            : dbRef.Child("rooms").Child(roomId).Child("players").Child(cachedUserId);

        Debug.Log($"[ScoreManager] Loading time from: {(isSinglePlayer ? "levels" : "rooms")}/{roomId}/players/{cachedUserId}");

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
                    secondsTaken = 0;
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

    public void AddTreasurePoints()
    {
        currentScore += pointsPerTreasure;
        SaveScoreToFirebase();
        Debug.Log($"[ScoreManager] +{pointsPerTreasure} points. Total: {currentScore}");
        UpdateScoreboardUI();
    }

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

    public void StartGameTimer()
    {
        gameStartTime = Time.time;
        Debug.Log("[ScoreManager] Game timer started");
    }

    public void EndGameTimer()
    {
        if (finalResultSaved) return;
        finalResultSaved = true;

        if (gameStartTime <= 0f)
        {
            Debug.LogWarning("[ScoreManager] Game timer was never started!");
            return;
        }

        secondsTaken = Mathf.Max(0, (int)(Time.time - gameStartTime));
        SaveTimeToFirebase();
        UpdateScoreboardUI();
    }

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

    public int GetScore() => currentScore;
    public string GetPlayerName() => playerName;
    public int GetTimeTakenSeconds() => secondsTaken;

    public string GetFormattedTime()
    {
        int minutes = secondsTaken / 60;
        int seconds = secondsTaken % 60;
        return $"{minutes:D2}:{seconds:D2}";
    }

    public void ResetScore()
    {
        currentScore = 0;
        secondsTaken = 0;
        gameStartTime = 0f;
        finalResultSaved = false;
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