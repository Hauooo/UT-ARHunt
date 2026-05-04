using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;
using Firebase.Auth;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Linq;

public class TreasureManagerGPS_Multiplayer : MonoBehaviour
{
    // --- Data Structures ---
    [Serializable]
    public class TreasureData
    {
        public string name;
        public double lat;
        public double lon;
        public long points;
        public int orderIndex; // NEW: sequence index for InOrder mode

        public Dictionary<string, bool> collectedBy = new Dictionary<string, bool>();
        public ChallengeData challenge;

        public TreasureData() { }

        public TreasureData(string name, double lat, double lon, long points)
        {
            this.name = name;
            this.lat = lat;
            this.lon = lon;
            this.points = points;
            this.orderIndex = 0;
            this.collectedBy = new Dictionary<string, bool>();
        }
    }

    // Local runtime treasure state
    public class Treasure
    {
        public string key;
        public TreasureData data;
        public GameObject instance;
    }

    public enum PlayerMode { Setter, Finder }

    [Header("Game Mode")]
    public PlayerMode mode = PlayerMode.Finder;

    private List<TreasureData> treasures = new List<TreasureData>();

    [Header("AR & Game Settings")]
    [SerializeField] private ARRaycastManager arRaycastManager;
    [SerializeField] private GameObject treasurePrefab;
    [SerializeField] private float spawnRange = 5f;      // Closer treasures
    [SerializeField] private float revealRadius = 5.0f;   // Easier to find
    [SerializeField] private float collectDistance = 2f;   // Precise tap
    [SerializeField] private float updateInterval = 1.0f;  // More frequent updates

    [Header("Firebase")]
    [SerializeField]
    private string firebaseDatabaseUrl =
        "https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/";

    [Header("UI")]
    [SerializeField] private Button setTreasureButton;
    [SerializeField] private Button collectButton;
    [SerializeField] private Button modeToggleButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text modeLabel;
    [SerializeField] private TMP_Text distanceLabel;
    [SerializeField] private RectTransform arrowIndicator;
    [SerializeField] private ChallengeRunner challengeRunner; // Optional: for future extension to run challenges attached to treasures

    [Header("Timer")]
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private Image timerFill;

    // --- Timer State ---
    private float timeRemaining = 0f;
    private float totalTime = 0f;
    private bool timerRunning = false;

    // --- Service References ---
    private DatabaseReference dbRef;
    private AuthManager authManager;
    private LocationManager locationManager;

    // --- State ---
    private bool servicesReady = false;
    private bool initialized = false;
    private bool isCollectInProgress = false;

    // --- Mode Management ---
    private int roomCollectionMode = (int)CollectionMode.Free; // 0 Free, 1 InOrder
    private int nextTreasureIndex = 0;

    private readonly Dictionary<string, Treasure> localTreasures = new Dictionary<string, Treasure>();
    private string currentTargetKey;
    private string currentRoomId;
    private long runStartLocalMs;

    [Header("Quit Confirmation UI")]
    [SerializeField] private GameObject quitConfirmPanel;
    [SerializeField] private Button quitConfirmYesButton;
    [SerializeField] private Button quitConfirmNoButton;
    [SerializeField] private TMP_Text quitConfirmText;

    // Existing flags
    private bool suppressResultSave = false;
    private bool isExitingLevel = false;

    private void Start()
    {
        Debug.Log($"[TreasureManager] statusText assigned: {statusText != null}");
        Debug.Log($"[TreasureManager] distanceLabel assigned: {distanceLabel != null}");
        Debug.Log($"[TreasureManager] collectButton assigned: {collectButton != null}");
        Debug.Log($"[TreasureManager] arrowIndicator assigned: {arrowIndicator != null}");

        if (collectButton != null)
        {
            collectButton.gameObject.SetActive(false);
        }

        authManager = AuthManager.Instance;
        locationManager = LocationManager.Instance;

        if (authManager == null || locationManager == null)
        {
            Debug.LogError("[TreasureManagerGPS_Multiplayer] Missing required managers!");
            return;
        }

        
        if (challengeRunner == null)
        {
            challengeRunner = FindObjectOfType<ChallengeRunner>();
            if (challengeRunner != null)
            {
                Debug.Log("[TreasureManagerGPS_Multiplayer] Auto-found ChallengeRunner");
            }
            else
            {
                Debug.LogWarning("[TreasureManagerGPS_Multiplayer] ChallengeRunner not found in scene!");
            }
        }

        // Initialize Firebase
        try
        {
            dbRef = FirebaseDatabase.GetInstance(firebaseDatabaseUrl).RootReference;
            Debug.Log("[TreasureManagerGPS_Multiplayer] Firebase initialized");
        }
        catch (Exception ex)
        {
            Debug.LogError("[TreasureManagerGPS_Multiplayer] Firebase init failed: " + ex);
        }

        // Multiplayer flow: ARSceneController calls InitializeForRoom(roomId).
        Debug.Log("[TreasureManagerGPS_Multiplayer] Start() - waiting for InitializeForRoom...");
    }

    private void Awake()
    {
        
        if (quitConfirmPanel != null) quitConfirmPanel.SetActive(false);

        if (quitConfirmYesButton != null)
        {
            quitConfirmYesButton.onClick.RemoveAllListeners();
            quitConfirmYesButton.onClick.AddListener(ConfirmQuitLevelWithoutSaving);
        }

        if (quitConfirmNoButton != null)
        {
            quitConfirmNoButton.onClick.RemoveAllListeners();
            quitConfirmNoButton.onClick.AddListener(CancelQuitLevelWithoutSaving);
        }
    }



    private void OnDestroy()
    {
        StopListeningForTreasures();
        CancelInvoke(nameof(UpdateFinderState));
    }

    private void Update()
    {
        if (!servicesReady) return;

        // Spawn treasures when in range
        if (!string.IsNullOrEmpty(currentTargetKey)
            && localTreasures.ContainsKey(currentTargetKey)
            && localTreasures[currentTargetKey].instance == null
            && CanScanForTreasure())
        {
            TrySpawnTreasure();
        }

        // Update timer
        if (timerRunning)
        {
            timeRemaining -= Time.deltaTime;
            if (timeRemaining <= 0)
            {
                timeRemaining = 0;
                timerRunning = false;
                HandleTimeUp();
            }
            UpdateTimerUI();
        }

        // Update UI elements
        UpdateUIElements();
    }

