using UnityEngine;
using UnityEngine.SceneManagement;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;
using System;
using System.Collections.Generic;
using System.Collections;

// This is the central controller for the entire game.
// It persists across scenes and manages the overall game state,
// including room creation, joining, and scene transitions.

public enum GameMode
{
    InMenu,
    CreatingSet,
    PlayingInRoom
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Scene Configuration")]
    [SerializeField] private string arSceneName = "ARScene";
    [SerializeField] private string scoreboardSceneName = "ScoreboardScene";
    
    [Header("Firebase")]
    [SerializeField]
    private string firebaseDatabaseUrl =
        "https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/";

    public GameMode CurrentMode { get; private set; } = GameMode.InMenu;

    public List<TreasureManagerGPS_Multiplayer.TreasureData> newSetTreasure = new List<TreasureManagerGPS_Multiplayer.TreasureData>();

    // --- Public Properties & Events ---
    public string CurrentRoomId { get; private set; }
    public bool IsHost { get; private set; }

    public event Action<string, bool> OnLobbyReady; // roomId, isHost
    public event Action<Dictionary<string, PlayerData>> OnPlayerListUpdated;
    public event Action OnGameStarting;
    public event Action<string> OnJoinFailed;
    public Dictionary<string, PlayerData> CurrentPlayers { get; private set; } = new Dictionary<string, PlayerData>();

    // --- Private State ---
    private DatabaseReference dbRef;
    private bool isFirebaseReady = false;

    private string lastFirebaseInitError = null;
    private string lastFirebaseInitState = "Not started";
    private bool isShowingScoreboard = false;

    #region --- Unity Methods ---

    private void Awake()
    {
        // Singleton guard
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Subscribe to AuthManager
        if (AuthManager.Instance != null)
        {
            AuthManager.Instance.OnSignedIn += HandleAuthReady;

            if (AuthManager.Instance.User != null)
            {
                Debug.Log("AuthManager already signed in; initializing Firebase DB now.");
                HandleAuthReady(AuthManager.Instance.User);
            }
        }
        else
        {
            Debug.LogError("[GameManager] AuthManager.Instance is null in Awake().");
        }
    }

    private void Start()
    {
        Debug.Log("GameManager initialized.");
    }

