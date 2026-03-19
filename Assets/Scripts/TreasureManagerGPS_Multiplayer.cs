using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

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

        // Note: JsonUtility doesn't serialize Dictionary well, but Firebase will create this map once a player collects.
        public Dictionary<string, bool> collectedBy = new Dictionary<string, bool>();
        public ChallengeData challenge; // Optional: add challenge data here for future extension
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

    [Header("AR & Game Settings")]
    [SerializeField] private ARRaycastManager arRaycastManager;
    [SerializeField] private GameObject treasurePrefab;
    [SerializeField] private float spawnRange = 500f;     // meters: must be within this many meters (GPS) to start scanning/spawning
    [SerializeField] private float revealRadius = 50.0f;  // Unity meters: AR hit point must be close enough to GPS target spot
    [SerializeField] private float collectDistance = 10f; // Unity meters: show collect button when close enough to spawned instance
    [SerializeField] private float updateInterval = 2.0f;

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

    // --- Service References ---
    private DatabaseReference dbRef;
    private AuthManager authManager;
    private LocationManager locationManager;

    // --- State ---
    private bool servicesReady = false;
    private bool initialized = false;
    private bool isCollectInProgress = false;

    private readonly Dictionary<string, Treasure> localTreasures = new Dictionary<string, Treasure>();
    private string currentTargetKey;
    private string currentRoomId;

    private void Start()
    {
        // Multiplayer flow: ARSceneController calls InitializeForRoom(roomId).
        Debug.Log("[TreasureManagerGPS_Multiplayer] Start() - waiting for InitializeForRoom...");
    }

    private void OnDestroy()
    {
        StopListeningForTreasures();
        CancelInvoke(nameof(UpdateFinderState));
    }

    private void Update()
    {
        if (!servicesReady) return;

        if (!string.IsNullOrEmpty(currentTargetKey))
        {
            if (CanScanForTreasure()
                && localTreasures.ContainsKey(currentTargetKey)
                && localTreasures[currentTargetKey].instance == null)
            {
                TrySpawnTreasure();
            }
        }

        UpdateUIElements();
    }

    /// <summary>
    /// Called by ARSceneController once the AR scene is loaded and the roomId is known.
    /// </summary>
    public void InitializeForRoom(string roomId)
    {
        if (initialized)
        {
            Debug.LogWarning("[TreasureManagerGPS_Multiplayer] InitializeForRoom called more than once. Ignoring.");
            return;
        }

        if (string.IsNullOrWhiteSpace(roomId))
        {
            Debug.LogError("[TreasureManagerGPS_Multiplayer] InitializeForRoom called with null/empty roomId.");
            enabled = false;
            return;
        }

        initialized = true;
        currentRoomId = roomId;

        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.StartGameTimer();
        }

        authManager = AuthManager.Instance;
        locationManager = LocationManager.Instance;

        if (authManager == null || locationManager == null)
        {
            LogToUI("ERROR: AuthManager or LocationManager not found!");
            enabled = false;
            return;
        }

        if (arRaycastManager == null) arRaycastManager = FindObjectOfType<ARRaycastManager>();

        WireButtons();
        SetupUI();

        LogToUI($"Joining hunt in room: {roomId}");
        StartCoroutine(WaitForServices());
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
            points = 100,
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

        DatabaseReference gameStateRef = dbRef.Child("rooms").Child(currentRoomId).Child("gameState");
        gameStateRef.ChildAdded += HandleTreasureAdded;
        gameStateRef.ChildChanged += HandleTreasureChanged;

        Debug.Log($"[TreasureManager] Listening: rooms/{currentRoomId}/gameState");
    }

    private void StopListeningForTreasures()
    {
        if (dbRef == null || string.IsNullOrEmpty(currentRoomId)) return;

        DatabaseReference gameStateRef = dbRef.Child("rooms").Child(currentRoomId).Child("gameState");
        gameStateRef.ChildAdded -= HandleTreasureAdded;
        gameStateRef.ChildChanged -= HandleTreasureChanged;
    }

    private void HandleTreasureAdded(object sender, ChildChangedEventArgs args)
    {
        if (args.DatabaseError != null) return;

        string key = args.Snapshot.Key;
        if (localTreasures.ContainsKey(key)) return;

        TreasureData data = JsonUtility.FromJson<TreasureData>(args.Snapshot.GetRawJsonValue());

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

        TreasureData newData = JsonUtility.FromJson<TreasureData>(args.Snapshot.GetRawJsonValue());
        localTreasures[key].data = newData;

        // Check if treasure has been marked as collected (disappears for everyone)
        bool isMarkedCollected = false;

        // Try to get isCollected flag from the snapshot
        var snapshot = args.Snapshot;
        if (snapshot.HasChild("isCollected"))
        {
            object isCollectedObj = snapshot.Child("isCollected").Value;
            if (isCollectedObj is bool boolVal)
                isMarkedCollected = boolVal;
        }

        // If treasure is marked as collected, disappear for everyone
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

    private void CollectTargetTreasure()
    {
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

        // ── NEW: Check if this checkpoint has a challenge ─────────────────────
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

        DatabaseReference treasureRef = dbRef
            .Child("rooms").Child(currentRoomId)
            .Child("gameState").Child(target.key);

        bool abortedAlreadyCollected = false;
        bool abortedMissingNode = false;

        treasureRef.RunTransaction(mutableData =>
        {
            if (mutableData.Value == null)
            {
                abortedMissingNode = true;
                return TransactionResult.Abort();
            }

            if (mutableData.Value is not Dictionary<string, object> data)
                return TransactionResult.Abort();

            Dictionary<string, object> collectedBy;

            if (data.TryGetValue("collectedBy", out object collectedByObj)
                && collectedByObj is Dictionary<string, object> existing)
                collectedBy = existing;
            else
                collectedBy = new Dictionary<string, object>();

            // If THIS USER already collected it, abort
            if (collectedBy.ContainsKey(myUserId))
            {
                abortedAlreadyCollected = true;
                return TransactionResult.Abort();
            }

            // Add this user to the collectors
            collectedBy[myUserId] = true;

            // Mark treasure as collected
            data["collectedBy"] = collectedBy;
            data["isCollected"] = true;

            if (bonusPoints > 0)
                data["bonusPoints"] = bonusPoints;

            mutableData.Value = data;
            return TransactionResult.Success(mutableData);

        }).ContinueWithOnMainThread(task =>
        {
            isCollectInProgress = false;
            if (collectButton != null) collectButton.interactable = true;

            if (task.IsFaulted)
            {
                if (abortedAlreadyCollected)
                {
                    LogToUI("You already collected this treasure!");
                    if (collectButton != null)
                    {
                        collectButton.gameObject.SetActive(false);
                        collectButton.interactable = false;
                    }
                    return;
                }

                if (abortedMissingNode)
                {
                    LogToUI("Treasure no longer exists.");
                    if (collectButton != null)
                    {
                        collectButton.gameObject.SetActive(false);
                        collectButton.interactable = false;
                    }
                    return;
                }

                Debug.LogError("Error collecting treasure: " + task.Exception);
                LogToUI("Collect failed.");
                return;
            }

            // ← SUCCESS: Treasure collected!
            // Calculate total points earned (base points + bonus from challenge)
            long totalEarned = target.data.points + bonusPoints;

            // Update ScoreManager UI
            if (ScoreManager.Instance != null)
            {
                
                int currentScore = ScoreManager.Instance.GetScore();

                // Save to Firebase
                string userId = authManager.UserId;
                dbRef.Child("rooms").Child(currentRoomId)
                     .Child("players").Child(userId)
                     .Child("currentScore")
                     .SetValueAsync(currentScore)
                     .ContinueWithOnMainThread(scoreTask =>
                     {
                         if (scoreTask.IsCompleted)
                         {
                             Debug.Log($"[TreasureManager] Score saved to Firebase: {currentScore}");
                         }
                     });
            }

            // Also save to Firebase for persistence
            dbRef.Child("rooms").Child(currentRoomId)
                 .Child("scores").Child(myUserId)
                 .RunTransaction(scoreData =>
                 {
                     long current = scoreData.Value == null ? 0 : (long)scoreData.Value;
                     scoreData.Value = current + totalEarned;
                     return TransactionResult.Success(scoreData);
                 }).ContinueWithOnMainThread(scoreTask =>
                 {
                     if (scoreTask.IsFaulted)
                     {
                         Debug.LogError("Failed to update score in Firebase: " + scoreTask.Exception);
                     }
                     else
                     {
                         Debug.Log($"[TreasureManager] Score updated: +{totalEarned} points. Total in Firebase.");
                     }
                 });

            Debug.Log($"[TreasureManager] Treasure '{target.data.name}' collected by {myUserId}");
        });
    }

    // --- FINDER LOGIC ---

    private void UpdateFinderState()
    {
        if (!servicesReady) return;

        double myLat = locationManager.Latitude;
        double myLon = locationManager.Longitude;

        string nearestKey = null;
        float minDistance = float.MaxValue;

        foreach (var treasure in localTreasures.Values)
        {
            bool alreadyCollected = treasure.data.collectedBy != null && treasure.data.collectedBy.Count > 0;
            if (alreadyCollected) continue;

            float distance = (float)LocationManager.Haversine(treasure.data.lat, treasure.data.lon, myLat, myLon);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearestKey = treasure.key;
            }
        }

        currentTargetKey = nearestKey;
    }

    private bool CanScanForTreasure()
    {
        if (string.IsNullOrEmpty(currentTargetKey) || !localTreasures.ContainsKey(currentTargetKey)) return false;

        Treasure target = localTreasures[currentTargetKey];
        if (target.instance != null) return false;

        double distance = LocationManager.Haversine(
            target.data.lat, target.data.lon,
            locationManager.Latitude, locationManager.Longitude);

        return distance <= spawnRange;
    }

    private void TrySpawnTreasure()
    {
        if (string.IsNullOrEmpty(currentTargetKey) || !localTreasures.ContainsKey(currentTargetKey)) return;

        Treasure target = localTreasures[currentTargetKey];
        if (target.instance != null) return;

        if (arRaycastManager == null) return;
        if (treasurePrefab == null)
        {
            Debug.LogError("[TreasureManagerGPS_Multiplayer] treasurePrefab not assigned.");
            return;
        }

        var hits = new List<ARRaycastHit>();
        var screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

        if (arRaycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated))
        {
            Pose hitPose = hits[0].pose;
            target.instance = Instantiate(treasurePrefab, hitPose.position, hitPose.rotation);
        }
    }

    // --- MODE SWITCHING & UI ---

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
        if (distanceLabel == null || arrowIndicator == null || collectButton == null || statusText == null) return;

        if (string.IsNullOrEmpty(currentTargetKey) || !localTreasures.ContainsKey(currentTargetKey))
        {
            distanceLabel.text = "Searching...";
            arrowIndicator.gameObject.SetActive(false);
            collectButton.gameObject.SetActive(false);

            statusText.text = localTreasures.Count == 0 ? "No active treasures found." : "Finding closest treasure...";
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

            bool inCollectRange = distanceToShow <= collectDistance;
            collectButton.gameObject.SetActive(inCollectRange);
            collectButton.interactable = inCollectRange && !isCollectInProgress;
        }
        else
        {
            distanceToShow = targetGpsPos.magnitude;
            direction = targetGpsPos;

            collectButton.gameObject.SetActive(false);
            collectButton.interactable = false;
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
}