    /// <summary>
    /// Start the game timer with duration from level or challenge
    /// </summary>
    public void StartGameTimer(float durationSeconds)
    {
        totalTime = durationSeconds;
        timeRemaining = durationSeconds;
        timerRunning = true;
        Debug.Log($"[Timer] Game started with {durationSeconds}s limit");
    }

    /// <summary>
    /// Stop the timer (when game completes)
    /// </summary>
    public void StopGameTimer()
    {
        timerRunning = false;
        Debug.Log("[Timer] Game timer stopped");
    }

    private void UpdateTimerUI()
    {
        if (timerText != null)
        {
            int minutes = (int)timeRemaining / 60;
            int seconds = (int)timeRemaining % 60;
            timerText.text = $"{minutes:D2}:{seconds:D2}";
        }

        if (timerFill != null)
        {
            timerFill.fillAmount = timeRemaining / totalTime;

            // Change color as time runs out
            if (timeRemaining / totalTime > 0.5f)
                timerFill.color = Color.green;
            else if (timeRemaining / totalTime > 0.25f)
                timerFill.color = Color.yellow;
            else
                timerFill.color = Color.red;
        }
    }


    /// <summary>
    /// Get all treasures in the current set for uploading
    /// </summary>
    public List<TreasureData> GetAllTreasures()
    {
        if (localTreasures == null || localTreasures.Count == 0)
        {
            Debug.LogWarning("[TreasureManager] No treasures loaded");
            return new List<TreasureData>();
        }

        List<TreasureData> treasuresList = new List<TreasureData>();
        foreach (var treasure in localTreasures.Values)
        {
            treasuresList.Add(treasure.data);
        }

        Debug.Log($"[TreasureManager] Returning {treasuresList.Count} treasures for upload");
        return treasuresList;
    }

    public void InitializeForRoom(string roomId)
    {
        Debug.Log($"[TreasureManagerGPS_Multiplayer] Initializing for room: {roomId}");
        currentRoomId = roomId;

        ResetForNewLevel();

        // Ensure deps FIRST
        if (dbRef == null)
            dbRef = FirebaseDatabase.GetInstance(firebaseDatabaseUrl).RootReference;

        if (authManager == null)
            authManager = AuthManager.Instance;

        var user = FirebaseAuth.DefaultInstance?.CurrentUser;
        string uid = authManager?.UserId;
        if (string.IsNullOrEmpty(uid)) uid = user?.UserId;

        string displayName = user?.DisplayName;
        if (string.IsNullOrWhiteSpace(displayName)) displayName = "Player";

        // Start markers
        runStartLocalMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SaveRunStart(roomId, uid, displayName);

    // Try to load treasures from GameManager first (if they exist there), otherwise fall back to Firebase
    var gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            Debug.LogWarning("[TreasureManagerGPS_Multiplayer] GameManager.Instance is NULL");
            LoadTreasuresFromFirebase(roomId);
            return;
        }

        var currentLevelTreasures = gameManager.CurrentLevelTreasures;
        if (currentLevelTreasures == null || currentLevelTreasures.Count == 0)
        {
            Debug.Log("[TreasureManagerGPS_Multiplayer] No treasures in CurrentLevelTreasures, loading from Firebase");
            LoadTreasuresFromFirebase(roomId);
            return;
        }

    // Start the game timer with a duration (get from level metadata or default)
        float timerDuration = gameManager.CurrentLevelTimerSeconds > 0 ? gameManager.CurrentLevelTimerSeconds : 300f; // default 5 minutes
        StartGameTimer(timerDuration);

    // Debug logging
    foreach (var t in currentLevelTreasures)
        {
            Debug.Log($"[PreInit] {t.name} | chType={t.challenge?.type} | optCount={(t.challenge?.options == null ? -1 : t.challenge.options.Count)}");
        }