    private void OnEnable()
    {
        // ← NEW: Reset when scene loads
        if (SceneManager.GetActiveScene().name == "ScoreboardScene")
        {
            Debug.Log("[ScoreManager] Scoreboard scene loaded, initializing...");
            Start();  // Force re-initialization
        }
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (AuthManager.Instance != null)
            AuthManager.Instance.OnSignedIn -= HandleAuthReady;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void HandleAuthReady(Firebase.Auth.FirebaseUser user)
    {
        Debug.Log($"[GameManager] HandleAuthReady received. uid={user?.UserId}");
        TryInitFirebaseDb("HandleAuthReady");
    }

    #endregion

    #region --- Firebase Init ---

    private void TryInitFirebaseDb(string caller)
    {
        if (isFirebaseReady && dbRef != null) return;

        if (AuthManager.Instance == null || AuthManager.Instance.User == null)
        {
            lastFirebaseInitState = "Auth not ready";
            Debug.LogWarning($"[FirebaseInit] ({caller}) {lastFirebaseInitState}");
            return;
        }

        try
        {
            dbRef = FirebaseDatabase.GetInstance(firebaseDatabaseUrl).RootReference;
            isFirebaseReady = (dbRef != null);
            lastFirebaseInitError = null;
            lastFirebaseInitState = isFirebaseReady ? "READY" : "dbRef null";
        }
        catch (Exception ex)
        {
            isFirebaseReady = false;
            dbRef = null;
            lastFirebaseInitError = ex.Message;
            lastFirebaseInitState = "Exception: " + ex.Message;
        }

        Debug.Log($"[FirebaseInit] ({caller}) state={lastFirebaseInitState} err={lastFirebaseInitError}");
    }

    private void EnsureFirebaseReady(string caller)
    {
        if (isFirebaseReady && dbRef != null) return;
        TryInitFirebaseDb(caller);
    }

    #endregion

    #region --- Public API for UI ---

    /// <summary>
    /// Setup game mode for playing a single-player level
    /// </summary>
    public void SetGameModeForLevel(string levelId, string levelName, List<TreasureManagerGPS_Multiplayer.TreasureData> treasures)
    {
        CurrentMode = GameMode.PlayingInRoom;
        CurrentRoomId = levelId;
        IsHost = true;

        CurrentLevelTreasures?.Clear();
        CurrentLevelTreasures = new List<TreasureManagerGPS_Multiplayer.TreasureData>();

        TreasureKeys?.Clear();
        TreasureKeys = new Dictionary<TreasureManagerGPS_Multiplayer.TreasureData, string>();

        for (int i = 0; i < treasures.Count; i++)
        {
            var t = treasures[i];

            // ← DEEP COPY challenge including options
            ChallengeData challengeCopy = null;
            if (t.challenge != null && t.challenge.type != ChallengeType.None)
            {
                challengeCopy = new ChallengeData
                {
                    type = t.challenge.type,
                    question = t.challenge.question,
                    bonusPoints = t.challenge.bonusPoints,
                    maxAttempts = t.challenge.maxAttempts,
                    minigameId = t.challenge.minigameId,
                    timeLimitSeconds = t.challenge.timeLimitSeconds,
                    // ← CRITICAL: Deep copy options list
                    options = t.challenge.options != null
                        ? new List<MCQOption>(t.challenge.options)
                        : new List<MCQOption>()
                };

                Debug.Log($"[GameManager] Cloned challenge for '{t.name}': " +
                          $"type={challengeCopy.type}, " +
                          $"question='{challengeCopy.question}', " +
                          $"options={challengeCopy.options.Count}");
            }

            var copy = new TreasureManagerGPS_Multiplayer.TreasureData
            {
                name = t.name,
                lat = t.lat,
                lon = t.lon,
                points = t.points,
                orderIndex = t.orderIndex,
                collectedBy = t.collectedBy != null ? new Dictionary<string, bool>(t.collectedBy) : new Dictionary<string, bool>(),
                challenge = challengeCopy  // ← Use the cloned challenge
            };

            CurrentLevelTreasures.Add(copy);

            // Generate unique key based on index
            string uniqueKey = $"treasure_{i}";
            TreasureKeys[copy] = uniqueKey;

            Debug.Log($"[GameManager] Mapped '{copy.name}' -> {uniqueKey}");
        }

        Debug.Log($"[GameManager] Set game mode for level: {levelName} ({levelId}) with {CurrentLevelTreasures.Count} treasures");
    }

    private ChallengeData CloneChallenge(ChallengeData c)
    {
        if (c == null) return null;
        return new ChallengeData
        {
            type = c.type,
            question = c.question,
            bonusPoints = c.bonusPoints,
            maxAttempts = c.maxAttempts,
            minigameId = c.minigameId,
            timeLimitSeconds = c.timeLimitSeconds,
            options = c.options != null
                ? new List<MCQOption>(c.options.ConvertAll(o => new MCQOption { text = o.text, isCorrect = o.isCorrect }))
                : new List<MCQOption>()
        };
    }

    public Dictionary<TreasureManagerGPS_Multiplayer.TreasureData, string> TreasureKeys { get; set; }

    // Add this field to GameManager
    public List<TreasureManagerGPS_Multiplayer.TreasureData> CurrentLevelTreasures { get; set; }

    private string GetSafeDisplayName(string fallback)
    {
        string raw = AuthManager.Instance?.User?.DisplayName;
        if (string.IsNullOrWhiteSpace(raw))
        {
            string uid = AuthManager.Instance?.UserId;
            if (!string.IsNullOrEmpty(uid) && uid.Length >= 4)
                return $"{fallback}-{uid.Substring(0, 4)}";
            return fallback;
        }
        return raw.Trim();
    }

    private async System.Threading.Tasks.Task<string> GetUniqueRoomCodeAsync(int maxAttempts = 10)
    {
        EnsureFirebaseReady("GetUniqueRoomCodeAsync");
        if (!isFirebaseReady || dbRef == null)
            throw new Exception("Firebase not ready.");

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            string code = GenerateRoomCode();

            var snapshot = await dbRef.Child("rooms").Child(code).GetValueAsync();
            if (!snapshot.Exists)
                return code;
        }

        throw new Exception($"Failed to generate unique room code after {maxAttempts} attempts.");
    }

