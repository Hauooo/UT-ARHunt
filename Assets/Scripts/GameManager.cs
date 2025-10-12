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
    [SerializeField] private string arSceneName = "ARScene"; // Set this to the name of your AR scene in the Inspector

    public GameMode CurrentMode { get; private set; }

    public List<TreasureManagerGPS_Multiplayer.TreasureData> newSetTreasure = new List<TreasureManagerGPS_Multiplayer.TreasureData>();

    // --- Public Properties & Events ---
    public string CurrentRoomId { get; private set; }
    public bool IsHost { get; private set; }

    // Events for the UI to subscribe to, decoupling the UI from the GameManager.
    public event Action<string, bool> OnLobbyReady; // string: roomId, bool: isHost
    public event Action<Dictionary<string, PlayerData>> OnPlayerListUpdated;
    public event Action OnGameStarting;
    public event Action<string> OnJoinFailed;

    // --- Private State ---
    private DatabaseReference dbRef;
    private bool isFirebaseReady = false;

    #region --- Unity Methods ---

    private void Awake()
    {
        string stackTrace = UnityEngine.StackTraceUtility.ExtractStackTrace();
        Debug.LogError($"--- A GameManager just ran Awake() in scene '{gameObject.scene.name}' on GameObject: '{gameObject.name}' [ID: {gameObject.GetInstanceID()}] ---\nCreation Stack Trace:\n{stackTrace}", gameObject);

        // Check FIRST — before doing anything else
        if (Instance != null && Instance != this)
        {
            Debug.LogError($"Duplicate GameManager detected! Keeping ID: {Instance.GetInstanceID()}, destroying new ID: {this.GetInstanceID()} (Scene: {gameObject.scene.name})");
            Destroy(gameObject);
            return;
        }

        // If we reach here, we are the one true instance
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Subscribe to AuthManager
        AuthManager.Instance.OnSignedIn += HandleAuthReady;

        // If user already signed in, initialize immediately
        if (AuthManager.Instance.User != null)
        {
            Debug.Log("AuthManager already has a signed-in user. Initializing Firebase immediately.");
            HandleAuthReady(AuthManager.Instance.User);
        }
    }


    private void Start()
    {
        // --- REMOVE THIS ---
        // We no longer initialize Firebase here.
        // dbRef = FirebaseDatabase.DefaultInstance.RootReference;
        // isInitialized = true;

        Debug.Log("GameManager initialized.");
    }

    private void OnEnable()
    {
        var managers = FindObjectsOfType<GameManager>();
        if (managers.Length > 1)
        {
            Debug.LogError($"[GameManager] {managers.Length} instances found! Destroying duplicates...");
            foreach (var gm in managers)
            {
                if (gm != Instance)
                    Destroy(gm.gameObject);
            }
        }
        // Subscribe to the sceneLoaded event to know when the AR scene is ready.
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        // If this message appears, it is the smoking gun.
        Debug.LogError($"!!! A GameManager instance [ID: {gameObject.GetInstanceID()}] was JUST DESTROYED !!! Is there another script loading scenes in a way that breaks DontDestroyOnLoad?", gameObject);

        // Unsubscribe to prevent memory leaks
        if (AuthManager.Instance != null)
        {
            AuthManager.Instance.OnSignedIn -= HandleAuthReady;
        }
    }

    private void HandleAuthReady(Firebase.Auth.FirebaseUser user)
    {
        Debug.Log($"GameManager [ID: {gameObject.GetInstanceID()}] received 'OnSignedIn' signal. Initializing Firebase Database.");
        dbRef = FirebaseDatabase.DefaultInstance.RootReference;
        isFirebaseReady = true;

        // We will add a log here to prove dbRef is NOT null.
        if (dbRef != null)
        {
            Debug.Log($"GameManager [ID: {gameObject.GetInstanceID()}] CONFIRMS: dbRef is now INITIALIZED and NOT NULL.");
        }
        else
        {
            Debug.LogError($"GameManager [ID: {gameObject.GetInstanceID()}] FAILED to initialize dbRef!");
        }
    }

    #endregion

    #region --- Public API for UI ---

    /// <summary>
    /// Called by the UI when the user wants to host a new game.
    /// </summary>
    public void HostNewRoom(TreasureSetData treasureSet)
    {
        if (!isFirebaseReady || AuthManager.Instance.User == null)
        {
            Debug.LogError("GameManager or Auth not ready!");
            return;
        }

        IsHost = true;
        CurrentRoomId = GenerateRoomCode();

        RoomData newRoom = new RoomData(
            AuthManager.Instance.UserId,
            AuthManager.Instance.User.DisplayName ?? "The Host",
            treasureSet.setId // Assuming TreasureSetData has an ID
        );

        // Copy treasures from the set to the new room's gameState.
        foreach (var treasure in treasureSet.treasures)
        {
            // We need a unique key for the live game state, so we generate one.
            string liveTreasureKey = dbRef.Child("rooms").Child(CurrentRoomId).Child("gameState").Push().Key;

            // Create a new instance to avoid reference issues.
            var liveTreasure = new TreasureManagerGPS_Multiplayer.TreasureData
            {
                name = treasure.name,
                lat = treasure.lat,
                lon = treasure.lon,
                points = treasure.points,
                collectedBy = new Dictionary<string, bool>()
            };
            newRoom.gameState[liveTreasureKey] = liveTreasure; // The live game still uses a Dictionary for fast lookups.
        }

        // Add the host as the first player.
        newRoom.players[AuthManager.Instance.UserId] = new PlayerData(AuthManager.Instance.User.DisplayName ?? "The Host", 0);

        // Set the data in Firebase.
        dbRef.Child("rooms").Child(CurrentRoomId).SetRawJsonValueAsync(JsonUtility.ToJson(newRoom)).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("Failed to host room: " + task.Exception);
                return;
            }
            Debug.Log($"Room {CurrentRoomId} hosted successfully!");
            ListenToRoomUpdates(CurrentRoomId);
            OnLobbyReady?.Invoke(CurrentRoomId, true);
        });
    }

    /// <summary>
    /// Called by the UI when a player wants to join an existing game.
    /// </summary>
    public void JoinRoomById(string roomId)
    {
        if (!isFirebaseReady || AuthManager.Instance.User == null) return;

        IsHost = false;
        CurrentRoomId = roomId.ToUpper(); // Standardize to uppercase.

        DatabaseReference roomRef = dbRef.Child("rooms").Child(CurrentRoomId);
        roomRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                OnJoinFailed?.Invoke("Error checking room.");
                return;
            }
            if (!task.Result.Exists || task.Result.Child("status").Value.ToString() != "waiting")
            {
                OnJoinFailed?.Invoke($"Room {CurrentRoomId} is not available to join.");
                return;
            }

            // Room is valid, add the player.
            PlayerData newPlayer = new PlayerData(AuthManager.Instance.User.DisplayName ?? "A Player", 0);
            roomRef.Child("players").Child(AuthManager.Instance.UserId).SetRawJsonValueAsync(JsonUtility.ToJson(newPlayer)).ContinueWithOnMainThread(joinTask =>
            {
                if (joinTask.IsFaulted)
                {
                    OnJoinFailed?.Invoke("Failed to join room.");
                    return;
                }
                Debug.Log($"Joined room {CurrentRoomId}!");
                ListenToRoomUpdates(CurrentRoomId);
                OnLobbyReady?.Invoke(CurrentRoomId, false);
            });
        });
    }

    /// <summary>
    /// Called by the host from the UI to start the game for everyone.
    /// </summary>
    public void StartGame()
    {
        if (!IsHost) return;
        // This write will trigger HandleStatusChanged for all connected clients.
        dbRef.Child("rooms").Child(CurrentRoomId).Child("status").SetValueAsync("in-progress");
    }

    /// <summary>
    /// Called when a player wants to leave the lobby or game.
    /// </summary>
    public void LeaveRoom()
    {
        if (string.IsNullOrEmpty(CurrentRoomId)) return;

        // Remove player from list. If host leaves, you might want to add logic to end the game.
        dbRef.Child("rooms").Child(CurrentRoomId).Child("players").Child(AuthManager.Instance.UserId).RemoveValueAsync();

        StopListeningToRoomUpdates();
        CurrentRoomId = null;
        // Consider loading back to the main menu scene here.
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

        // Manually parse the dictionary from Firebase into our strongly-typed class.
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

        string status = args.Snapshot.Value.ToString();
        if (status == "in-progress")
        {
            Debug.Log("Game is starting! Loading AR Scene.");
            OnGameStarting?.Invoke();
            StopListeningToRoomUpdates();
            SceneManager.LoadScene(arSceneName);
        }
    }

    #endregion

    #region --- Scene Management ---

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == arSceneName)
        {
            // We've just loaded the AR Scene. Find the TreasureManager and initialize it.
            var treasureManager = FindObjectOfType<TreasureManagerGPS_Multiplayer>();
            if (treasureManager != null)
            {
                treasureManager.InitializeForRoom(CurrentRoomId);
            }
            else
            {
                Debug.LogError("Loaded AR Scene but couldn't find TreasureManager!");
            }
        }
    }

    #endregion

    #region --- Helpers ---
    private string GenerateRoomCode(int length = 4)
    {
        const string chars = "ABCDEFGHIJKLMNPQRSTUVWXYZ123456789";
        var random = new System.Random();
        var code = new char[length];
        for (int i = 0; i < length; i++)
        {
            code[i] = chars[random.Next(chars.Length)];
        }
        return new String(code);
    }
    #endregion


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

        if (!isFirebaseReady)
        {
            Debug.LogWarning("Firebase not ready yet — delaying save attempt...");
            StartCoroutine(RetrySaveTreasureSet(setName));
            return;
        }


        // --- BREADCRUMB 1: Check for null references ---
        if (dbRef == null)
        {
            Debug.LogError("FATAL: dbRef is NULL. The Firebase Database reference was not initialized.");
            return; // Stop execution
        }
        if (AuthManager.Instance == null)
        {
            Debug.LogError("FATAL: AuthManager.Instance is NULL. The AuthManager is missing.");
            return; // Stop execution
        }
        if (AuthManager.Instance.User == null)
        {
            Debug.LogError("FATAL: AuthManager.Instance.User is NULL. The user is not signed in.");
            return; // Stop execution
        }
        Debug.Log("BREADCRUMB 1 PASSED: All manager references are valid.");

        // --- BREADCRUMB 2: Check for valid data ---
        if (newSetTreasure.Count == 0)
        {
            Debug.LogError("No treasures to save.");
            return;
        }
        Debug.Log($"BREADCRUMB 2 PASSED: Found {newSetTreasure.Count} treasures to save.");

        // --- BREADCRUMB 3: Prepare the data object ---
        string newSetId = dbRef.Child("treasureSets").Push().Key;
        TreasureSetData newSet = new TreasureSetData
        {
            setId = newSetId,
            setName = setName,
            createdBy = AuthManager.Instance.UserId,
            treasures = this.newSetTreasure
        };
        Debug.Log("BREADCRUMB 3 PASSED: TreasureSetData object created successfully.");

        // --- BREADCRUMB 4: Convert to JSON and save ---
        string json = JsonUtility.ToJson(newSet);
        Debug.Log("BREADCRUMB 4 PASSED: Data converted to JSON. Sending to Firebase...");

        dbRef.Child("treasureSets").Child(newSetId).SetRawJsonValueAsync(json).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("BREADCRUMB 5 FAILED: Failed to save treasure set: " + task.Exception);
            }
            else
            {
                Debug.Log("BREADCRUMB 5 SUCCESS: Treasure set saved successfully!");
                ExitCreatorMode();
            }
        });

        Debug.Log("--- SaveNewTreasureSet END ---");
    }

    private IEnumerator RetrySaveTreasureSet(string setName)
    {
        float timeout = 5f;
        while (!isFirebaseReady && timeout > 0f)
        {
            yield return new WaitForSeconds(0.5f);
            timeout -= 0.5f;
        }

        if (isFirebaseReady)
            SaveNewTreasureSet(setName);
        else
            Debug.LogError("Firebase never became ready in time.");
    }


    public void ReturnToMenu()
    {
        Debug.Log("Returning to Main Menu...");

        // Stop any active Firebase listeners to prevent errors in the menu scene.
        StopListeningToRoomUpdates();

        // Reset the game state.
        CurrentMode = GameMode.InMenu;
        CurrentRoomId = null;
        IsHost = false;
        newSetTreasure.Clear(); // Clear any temporary creation data.

        // Load the menu scene. Make sure your scene is named "MenuScene".
        SceneManager.LoadScene("MenuScene");
    }
}


#region --- Data Structures ---

// These classes define the structure of your data in Firebase.
// They should be [Serializable] to work with JsonUtility.

[Serializable]
public class TreasureSetData
{
    public string setId;
    public string setName;
    public string createdBy;
    public List<TreasureManagerGPS_Multiplayer.TreasureData> treasures = new List<TreasureManagerGPS_Multiplayer.TreasureData>();
}

[Serializable]
public class RoomData
{
    public string hostId;
    public string hostName;
    public string selectedSetId;
    public string status; // "waiting", "in-progress", "finished"
    public Dictionary<string, PlayerData> players;
    public Dictionary<string, TreasureManagerGPS_Multiplayer.TreasureData> gameState;

    public RoomData(string hostId, string hostName, string setId)
    {
        this.hostId = hostId;
        this.hostName = hostName;
        this.selectedSetId = setId;
        this.status = "waiting";
        this.players = new Dictionary<string, PlayerData>();
        this.gameState = new Dictionary<string, TreasureManagerGPS_Multiplayer.TreasureData>();
    }
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