        Debug.Log($"[TreasureManagerGPS_Multiplayer] Loading {currentLevelTreasures.Count} treasures from level");
        nextTreasureIndex = 0;
        InitializeTreasuresFromList(currentLevelTreasures);
    }

    private void ResetForNewLevel()
    {
        Debug.Log("[TreasureManager] Resetting state for new level");

        // Hide end game panel
        if (GameEndManager.Instance != null)
            GameEndManager.Instance.HidePanel();

        // Clear treasures and state
        localTreasures.Clear();
        currentTargetKey = null;
        nextTreasureIndex = 0;

        // Reset flags
        isCollectInProgress = false;
        servicesReady = false;
        initialized = false;

        // Hide UI
        if (collectButton != null)
        {
            collectButton.gameObject.SetActive(false);
            collectButton.interactable = false;
        }

        if (statusText != null)
            statusText.text = "";

        // Stop any ongoing loops
        CancelInvoke(nameof(UpdateFinderState));
        StopListeningForTreasures();
    }

    

    private void SaveRunStart(string roomId, string uid, string displayName)
    {
        if (dbRef == null || string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(uid))
        {
            Debug.LogWarning("[SaveRunStart] Missing dbRef/roomId/uid");
            return;
        }

        bool isSingle = roomId.StartsWith("-");
        string root = isSingle ? "levels" : "rooms";

        DatabaseReference playerRef = dbRef.Child(root).Child(roomId).Child("players").Child(uid);

        var data = new Dictionary<string, object>
    {
        { "uid", uid },
        { "displayName", string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName },
        { "startAt", ServerValue.Timestamp }
    };

        playerRef.UpdateChildrenAsync(data).ContinueWithOnMainThread(t =>
        {
            if (t.IsFaulted || t.IsCanceled)
                Debug.LogWarning("[SaveRunStart] Failed: " + t.Exception);
            else
                Debug.Log($"[SaveRunStart] OK -> {root}/{roomId}/players/{uid}");
        });

        playerRef.Child("endAt").RemoveValueAsync();
        playerRef.Child("timeTakenMs").RemoveValueAsync();
        playerRef.Child("elapsedTime").RemoveValueAsync();
    }


    private void SaveRunEnd(string roomId, string uid, int finalScore, long timeTakenMs)
    {
        bool isSingle = roomId.StartsWith("-");
        string root = isSingle ? "levels" : "rooms";

        DatabaseReference baseRef = dbRef.Child(root).Child(roomId);
        DatabaseReference playerRef = baseRef.Child("players").Child(uid);
        DatabaseReference scoreRef = baseRef.Child("scores").Child(uid);

        var playerUpdates = new Dictionary<string, object>
    {
        { "endAt", ServerValue.Timestamp },
        { "timeTakenMs", timeTakenMs },
        { "elapsedTime", timeTakenMs / 1000L } // optional legacy
    };

        playerRef.UpdateChildrenAsync(playerUpdates); // overwrite keys
        scoreRef.SetValueAsync(finalScore);           // ← REPLACE score, not add to it

        Debug.Log($"[SaveRunEnd] Replaced score for {uid}: score={finalScore}, timeMs={timeTakenMs}");
    }


    private void LoadTreasuresFromFirebase(string roomId)
    {
        if (dbRef == null)
        {
            Debug.LogError("[TreasureManagerGPS_Multiplayer] Firebase not initialized!");
            return;
        }

        Debug.Log($"[TreasureManagerGPS_Multiplayer] Loading treasures from Firebase for room: {roomId}");
        LogToUI("Loading treasures...");

        // Check if it's a single player level or a multiplayer room
        string rootFolder = roomId.StartsWith("-") ? "levels" : "rooms";

        dbRef.Child(rootFolder).Child(roomId).GetValueAsync().ContinueWithOnMainThread(roomTask =>
        {
            if (!roomTask.IsFaulted && roomTask.Result.Exists)
            {
                var roomSnap = roomTask.Result;

                // Safely parse the collection mode whether it was saved as an int (1) or string ("In Order")
                if (roomSnap.HasChild("collectionMode"))
                {
                    string modeStr = roomSnap.Child("collectionMode").Value?.ToString();
                    if (modeStr == "1" || modeStr == "In Order")
                    {
                        roomCollectionMode = 1; // 1 = InOrder
                    }
                    else
                    {
                        roomCollectionMode = 0; // 0 = FreeOrder
                    }
                }

                if (roomSnap.HasChild("nextTreasureIndex") &&
                    int.TryParse(roomSnap.Child("nextTreasureIndex").Value?.ToString(), out int idx))
                {
                    nextTreasureIndex = idx;
                }

                Debug.Log($"[TreasureManager] Loaded Collection Mode: {roomCollectionMode} (0=Free, 1=InOrder)");
            }
        });

        dbRef.Child("rooms").Child(roomId).Child("gameState")
            .GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError("[TreasureManagerGPS_Multiplayer] Error loading treasures: " + task.Exception);
                    LogToUI("Error loading treasures.");
                    return;
                }

                if (!task.Result.Exists)
                {
                    Debug.Log("[TreasureManagerGPS_Multiplayer] No treasures in this room yet.");
                    LogToUI("No treasures placed yet. Waiting...");
                    servicesReady = true;
                    StartListeningForTreasures();
                    return;
                }

                // Load all treasures from the snapshot
                foreach (var treasureSnapshot in task.Result.Children)
                {
                    try
                    {
                        TreasureData data = ParseTreasureDataFromSnapshot(treasureSnapshot);

                        // Skip if already collected
                        bool alreadyCollected = data.collectedBy != null && data.collectedBy.Count > 0;
                        if (alreadyCollected) continue;

                        var treasure = new Treasure
                        {
                            key = treasureSnapshot.Key,
                            data = data,
                            instance = null
                        };

                        localTreasures[treasureSnapshot.Key] = treasure;
                        Debug.Log($"[TreasureManagerGPS_Multiplayer] Loaded treasure: {data.name} (key: {treasureSnapshot.Key})");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[TreasureManagerGPS_Multiplayer] Error parsing treasure: " + ex);
                    }
                }

                servicesReady = true;
                initialized = true;

                // Wire buttons and setup UI
                WireButtons();
                SetupUI();

                // Start listening for new treasures and changes
                StartListeningForTreasures();
                InvokeRepeating(nameof(UpdateFinderState), 1.0f, updateInterval);

                LogToUI($"Loaded {localTreasures.Count} treasures. Ready to hunt!");
                Debug.Log($"[TreasureManagerGPS_Multiplayer] Room initialized with {localTreasures.Count} treasures");
            });
    }

    private void InitializeTreasuresFromList(List<TreasureManagerGPS_Multiplayer.TreasureData> treasures)
    {
        servicesReady = true;
        initialized = true;

        var gameManager = GameManager.Instance;
        var treasureKeys = gameManager?.TreasureKeys;

        Debug.Log($"[TreasureManagerGPS_Multiplayer] TreasureKeys from GameManager: {(treasureKeys != null ? treasureKeys.Count : 0)} mappings");
        if (treasureKeys != null)
        {
            foreach (var kvp in treasureKeys)
            {
                Debug.Log($"[TreasureManagerGPS_Multiplayer] Key mapping: {kvp.Value} <- {kvp.Key.name}");
            }
        }

        for (int i = 0; i < treasures.Count; i++)  // ← Use index
        {
            var treasure = treasures[i];

            // Get the Firebase key for this treasure
            string validKey = $"treasure_{i}";  // ← NEW: Use index-based key

            if (treasureKeys != null && treasureKeys.TryGetValue(treasure, out string key))
            {
                validKey = key;
                Debug.Log($"[TreasureManagerGPS_Multiplayer] ✓ Found mapping for '{treasure.name}': {validKey}");
            }
            else
            {
                Debug.LogWarning($"[TreasureManagerGPS_Multiplayer] ✗ No mapping found for '{treasure.name}', using: {validKey}");
            }

            var treasureObj = new Treasure
            {
                key = validKey,
                data = treasure,
                instance = null
            };

            localTreasures[validKey] = treasureObj;  // ← Use validKey as dictionary key
            Debug.Log($"[TreasureManagerGPS_Multiplayer] Loaded treasure: {treasure.name} (key: {validKey})");
        }

        WireButtons();
        SetupUI();
        StartListeningForTreasures();
        InvokeRepeating(nameof(UpdateFinderState), 1.0f, updateInterval);

        LogToUI("Level loaded! Find the treasures.");
        Debug.Log($"[TreasureManagerGPS_Multiplayer] Initialized with {treasures.Count} treasures");
    }

    private void WireButtons()
    {
        if (setTreasureButton != null)
        {
            setTreasureButton.onClick.RemoveAllListeners();
            setTreasureButton.onClick.AddListener(SetTreasureHere);
        }
        else
        {
            Debug.LogWarning("[TreasureManagerGPS_Multiplayer] setTreasureButton not assigned.");
        }

        if (collectButton != null)
        {
            collectButton.onClick.RemoveAllListeners();
            collectButton.onClick.AddListener(CollectTargetTreasure);
        }
        else
        {
            Debug.LogWarning("[TreasureManagerGPS_Multiplayer] collectButton not assigned.");
        }

        if (modeToggleButton != null)
        {
            modeToggleButton.onClick.RemoveAllListeners();
            modeToggleButton.onClick.AddListener(ToggleMode);
        }
    }

    // --- MULTIPLAYER TREASURE SYSTEM ---

    private void SetTreasureHere()
    {
        if (!servicesReady) { LogToUI("Services not ready."); return; }
        if (string.IsNullOrEmpty(currentRoomId)) { LogToUI("No active room."); return; }

        DatabaseReference gameStateRef = dbRef.Child("rooms").Child(currentRoomId).Child("gameState");
        string newId = gameStateRef.Push().Key;

        TreasureData treasure = new TreasureData
        {
            name = "Treasure",
            lat = locationManager.Latitude,
            lon = locationManager.Longitude,
            points = 0,
            collectedBy = null
        };

        gameStateRef.Child(newId).SetRawJsonValueAsync(JsonUtility.ToJson(treasure))
            .ContinueWithOnMainThread(t =>
            {
                if (t.IsFaulted) LogToUI("Error adding treasure: " + t.Exception);
                else LogToUI("Treasure placed successfully!");
            });
    }

    private void StartListeningForTreasures()
    {
        if (dbRef == null || string.IsNullOrEmpty(currentRoomId)) return;

        DatabaseReference roomRef = dbRef.Child("rooms").Child(currentRoomId);
        DatabaseReference gameStateRef = roomRef.Child("gameState");

        gameStateRef.ChildAdded += HandleTreasureAdded;
        gameStateRef.ChildChanged += HandleTreasureChanged;

        // NEW: keep order state synced
        roomRef.Child("nextTreasureIndex").ValueChanged += HandleNextTreasureIndexChanged;
        roomRef.Child("collectionMode").ValueChanged += HandleCollectionModeChanged;

        Debug.Log($"[TreasureManager] Listening: rooms/{currentRoomId}/gameState + order state");
    }

    private void StopListeningForTreasures()
    {
        if (dbRef == null || string.IsNullOrEmpty(currentRoomId)) return;

        DatabaseReference roomRef = dbRef.Child("rooms").Child(currentRoomId);
        DatabaseReference gameStateRef = roomRef.Child("gameState");

        gameStateRef.ChildAdded -= HandleTreasureAdded;
        gameStateRef.ChildChanged -= HandleTreasureChanged;

        roomRef.Child("nextTreasureIndex").ValueChanged -= HandleNextTreasureIndexChanged;
        roomRef.Child("collectionMode").ValueChanged -= HandleCollectionModeChanged;
    }

    private void HandleNextTreasureIndexChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null || !args.Snapshot.Exists) return;
        if (int.TryParse(args.Snapshot.Value?.ToString(), out int idx))
            nextTreasureIndex = idx;
    }

    private void HandleCollectionModeChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null || !args.Snapshot.Exists) return;
        if (int.TryParse(args.Snapshot.Value?.ToString(), out int mode))
            roomCollectionMode = mode;
    }

    private void HandleTreasureAdded(object sender, ChildChangedEventArgs args)
    {
        if (args.DatabaseError != null) return;

        string key = args.Snapshot.Key;
        if (localTreasures.ContainsKey(key)) return;

        TreasureData data = ParseTreasureDataFromSnapshot(args.Snapshot);

        // If it already has ANY collectedBy entry, treat it as already collected and ignore (disappear for everyone)
        bool collectedByAnyone = data.collectedBy != null && data.collectedBy.Count > 0;
        if (collectedByAnyone) return;

        localTreasures[key] = new Treasure { key = key, data = data };
        LogToUI($"New treasure '{data.name}' appeared!");
        Debug.Log($"[TreasureAdded] room={currentRoomId} key={key}");
    }

    private void HandleTreasureChanged(object sender, ChildChangedEventArgs args)
    {
        if (args.DatabaseError != null) return;

        string key = args.Snapshot.Key;
        if (!localTreasures.ContainsKey(key)) return;

        TreasureData newData = ParseTreasureDataFromSnapshot(args.Snapshot);
        localTreasures[key].data = newData;

        
        bool isMarkedCollected = false;

        // Try to get isCollected flag from the snapshot
        var snapshot = args.Snapshot;
        if (snapshot.HasChild("isCollected"))
        {
            object isCollectedObj = snapshot.Child("isCollected").Value;
            if (isCollectedObj is bool boolVal)
                isMarkedCollected = boolVal;
        }

        
        if (!isMarkedCollected) return;

        LogToUI($"Treasure '{newData.name}' was collected!");

        // Destroy spawned instance locally
        if (localTreasures[key].instance != null)
            Destroy(localTreasures[key].instance);

        // Clear target if we were tracking it
        if (currentTargetKey == key)
            currentTargetKey = null;

        // Hide/disable collect UI
        if (collectButton != null)
        {
            collectButton.gameObject.SetActive(false);
            collectButton.interactable = false;
        }

        // Remove from local list so it cannot be targeted/spawned again
        localTreasures.Remove(key);
    }

    public void CollectTargetTreasure()
    {
        if (isExitingLevel) return;
        Debug.Log("[Collect] CollectTargetTreasure() called");
        if (isCollectInProgress) return;

        if (!servicesReady || string.IsNullOrEmpty(currentTargetKey) || !localTreasures.ContainsKey(currentTargetKey))
            return;

        if (string.IsNullOrEmpty(authManager.UserId))
        {
            LogToUI("Cannot collect: user not signed in.");
            return;
        }

        // Lock immediately so double-taps can't queue two challenges
        isCollectInProgress = true;
        if (collectButton != null) collectButton.interactable = false;

        Treasure target = localTreasures[currentTargetKey];

        if (roomCollectionMode == (int)CollectionMode.InOrder && target.data.orderIndex != nextTreasureIndex)
        {
            LogToUI($"Collect checkpoint #{nextTreasureIndex + 1} first.");
            isCollectInProgress = false;
            if (collectButton != null) collectButton.interactable = true;
            return;
        }

        // ── Check if this checkpoint has a challenge ─────────────────────
        bool hasChallenge = target.data.challenge != null
                 && target.data.challenge.type != ChallengeType.None;

        Debug.Log($"[Collect] hasChallenge={hasChallenge}, challengeNull={(target.data.challenge == null)}, challengeType={(target.data.challenge != null ? target.data.challenge.type.ToString() : "null")}, runnerNull={(challengeRunner == null)}");


        if (hasChallenge && challengeRunner != null)
        {
            LogToUI("Complete the challenge to collect this treasure!");

            challengeRunner.RunChallenge(target.data.challenge, (success, bonusPoints) =>
            {
                if (!success)
                {
                    // Challenge failed — release the lock and let player retry later
                    isCollectInProgress = false;
                    if (collectButton != null) collectButton.interactable = true;
                    LogToUI("Challenge failed. Try again!");
                    return;
                }

                // Challenge passed — run the Firebase transaction
                LogToUI($"Challenge passed! +{bonusPoints} bonus pts. Collecting...");
                RunCollectTransaction(target, bonusPoints);
            });
        }
        else
        {
            // No challenge — go straight to the transaction
            RunCollectTransaction(target, 0);
        }
    }

    private void RunCollectTransaction(Treasure target, int bonusPoints)
    {
        string myUserId = authManager.UserId;
        bool isSinglePlayer = currentRoomId.StartsWith("-");

        Debug.Log($"[RunCollectTransaction] isSinglePlayer={isSinglePlayer}, userId={myUserId}, targetKey={target.key}");

        // For single-player, save to a separate collection to track who collected what
        if (isSinglePlayer)
        {
            var collectedData = new Dictionary<string, object>
        {
            { "treasureName", target.data.name },
            { "treasureKey", target.key },
            { "collectedBy", myUserId },
            { "points", target.data.points + bonusPoints }
        };

            dbRef.Child("levels").Child(currentRoomId)
                 .Child("collectedTreasures").Child(myUserId)
                 .Child(target.key)
                 .SetValueAsync(collectedData)
                 .ContinueWithOnMainThread(task =>
                 {
                     isCollectInProgress = false;
                     if (collectButton != null) collectButton.interactable = true;

                     if (task.IsFaulted)
                     {
                         Debug.LogError("Failed to save collected treasure: " + task.Exception);
                         LogToUI("Collect failed");
                         return;
                     }

                     // SUCCESS
                     long totalEarned = target.data.points + bonusPoints;

                     if (roomCollectionMode == (int)CollectionMode.InOrder)
                     {
                         nextTreasureIndex++;
                     }

                     if (localTreasures.ContainsKey(target.key))
                     {
                         if (localTreasures[target.key].instance != null)
                             Destroy(localTreasures[target.key].instance);
                         localTreasures.Remove(target.key);
                     }

                     if (collectButton != null)
                     {
                         collectButton.gameObject.SetActive(false);
                         collectButton.interactable = false;
                     }

                     currentTargetKey = null;
                     LogToUI($"Treasure collected! +{totalEarned} points");
                     Debug.Log($"[TreasureManager] ✓ Successfully collected '{target.data.name}'!");

                     Invoke(nameof(CheckForRemainingTreasures), 2f);
                 });

            return;
        }

        // MULTIPLAYER PATH - simplified approach
        // Instead of transaction on room root, just update the treasure directly

        DatabaseReference treasureRef = dbRef.Child("rooms").Child(currentRoomId)
                                             .Child("gameState").Child(target.key);

        treasureRef.RunTransaction(mutableData =>
        {
            Debug.Log("[Transaction] Starting treasure transaction...");

            if (mutableData.Value == null)
            {
                Debug.LogError("[Transaction] Treasure data is NULL");
                return TransactionResult.Abort();
            }

            if (mutableData.Value is not Dictionary<string, object> treasureData)
            {
                Debug.LogError($"[Transaction] Treasure is wrong type: {mutableData.Value.GetType()}");
                return TransactionResult.Abort();
            }

            // Already collected?
            bool isCollected = false;
            if (treasureData.TryGetValue("isCollected", out object isCollectedObj) && isCollectedObj != null)
            {
                if (isCollectedObj is bool b)
                    isCollected = b;
                else
                    bool.TryParse(isCollectedObj.ToString(), out isCollected);
            }

            if (isCollected)
            {
                Debug.LogError("[Transaction] Treasure already collected");
                return TransactionResult.Abort();
            }

            // Mark collected
            Dictionary<string, object> collectedBy;
            if (treasureData.TryGetValue("collectedBy", out object collectedByObj) &&
                collectedByObj is Dictionary<string, object> existingCollectedBy)
            {
                collectedBy = existingCollectedBy;
            }
            else
            {
                collectedBy = new Dictionary<string, object>();
            }

            collectedBy[myUserId] = true;
            treasureData["collectedBy"] = collectedBy;
            treasureData["isCollected"] = true;

            if (bonusPoints > 0)
                treasureData["bonusPoints"] = bonusPoints;

            mutableData.Value = treasureData;
            Debug.Log("[Transaction] Treasure transaction committed successfully");
            return TransactionResult.Success(mutableData);

        }).ContinueWithOnMainThread(task =>
        {
            isCollectInProgress = false;
            if (collectButton != null) collectButton.interactable = true;

            if (task.IsFaulted || task.IsCanceled)
            {
                LogToUI("Collect failed.");
                Debug.LogError("[TreasureManager] Collect transaction faulted: " + task.Exception);
                return;
            }

            // SUCCESS
            long totalEarned = target.data.points + bonusPoints;

            // Update player score
            dbRef.Child("rooms").Child(currentRoomId)
                 .Child("scores").Child(myUserId)
                 .RunTransaction(scoreData =>
                 {
                     long current = scoreData.Value == null ? 0 : (long)scoreData.Value;
                     scoreData.Value = current + totalEarned;
                     return TransactionResult.Success(scoreData);
                 });

            // Clean up local state
            if (localTreasures.ContainsKey(target.key))
            {
                if (localTreasures[target.key].instance != null)
                    Destroy(localTreasures[target.key].instance);
                localTreasures.Remove(target.key);
            }

            if (collectButton != null)
            {
                collectButton.gameObject.SetActive(false);
                collectButton.interactable = false;
            }

            currentTargetKey = null;
            LogToUI($"Treasure collected! +{totalEarned} points");
            Debug.Log($"[TreasureManager] Treasure '{target.data.name}' collected.");

            Invoke(nameof(CheckForRemainingTreasures), 2f);
        });
    }

    private TreasureData ParseTreasureDataFromSnapshot(DataSnapshot snap)
    {
        var data = new TreasureData();

        data.name = snap.Child("name").Value?.ToString() ?? "Treasure";
        double.TryParse(snap.Child("lat").Value?.ToString(), out data.lat);
        double.TryParse(snap.Child("lon").Value?.ToString(), out data.lon);
        long.TryParse(snap.Child("points").Value?.ToString(), out data.points);
        int.TryParse(snap.Child("orderIndex").Value?.ToString(), out data.orderIndex);

        data.collectedBy = new Dictionary<string, bool>();
        if (snap.HasChild("collectedBy"))
        {
            foreach (var c in snap.Child("collectedBy").Children)
            {
                bool v = false;
                bool.TryParse(c.Value?.ToString(), out v);
                data.collectedBy[c.Key] = v;
            }
        }

        if (snap.HasChild("challenge"))
        {
            var ch = snap.Child("challenge");
            var cd = new ChallengeData();

            int typeInt = 0;
            int.TryParse(ch.Child("type").Value?.ToString(), out typeInt);
            cd.type = (ChallengeType)typeInt;

            cd.question = ch.Child("question").Value?.ToString() ?? "";
            int.TryParse(ch.Child("bonusPoints").Value?.ToString(), out cd.bonusPoints);
            int.TryParse(ch.Child("maxAttempts").Value?.ToString(), out cd.maxAttempts);
            int.TryParse(ch.Child("timeLimitSeconds").Value?.ToString(), out cd.timeLimitSeconds);
            cd.minigameId = ch.Child("minigameId").Value?.ToString() ?? "";

            cd.options = new List<MCQOption>();

            if (ch.HasChild("options"))
            {
                foreach (var optSnap in ch.Child("options").Children)
                {
                    var opt = new MCQOption
                    {
                        text = optSnap.Child("text").Value?.ToString() ?? ""
                    };
                    bool isCorrect = false;
                    bool.TryParse(optSnap.Child("isCorrect").Value?.ToString(), out isCorrect);
                    opt.isCorrect = isCorrect;
                    cd.options.Add(opt);
                }
                Debug.Log($"[ParseTreasure] Loaded {cd.options.Count} options for {cd.type}");
            }

            data.challenge = cd;

            Debug.Log($"[ParseTreasure] Challenge: type={cd.type}, question='{cd.question}', " +
                      $"options={cd.options.Count}");
        }

        return data;
    }

    private void DebugPrintSnapshot(DataSnapshot snap, int depth = 0)
    {
        string indent = new string(' ', depth * 2);
        Debug.Log($"{indent}[{snap.Key}]: {snap.Value} (exists={snap.Exists}, children={snap.ChildrenCount})");

        if (snap.HasChild("challenge"))
        {
            var ch = snap.Child("challenge");
            Debug.Log($"{indent}  challenge: type={ch.Child("type").Value}, question='{ch.Child("question").Value}'");

            if (ch.HasChild("options"))
            {
                Debug.Log($"{indent}    options: {ch.Child("options").ChildrenCount} items");
                foreach (var opt in ch.Child("options").Children)
                {
                    Debug.Log($"{indent}      - {opt.Child("text").Value} (correct={opt.Child("isCorrect").Value})");
                }
            }
        }
    }

    /// <summary>
    /// Check if there are any remaining treasures to collect.
    /// If none remain, load the scoreboard. Otherwise, continue the game.
    /// </summary>
    private void CheckForRemainingTreasures()
    {
        // Count uncollected treasures
        int uncollectedCount = 0;
        foreach (var treasure in localTreasures.Values)
        {
            bool alreadyCollected = treasure.data.collectedBy != null && treasure.data.collectedBy.Count > 0;
            if (!alreadyCollected)
                uncollectedCount++;
        }

        Debug.Log($"[TreasureManager] Remaining treasures: {uncollectedCount} / {localTreasures.Count}");

        if (uncollectedCount == 0)
        {
            // No more treasures - go to scoreboard
            Debug.Log("[TreasureManager] All treasures collected! Loading scoreboard...");
            LoadScoreboard();
        }
        else
        {
            // Still treasures remaining - continue game
            Debug.Log($"[TreasureManager] Continue hunting! {uncollectedCount} treasures left.");
            LogToUI($"Great job! {uncollectedCount} treasures remaining.");
        }
    }

    private void LoadScoreboard()
    {
        CancelInvoke(nameof(UpdateFinderState));
        StopListeningForTreasures();

        if (suppressResultSave)
        {
            Debug.Log("[TreasureManager] Result save suppressed. Exiting without Firebase updates.");
            UnityEngine.SceneManagement.SceneManager.LoadScene("LevelBrowserScene");
            return;
        }

        if (string.IsNullOrEmpty(authManager?.UserId) || string.IsNullOrEmpty(currentRoomId) || dbRef == null)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("ScoreboardScene");
            return;
        }

        bool isSingle = currentRoomId.StartsWith("-");
        string root = isSingle ? "levels" : "rooms";
        string uid = authManager.UserId;

        long takenMs = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - runStartLocalMs);

        CalculateFinalScore(currentRoomId, uid, isSingle, root, takenMs);
    }

    private void CalculateFinalScore(string roomId, string uid, bool isSingle, string root, long takenMs)
    {
        if (isSingle)
        {
            // Single player: sum points from collectedTreasures/{uid}
            dbRef.Child(root).Child(roomId).Child("collectedTreasures").Child(uid)
                .GetValueAsync()
                .ContinueWithOnMainThread(task =>
                {
                    int totalScore = 0;

                    if (!task.IsFaulted && task.Result.Exists)
                    {
                        foreach (var treasureSnap in task.Result.Children)
                        {
                            if (treasureSnap.HasChild("points") &&
                                int.TryParse(treasureSnap.Child("points").Value?.ToString(), out int points))
                            {
                                totalScore += points;
                                Debug.Log($"[CalcScore] {treasureSnap.Key}: +{points} (total: {totalScore})");
                            }
                        }
                    }

                    Debug.Log($"[CalcScore] Final score calculated: {totalScore}");

                    // ← SAVE THE CALCULATED SCORE
                    SaveRunEnd(roomId, uid, totalScore, takenMs);

                    // ← Destroy and reload
                    if (ScoreManager.Instance != null)
                    {
                        Debug.Log("[TreasureManager] Destroying old ScoreManager for fresh load");
                        Destroy(ScoreManager.Instance.gameObject);
                    }

                    Debug.Log("[TreasureManager] Reloading ScoreboardScene with score: " + totalScore);
                    UnityEngine.SceneManagement.SceneManager.LoadScene("ScoreboardScene");
                });
        }
        else
        {
            // Multiplayer: sum points from gameState where collectedBy[uid] = true
            dbRef.Child(root).Child(roomId).Child("gameState")
                .GetValueAsync()
                .ContinueWithOnMainThread(task =>
                {
                    int totalScore = 0;

                    if (!task.IsFaulted && task.Result.Exists)
                    {
                        foreach (var treasureSnap in task.Result.Children)
                        {
                            if (treasureSnap.HasChild("collectedBy"))
                            {
                                var collectedByData = treasureSnap.Child("collectedBy");
                                if (collectedByData.HasChild(uid))
                                {
                                    int points = 0;
                                    if (treasureSnap.HasChild("points"))
                                        int.TryParse(treasureSnap.Child("points").Value?.ToString(), out points);

                                    int bonus = 0;
                                    if (treasureSnap.HasChild("bonusPoints"))
                                        int.TryParse(treasureSnap.Child("bonusPoints").Value?.ToString(), out bonus);

                                    int total = points + bonus;
                                    totalScore += total;
                                    Debug.Log($"[CalcScore] {treasureSnap.Key}: +{total} (points:{points}, bonus:{bonus})");
                                }
                            }
                        }
                    }

                    Debug.Log($"[CalcScore] Final score calculated: {totalScore}");

                    // ← SAVE THE CALCULATED SCORE
                    SaveRunEnd(roomId, uid, totalScore, takenMs);

                    // ← Destroy and reload
                    if (ScoreManager.Instance != null)
                    {
                        Debug.Log("[TreasureManager] Destroying old ScoreManager for fresh load");
                        Destroy(ScoreManager.Instance.gameObject);
                    }

                    Debug.Log("[TreasureManager] Reloading ScoreboardScene with score: " + totalScore);
                    UnityEngine.SceneManagement.SceneManager.LoadScene("ScoreboardScene");
                });
        }
    }

    // --- FINDER LOGIC ---

    private void UpdateFinderState()
    {
        if (!servicesReady) return;

        double myLat = locationManager.Latitude;
        double myLon = locationManager.Longitude;

        string targetKey = null;

        // If InOrder mode (1), target EXACTLY the next sequence item
        if (roomCollectionMode == 1) // 1 = InOrder
        {
            foreach (var treasure in localTreasures.Values)
            {
                bool alreadyCollected = treasure.data.collectedBy != null && treasure.data.collectedBy.Count > 0;
                if (!alreadyCollected && treasure.data.orderIndex == nextTreasureIndex)
                {
                    targetKey = treasure.key;
                    Debug.Log($"[UpdateFinderState] IN-ORDER MODE: Targeting checkpoint #{nextTreasureIndex} ('{treasure.data.name}')");
                    break; // Found the exact next one in sequence
                }
            }

            if (string.IsNullOrEmpty(targetKey))
            {
                Debug.Log($"[UpdateFinderState] IN-ORDER MODE: Could not find uncollected checkpoint with orderIndex #{nextTreasureIndex}");
            }
        }
        // Otherwise (Free Order), target the CLOSEST uncollected treasure
        else
        {
            float minDistance = float.MaxValue;
            string closestName = "";

            foreach (var treasure in localTreasures.Values)
            {
                bool alreadyCollected = treasure.data.collectedBy != null && treasure.data.collectedBy.Count > 0;
                if (alreadyCollected) continue;

                float distance = (float)GetDistanceInMeters(treasure.data.lat, treasure.data.lon, myLat, myLon);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    targetKey = treasure.key;
                    closestName = treasure.data.name;
                }
            }

            if (!string.IsNullOrEmpty(targetKey))
            {
                Debug.Log($"[UpdateFinderState] FREE-ORDER MODE: Targeting closest checkpoint '{closestName}' at {minDistance:F1}m away.");
            }
        }

        currentTargetKey = targetKey;
    }

    private bool CanScanForTreasure()
    {
        if (string.IsNullOrEmpty(currentTargetKey)
            || !localTreasures.ContainsKey(currentTargetKey)
            || locationManager == null)
            return false;

        Treasure target = localTreasures[currentTargetKey];
        if (target.instance != null)
            return false; // Already spawned

        // Calculate the exact distance in meters using your Haversine formula
        double distance = LocationManager.Haversine(
            target.data.lat, target.data.lon,
            locationManager.Latitude, locationManager.Longitude);

        // --- DEBUG LOGGING ---
        // This will print exactly what the game sees in the Unity Console or Android Logcat
        Debug.Log($"[SPAWN CHECK] Treasure: {target.data.name} | Distance: {distance:F2}m | Spawn Range Allowed: {spawnRange}m");

        // --- STRICT SAFETY CLAMP ---
        // If the Inspector accidentally set spawnRange to 500, this forces it back to a safe maximum (e.g. 15 meters)
        float actualSpawnRange = Mathf.Min(spawnRange, 15f);

        return distance <= actualSpawnRange;
    }

    private void TrySpawnTreasure()
    {
        if (string.IsNullOrEmpty(currentTargetKey) || !localTreasures.ContainsKey(currentTargetKey))
            return;

        Treasure target = localTreasures[currentTargetKey];

        if (target.instance != null || arRaycastManager == null || treasurePrefab == null || Camera.main == null)
            return;

        var hits = new List<ARRaycastHit>();
        var screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

        if (!arRaycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated))
            return;

        Pose hitPose = hits[0].pose;

        // Calculate direction to camera
        Vector3 directionToCamera = Camera.main.transform.position - hitPose.position;
        directionToCamera.y = 0;

        Quaternion facingRotation = directionToCamera.magnitude > 0.01f
            ? Quaternion.LookRotation(directionToCamera)
            : hitPose.rotation;

        Quaternion rotatedRotation = facingRotation * Quaternion.Euler(-90, 0, 0);

        target.instance = Instantiate(treasurePrefab, hitPose.position, rotatedRotation);

        var tapHandler = target.instance.GetComponent<TreasureARTapHandler>();
        if (tapHandler == null)
            target.instance.AddComponent<TreasureARTapHandler>();

        Debug.Log($"[TreasureManager] Spawned treasure '{target.data.name}' at: {hitPose.position}");
    }

    // --- MODE SWITCHING & UI ---

    /// <summary>
    /// Called by Exit button. Opens confirmation UI.
    /// </summary>
    public void RequestQuitLevelWithoutSaving()
    {
        if (isExitingLevel) return;

        if (quitConfirmPanel != null)
        {
            if (quitConfirmText != null)
                quitConfirmText.text = "Quit level now? Your progress and score will NOT be saved.";
            quitConfirmPanel.SetActive(true);
        }
        else
        {
            // Fallback if panel missing
            ConfirmQuitLevelWithoutSaving();
        }
    }

    private void CancelQuitLevelWithoutSaving()
    {
        if (quitConfirmPanel != null)
            quitConfirmPanel.SetActive(false);
    }

    private void ConfirmQuitLevelWithoutSaving()
    {
        if (isExitingLevel) return;
        isExitingLevel = true;
        suppressResultSave = true;

        if (quitConfirmPanel != null)
            quitConfirmPanel.SetActive(false);

        Debug.Log("[TreasureManager] Confirmed quit without saving.");

        CancelInvoke(nameof(UpdateFinderState));
        StopListeningForTreasures();
        StopAllCoroutines();

        if (challengeRunner != null)
            challengeRunner.gameObject.SetActive(false);

        UnityEngine.SceneManagement.SceneManager.LoadScene("MenuScene");
    }

    private void ToggleMode()
    {
        mode = (mode == PlayerMode.Setter) ? PlayerMode.Finder : PlayerMode.Setter;

        CancelInvoke(nameof(UpdateFinderState));
        StopListeningForTreasures();

        foreach (var treasure in localTreasures.Values)
        {
            if (treasure.instance != null) Destroy(treasure.instance);
        }

        localTreasures.Clear();
        currentTargetKey = null;

        SetupForCurrentMode();
        SetupUI();
    }

    private void SetupForCurrentMode()
    {
        if (!servicesReady) return;

        if (mode == PlayerMode.Finder)
        {
            LogToUI("Finder mode: listening for treasures...");
            StartListeningForTreasures();
            InvokeRepeating(nameof(UpdateFinderState), 1.0f, updateInterval);
        }
        else
        {
            LogToUI("Setter mode: tap to add treasures.");
            CancelInvoke(nameof(UpdateFinderState));
            StopListeningForTreasures();
        }
    }

    private void SetupUI()
    {
        // Guard all UI refs to avoid NullReferenceException
        if (setTreasureButton != null)
            setTreasureButton.gameObject.SetActive(mode == PlayerMode.Setter);

        if (modeLabel != null)
            modeLabel.text = $"Mode: {mode}";

        bool finderUIActive = mode == PlayerMode.Finder;

        if (distanceLabel != null)
            distanceLabel.gameObject.SetActive(finderUIActive);

        if (arrowIndicator != null)
            arrowIndicator.gameObject.SetActive(finderUIActive);

        if (collectButton != null)
        {
            collectButton.gameObject.SetActive(false);
            collectButton.interactable = false;
        }
    }

    private void UpdateUIElements()
    {
        if (mode != PlayerMode.Finder) return;
        if (distanceLabel == null || arrowIndicator == null || statusText == null) return;

        if (string.IsNullOrEmpty(currentTargetKey) || !localTreasures.ContainsKey(currentTargetKey))
        {
            distanceLabel.text = "Searching...";
            arrowIndicator.gameObject.SetActive(false);
            statusText.text = localTreasures.Count == 0 ? "No active treasures found." : "Finding treasure...";
            return;
        }

        Treasure target = localTreasures[currentTargetKey];
        Vector3 targetGpsPos = GPSToUnityPosition(target.data.lat, target.data.lon);

        float distanceToShow;
        Vector3 direction;

        if (target.instance != null)
        {
            if (Camera.main == null) return;

            distanceToShow = Vector3.Distance(Camera.main.transform.position, target.instance.transform.position);
            direction = target.instance.transform.position - Camera.main.transform.position;

            // ← Show prompt when close to treasure
            bool isNearby = distanceToShow <= collectDistance;

            // Find and update the tap prompt text
            var tapHandler = target.instance.GetComponent<TreasureARTapHandler>();
            if (tapHandler != null)
            {
                var promptText = target.instance.GetComponentInChildren<TextMeshProUGUI>();
                if (promptText != null)
                {
                    promptText.gameObject.SetActive(isNearby);

                    if (isNearby)
                    {
                        // Optional: pulsing effect
                        float pulse = Mathf.Sin(Time.time * 3f) * 0.5f + 0.5f;
                        promptText.alpha = 0.7f + (pulse * 0.3f);
                    }
                }
            }
        }
        else
        {
            distanceToShow = targetGpsPos.magnitude;
            direction = targetGpsPos;
        }

        distanceLabel.text = $"{distanceToShow:F1} m";
        arrowIndicator.gameObject.SetActive(true);

        direction.y = 0;
        if (Camera.main == null) return;

        float angle = Vector3.SignedAngle(Camera.main.transform.forward, direction, Vector3.up);
        arrowIndicator.localEulerAngles = new Vector3(0, 0, -angle);
    }

    private Vector3 GPSToUnityPosition(double targetLat, double targetLon)
    {
        double myLat = locationManager.Latitude;
        double myLon = locationManager.Longitude;
        const double R = 6371000.0;

        double dLat = (targetLat - myLat) * (Math.PI / 180.0);
        double dLon = (targetLon - myLon) * (Math.PI / 180.0);

        double x = dLon * R * Math.Cos(myLat * (Math.PI / 180.0));
        double z = dLat * R;

        return new Vector3((float)x, 0, (float)z);
    }

    private double GetDistanceInMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0; // Earth's radius in METERS
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private void LogToUI(string msg)
    {
        if (statusText) statusText.text = msg;
        Debug.Log(msg);
    }

    private IEnumerator WaitForServices()
    {
        // Wait for essential services like GPS
        while (locationManager.Status != LocationManager.LocationStatus.Ready)
        {
            LogToUI($"Waiting for GPS: {locationManager.Status}");
            yield return null;
        }

        LogToUI("Services ready. Initializing Firebase...");

        try
        {
            dbRef = FirebaseDatabase.GetInstance(firebaseDatabaseUrl).RootReference;
        }
        catch (Exception ex)
        {
            LogToUI("ERROR: Failed to init Firebase DB: " + ex.Message);
            enabled = false;
            yield break;
        }

        servicesReady = true;

        StartListeningForTreasures();
        InvokeRepeating(nameof(UpdateFinderState), 1.0f, updateInterval);
    }

    private void HandleTimeUp()
    {
        LogToUI("Time's up!");
        Debug.Log("[TreasureManager] Time's up!");
        // For multiplayer, we might want to end the game immediately or show a summary screen
        isExitingLevel = true;
        if (collectButton != null) collectButton.interactable = false;
        // Optionally, show final scores or navigate to a results screen here
        LoadScoreboard();
    }
}