    public void HostNewRoom(TreasureSetData treasureSet)
    {
        EnsureFirebaseReady("HostNewRoom");
        if (!isFirebaseReady || AuthManager.Instance?.User == null)
        {
            Debug.LogError($"HostNewRoom blocked. ready={isFirebaseReady} userNull={AuthManager.Instance?.User == null} state={lastFirebaseInitState} err={lastFirebaseInitError}");
            return;
        }

        if (treasureSet == null)
        {
            Debug.LogError("HostNewRoom failed: treasureSet is null.");
            return;
        }

        if (string.IsNullOrWhiteSpace(AuthManager.Instance?.User?.DisplayName))
        {
            OnJoinFailed?.Invoke("Please set your username first.");
            return;
        }

        HostNewRoomAsync(treasureSet).ContinueWithOnMainThread(t =>
        {
            if (t.IsFaulted)
                Debug.LogError("[GameManager] HostNewRoomAsync failed: " + t.Exception);
        });
    }

    private async System.Threading.Tasks.Task HostNewRoomAsync(TreasureSetData treasureSet)
    {
        IsHost = true;
        CurrentMode = GameMode.PlayingInRoom;
        CurrentRoomId = await GetUniqueRoomCodeAsync();

        var roomRef = dbRef.Child("rooms").Child(CurrentRoomId);
        string hostUid = AuthManager.Instance.UserId;
        string hostDisplayName = GetSafeDisplayName("Host");

        var tasks = new List<System.Threading.Tasks.Task>
    {
        roomRef.Child("hostId").SetValueAsync(hostUid),
        roomRef.Child("hostName").SetValueAsync(hostDisplayName),
        roomRef.Child("selectedSetId").SetValueAsync(treasureSet.setId),
        roomRef.Child("status").SetValueAsync("waiting"),
        roomRef.Child("collectionMode").SetValueAsync(treasureSet.collectionMode),
        roomRef.Child("nextTreasureIndex").SetValueAsync(0),
        roomRef.Child("players").Child(hostUid)
            .SetRawJsonValueAsync(JsonUtility.ToJson(new PlayerData(hostDisplayName, 0)))
    };

        var gameStateRef = roomRef.Child("gameState");
        for (int i = 0; i < treasureSet.treasures.Count; i++)
        {
            var treasure = treasureSet.treasures[i];
            string liveTreasureKey = gameStateRef.Push().Key;

            var liveTreasure = new TreasureManagerGPS_Multiplayer.TreasureData
            {
                name = treasure.name,
                lat = treasure.lat,
                lon = treasure.lon,
                points = treasure.points,
                challenge = treasure.challenge,
                collectedBy = null,
                orderIndex = i // NEW
            };

            tasks.Add(gameStateRef.Child(liveTreasureKey)
                .SetRawJsonValueAsync(JsonUtility.ToJson(liveTreasure)));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        Debug.Log($"Room {CurrentRoomId} hosted successfully!");
        ListenToRoomUpdates(CurrentRoomId);
        OnLobbyReady?.Invoke(CurrentRoomId, true);
    }

    public void JoinRoomById(string roomId)
    {
        EnsureFirebaseReady("JoinRoomById");
        if (!isFirebaseReady || AuthManager.Instance?.User == null)
        {
            OnJoinFailed?.Invoke("Not signed in or Firebase not ready yet.");
            Debug.LogError($"JoinRoomById blocked. ready={isFirebaseReady} userNull={AuthManager.Instance?.User == null} state={lastFirebaseInitState} err={lastFirebaseInitError}");
            return;
        }

        string normalizedRoomId = roomId?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalizedRoomId))
        {
            OnJoinFailed?.Invoke("Room code is empty.");
            return;
        }

        if (string.IsNullOrWhiteSpace(AuthManager.Instance?.User?.DisplayName))
        {
            OnJoinFailed?.Invoke("Please set your username first.");
            return;
        }

        string myUid = AuthManager.Instance.UserId;

