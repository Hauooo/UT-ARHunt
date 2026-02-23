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

    // --- Private State ---
    private DatabaseReference dbRef;
    private bool isFirebaseReady = false;

    private string lastFirebaseInitError = null;
    private string lastFirebaseInitState = "Not started";

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
        SceneManager.sceneLoaded += OnSceneLoaded;
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

        IsHost = true;
        CurrentRoomId = GenerateRoomCode();
        CurrentMode = GameMode.PlayingInRoom;

        var roomRef = dbRef.Child("rooms").Child(CurrentRoomId);

        // Write room metadata (JsonUtility cannot serialize dictionaries, so do not write RoomData as one JSON blob)
        var tasks = new List<System.Threading.Tasks.Task>();

        tasks.Add(roomRef.Child("hostId").SetValueAsync(AuthManager.Instance.UserId));
        tasks.Add(roomRef.Child("hostName").SetValueAsync(AuthManager.Instance.User.DisplayName ?? "The Host"));
        tasks.Add(roomRef.Child("selectedSetId").SetValueAsync(treasureSet.setId));
        tasks.Add(roomRef.Child("status").SetValueAsync("waiting"));

        // Add host player
        var hostPlayer = new PlayerData(AuthManager.Instance.User.DisplayName ?? "The Host", 0);
        tasks.Add(roomRef.Child("players").Child(AuthManager.Instance.UserId)
            .SetRawJsonValueAsync(JsonUtility.ToJson(hostPlayer)));

        // Write treasure game state
        var gameStateRef = roomRef.Child("gameState");
        foreach (var treasure in treasureSet.treasures)
        {
            string liveTreasureKey = gameStateRef.Push().Key;

            var liveTreasure = new TreasureManagerGPS_Multiplayer.TreasureData
            {
                name = treasure.name,
                lat = treasure.lat,
                lon = treasure.lon,
                points = treasure.points,
                collectedBy = null // let it be missing initially; treat null as "not collected"
            };

            tasks.Add(gameStateRef.Child(liveTreasureKey)
                .SetRawJsonValueAsync(JsonUtility.ToJson(liveTreasure)));
        }

        System.Threading.Tasks.Task.WhenAll(tasks).ContinueWithOnMainThread(t =>
        {
            if (t.IsFaulted)
            {
                Debug.LogError("Failed to host room: " + t.Exception);
                return;
            }

            Debug.Log($"Room {CurrentRoomId} hosted successfully!");
            ListenToRoomUpdates(CurrentRoomId);
            OnLobbyReady?.Invoke(CurrentRoomId, true);
        });
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

        IsHost = false;
        CurrentRoomId = roomId?.Trim().ToUpperInvariant();
        CurrentMode = GameMode.PlayingInRoom;

        if (string.IsNullOrEmpty(CurrentRoomId))
        {
            OnJoinFailed?.Invoke("Room code is empty.");
            return;
        }

        DatabaseReference roomRef = dbRef.Child("rooms").Child(CurrentRoomId);
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
                OnJoinFailed?.Invoke($"Room {CurrentRoomId} does not exist.");
                return;
            }

            string status = task.Result.Child("status").Value?.ToString();
            if (status != "waiting")
            {
                OnJoinFailed?.Invoke($"Room {CurrentRoomId} is not available to join.");
                return;
            }

            var newPlayer = new PlayerData(AuthManager.Instance.User.DisplayName ?? "A Player", 0);
            roomRef.Child("players").Child(AuthManager.Instance.UserId)
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
        if (!isFirebaseReady) return;

        dbRef.Child("rooms").Child(CurrentRoomId).Child("players").Child(AuthManager.Instance.UserId).RemoveValueAsync();

        StopListeningToRoomUpdates();

        CurrentRoomId = null;
        IsHost = false;
        CurrentMode = GameMode.InMenu;

        SceneManager.LoadScene("MenuScene");
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
        if (!args.Snapshot.Exists) return;

        var playersDict = new Dictionary<string, PlayerData>();
        foreach (var child in args.Snapshot.Children)
        {
            string json = child.GetRawJsonValue();
            PlayerData playerData = JsonUtility.FromJson<PlayerData>(json);
            playersDict[child.Key] = playerData;
        }

        OnPlayerListUpdated?.Invoke(playersDict);
    }

    private void HandleStatusChanged(object sender, ValueChangedEventArgs args)
    {
        if (!args.Snapshot.Exists) return;

        string status = args.Snapshot.Value?.ToString();
        if (status == "in-progress")
        {
            Debug.Log("Game is starting! Loading AR Scene.");
            CurrentMode = GameMode.PlayingInRoom;

            OnGameStarting?.Invoke();

            // OK to stop listening once we transition scenes (or keep if needed)
            StopListeningToRoomUpdates();

            SceneManager.LoadScene(arSceneName);
        }
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

[Serializable]
public class TreasureSetData
{
    public string setId;
    public string setName;
    public string createdBy;
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