        DatabaseReference roomRef = dbRef.Child("rooms").Child(normalizedRoomId);
        roomRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                OnJoinFailed?.Invoke("Error checking room.");
                Debug.LogError("JoinRoomById GetValueAsync faulted: " + task.Exception);
                return;
            }

            if (!task.Result.Exists)
            {
                OnJoinFailed?.Invoke($"Room {normalizedRoomId} does not exist.");
                return;
            }

            // Block host from joining their own room
            string hostId = task.Result.Child("hostId").Value?.ToString();
            if (!string.IsNullOrEmpty(hostId) && hostId == myUid)
            {
                OnJoinFailed?.Invoke("You are the host of this room. Hosts cannot join as a participant.");
                return;
            }

            string status = task.Result.Child("status").Value?.ToString();
            if (status != "waiting")
            {
                OnJoinFailed?.Invoke($"Room {normalizedRoomId} is not available to join.");
                return;
            }

            // Only NOW set local state for joining
            IsHost = false;
            CurrentRoomId = normalizedRoomId;
            CurrentMode = GameMode.PlayingInRoom;

            var newPlayer = new PlayerData(GetSafeDisplayName("Player"), 0);
            roomRef.Child("players").Child(myUid)
                .SetRawJsonValueAsync(JsonUtility.ToJson(newPlayer))
                .ContinueWithOnMainThread(joinTask =>
                {
                    if (joinTask.IsFaulted)
                    {
                        OnJoinFailed?.Invoke("Failed to join room.");
                        Debug.LogError("JoinRoomById add player faulted: " + joinTask.Exception);
                        return;
                    }

                    Debug.Log($"Joined room {CurrentRoomId}!");
                    ListenToRoomUpdates(CurrentRoomId);
                    OnLobbyReady?.Invoke(CurrentRoomId, false);
                });
        });
    }


    public void StartGame()
    {
        if (!IsHost) return;
        if (string.IsNullOrEmpty(CurrentRoomId)) return;

        EnsureFirebaseReady("StartGame");
        if (!isFirebaseReady) return;

        dbRef.Child("rooms").Child(CurrentRoomId).Child("status").SetValueAsync("in-progress");
    }

    public void LeaveRoom()
    {
        if (string.IsNullOrEmpty(CurrentRoomId)) return;

        EnsureFirebaseReady("LeaveRoom");
        if (!isFirebaseReady || dbRef == null) return;

        string roomId = CurrentRoomId;
        string myUid = AuthManager.Instance?.UserId;

        if (IsHost)
        {
            // Host leaving: close room for everyone, then delete
            dbRef.Child("rooms").Child(roomId).Child("status")
                .SetValueAsync("ended")
                .ContinueWithOnMainThread(t =>
                {
                    if (t.IsFaulted)
                    {
                        Debug.LogError("[GameManager] Failed to set room status to ended: " + t.Exception);
                    }

                    // small delay optional; helps clients receive 'ended'
                    StartCoroutine(DeleteRoomAfterDelay(roomId, 0.75f));

                    StopListeningToRoomUpdates();
                    CurrentPlayers = new Dictionary<string, PlayerData>();
                    CurrentRoomId = null;
                    IsHost = false;
                    CurrentMode = GameMode.InMenu;
                    SceneManager.LoadScene("MenuScene");
                });
        }
        else
        {
            // Participant leaving: remove only this player
            dbRef.Child("rooms").Child(roomId).Child("players").Child(myUid).RemoveValueAsync();

            StopListeningToRoomUpdates();
            CurrentPlayers = new Dictionary<string, PlayerData>();
            CurrentRoomId = null;
            IsHost = false;
            CurrentMode = GameMode.InMenu;
            SceneManager.LoadScene("MenuScene");
        }
    }

    #endregion

    #region --- Firebase Listeners ---

    private void ListenToRoomUpdates(string roomId)
    {
        dbRef.Child("rooms").Child(roomId).Child("players").ValueChanged += HandlePlayerListChanged;
        dbRef.Child("rooms").Child(roomId).Child("status").ValueChanged += HandleStatusChanged;
    }

    private void StopListeningToRoomUpdates()
    {
        if (string.IsNullOrEmpty(CurrentRoomId)) return;

        dbRef.Child("rooms").Child(CurrentRoomId).Child("players").ValueChanged -= HandlePlayerListChanged;
        dbRef.Child("rooms").Child(CurrentRoomId).Child("status").ValueChanged -= HandleStatusChanged;
    }

    private void HandlePlayerListChanged(object sender, ValueChangedEventArgs args)
    {
        var playersDict = new Dictionary<string, PlayerData>();

        if (args.Snapshot.Exists)
        {
            foreach (var child in args.Snapshot.Children)
            {
                string json = child.GetRawJsonValue();
                PlayerData playerData = JsonUtility.FromJson<PlayerData>(json);
                playersDict[child.Key] = playerData;
            }
        }

        CurrentPlayers = playersDict; // ✅ cache latest
        OnPlayerListUpdated?.Invoke(playersDict);
    }



    private void HandleStatusChanged(object sender, ValueChangedEventArgs args)
    {
        // ← NEW: Check scoreboard flag FIRST, before any other logic
        if (isShowingScoreboard)
        {
            Debug.Log("[GameManager] Scoreboard is showing - ignoring room status changes");
            return;
        }

        if (!args.Snapshot.Exists)
        {
            Debug.LogWarning("[GameManager] Room status node missing (room deleted). Returning to menu.");
            ReturnToMenu();
            return;
        }

        string status = args.Snapshot.Value?.ToString();

        // ✅ End-game signal for everyone
        if (status == "ended")
        {
            Debug.Log("[GameManager] Room ended. Loading Scoreboard scene.");
            isShowingScoreboard = true;  // ← SET FLAG before loading scene

            StopListeningToRoomUpdates();
            SceneManager.LoadScene(scoreboardSceneName);
            return;
        }

        if (status == "in-progress")
        {
            Debug.Log("[GameManager] Game is starting! Loading AR Scene.");
            CurrentMode = GameMode.PlayingInRoom;

            OnGameStarting?.Invoke();
            StopListeningToRoomUpdates();
            SceneManager.LoadScene(arSceneName);
            return;
        }
    }

    #endregion

    #region --- End Game Logic ---
    public void EndGame()
    {
        if (!IsHost)
        {
            Debug.LogWarning("[GameManager] EndGame blocked: only host can end the game.");
            return;
        }

        if (string.IsNullOrEmpty(CurrentRoomId))
        {
            Debug.LogWarning("[GameManager] EndGame blocked: CurrentRoomId is empty.");
            return;
        }

        EnsureFirebaseReady("EndGame");
        if (!isFirebaseReady || dbRef == null)
        {
            Debug.LogError("[GameManager] EndGame failed: Firebase not ready.");
            return;
        }
            

            string roomId = CurrentRoomId;
        var roomRef = dbRef.Child("rooms").Child(roomId);

        Debug.Log($"[GameManager] Host ending game. Setting status=ended for room {roomId}...");

        // 1) Signal all clients first
        roomRef.Child("status").SetValueAsync("ended").ContinueWithOnMainThread(setTask =>
        {
            if (setTask.IsFaulted)
            {
                Debug.LogError("[GameManager] Failed to set ended status: " + setTask.Exception);
                return;
            }

            // 2) Host can return immediately (others will return via HandleStatusChanged)
            ReturnToMenu();

            // 3) Cleanup room after a short delay so everyone receives the status update
            StartCoroutine(DeleteRoomAfterDelay(roomId, 1.0f));
        });
    }

    public void LeaveScoreboard()
    {
        isShowingScoreboard = false;  // ← RESET flag when leaving

        if (string.IsNullOrEmpty(CurrentRoomId))
        {
            ReturnToMenu();
            return;
        }

        EnsureFirebaseReady("LeaveScoreboard");
        if (!isFirebaseReady || dbRef == null)
        {
            ReturnToMenu();
            return;
        }

        string roomId = CurrentRoomId;
        string uid = AuthManager.Instance.UserId;
        var playersRef = dbRef.Child("rooms").Child(roomId).Child("players");

        Debug.Log($"[GameManager] LeaveScoreboard called. Removing player {uid} from room {roomId}");

        // 1) Remove self from players list
        playersRef.Child(uid).RemoveValueAsync().ContinueWithOnMainThread(_ =>
        {
            Debug.Log("[GameManager] Player removed from room");

            // 2) Check remaining players
            playersRef.GetValueAsync().ContinueWithOnMainThread(t =>
            {
                if (!t.IsFaulted)
                {
                    bool anyLeft = t.Result.Exists && t.Result.ChildrenCount > 0;
                    if (!anyLeft)
                    {
                        Debug.Log($"[GameManager] No players left. Deleting room {roomId}");
                        dbRef.Child("rooms").Child(roomId).RemoveValueAsync();
                    }
                    else
                    {
                        Debug.Log($"[GameManager] {t.Result.ChildrenCount} players still in room");
                    }
                }

                // Reset local state
                CurrentPlayers = new Dictionary<string, PlayerData>();
                CurrentRoomId = null;
                IsHost = false;
                CurrentMode = GameMode.InMenu;

                Debug.Log("[GameManager] Returning to Main Menu...");
                SceneManager.LoadScene("MenuScene");
            });
        });
    }

    private IEnumerator DeleteRoomAfterDelay(string roomId, float delaySeconds)
    {
        yield return new WaitForSeconds(delaySeconds);

        EnsureFirebaseReady("DeleteRoomAfterDelay");
        if (!isFirebaseReady || dbRef == null) yield break;

        Debug.Log($"[GameManager] Deleting room {roomId}...");
        dbRef.Child("rooms").Child(roomId).RemoveValueAsync().ContinueWithOnMainThread(t =>
        {
            if (t.IsFaulted) Debug.LogError("[GameManager] Failed to delete room: " + t.Exception);
            else Debug.Log($"[GameManager] Room {roomId} deleted.");
        });
    }

#endregion

#region --- Scene Management ---

private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // IMPORTANT:
        // Do NOT initialize TreasureManagerGPS_Multiplayer here.
        // ARSceneController is responsible for initializing it based on CurrentMode.
    }

    #endregion

    #region --- Creator Mode API ---

    public void StartCreatorMode()
    {
        Debug.Log("Entering Creator Mode...");
        CurrentMode = GameMode.CreatingSet;
        newSetTreasure.Clear();
        SceneManager.LoadScene("CreatorScene");
    }

    public void ExitCreatorMode()
    {
        Debug.Log("Exiting Creator Mode...");
        CurrentMode = GameMode.InMenu;
        newSetTreasure.Clear();
        SceneManager.LoadScene("MenuScene");
    }

    public void SaveNewTreasureSet(string setName)
    {
        Debug.Log("--- SaveNewTreasureSet START ---");
        EnsureFirebaseReady("SaveNewTreasureSet");

        if (!isFirebaseReady)
        {
            Debug.LogWarning($"Firebase not ready yet. state={lastFirebaseInitState}. Delaying save attempt...");
            StartCoroutine(RetrySaveTreasureSet(setName));
            return;
        }

        if (string.IsNullOrEmpty(AuthManager.Instance?.UserId))
        {
            Debug.LogError("FATAL: UserId empty. Cannot save.");
            return;
        }

        if (newSetTreasure.Count == 0)
        {
            Debug.LogError("No treasures to save.");
            return;
        }

        string newSetId = dbRef.Child("treasureSets").Push().Key;
        TreasureSetData newSet = new TreasureSetData
        {
            setId = newSetId,
            setName = setName,
            createdBy = AuthManager.Instance.UserId,
            treasures = this.newSetTreasure
        };

        string json = JsonUtility.ToJson(newSet);
        dbRef.Child("treasureSets").Child(newSetId).SetRawJsonValueAsync(json).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("Failed to save treasure set: " + task.Exception);
            }
            else
            {
                Debug.Log("Treasure set saved successfully!");
                ExitCreatorMode();
            }
        });

        Debug.Log("--- SaveNewTreasureSet END ---");
    }

    private IEnumerator RetrySaveTreasureSet(string setName)
    {
        float timeout = 30f;
        while (!isFirebaseReady && timeout > 0f)
        {
            EnsureFirebaseReady("RetrySaveTreasureSet");
            Debug.Log($"[RetrySave] waiting... t={timeout:0.0}s state={lastFirebaseInitState}");
            yield return new WaitForSeconds(0.5f);
            timeout -= 0.5f;
        }

        if (isFirebaseReady)
            SaveNewTreasureSet(setName);
        else
            Debug.LogError($"Firebase never became ready in time. state={lastFirebaseInitState}");
    }

    public void ReturnToMenu()
    {
        Debug.Log("Returning to Main Menu...");

        StopListeningToRoomUpdates();

        CurrentMode = GameMode.InMenu;
        CurrentRoomId = null;
        IsHost = false;
        newSetTreasure.Clear();

        SceneManager.LoadScene("MenuScene");
    }

    #endregion

    #region --- Helpers ---

    private string GenerateRoomCode(int length = 4)
    {
        const string chars = "ABCDEFGHIJKLMNPQRSTUVWXYZ123456789";
        var random = new System.Random();
        var code = new char[length];
        for (int i = 0; i < length; i++)
            code[i] = chars[random.Next(chars.Length)];
        return new string(code);
    }

    #endregion
}

#region --- Data Structures ---

public enum  CollectionMode
{
    Free = 0,
    InOrder = 1
}

[Serializable]
public class TreasureSetData
{
    public string setId;
    public string setName;
    public string createdBy;
    public string linkedLevelId;
    public int orderIndex;
    public int collectionMode = (int)CollectionMode.Free;
    public List<TreasureManagerGPS_Multiplayer.TreasureData> treasures = new List<TreasureManagerGPS_Multiplayer.TreasureData>();
}

[Serializable]
public class PlayerData
{
    public string displayName;
    public long score;

    public PlayerData(string name, long initialScore)
    {
        displayName = name;
        score = initialScore;
    }
}

#endregion