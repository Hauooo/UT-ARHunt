using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase;
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
        public Dictionary<string, bool> collectedBy = new Dictionary<string, bool>(); // Track who collected it
    }

    // A local class to hold all information about a treasure, including its key and GameObject instance.
    public class Treasure
    {
        public string key;
        public TreasureData data;
        public GameObject instance;

    }

    public void InitializeForRoom(string roomId)
    {
        currentRoomId = roomId;
        LogToUI($"Joining hunt in room: {roomId}");

        // Get references to our singleton managers
        authManager = AuthManager.Instance;
        locationManager = LocationManager.Instance;

        if (arRaycastManager == null) arRaycastManager = FindObjectOfType<ARRaycastManager>();
        collectButton.onClick.AddListener(CollectTargetTreasure);

        StartCoroutine(WaitForServices());
    }

    public enum PlayerMode { Setter, Finder }

    [Header("Game Mode")]
    public PlayerMode mode = PlayerMode.Finder;

    [Header("AR & Game Settings")]
    [SerializeField] private ARRaycastManager arRaycastManager;
    [SerializeField] private GameObject treasurePrefab;
    [SerializeField] private float spawnRange = 25f; // Player must be within this many meters to start scanning.
    [SerializeField] private float revealRadius = 3.0f; // AR plane must be within this many meters of the target spot.
    [SerializeField] private float collectDistance = 5f;
    [SerializeField] private float updateInterval = 2.0f;


    [Header("UI")]
    [SerializeField] private Button setTreasureButton;
    [SerializeField] private Button collectButton;
    [SerializeField] private Button modeToggleButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text modeLabel;
    [SerializeField] private TMP_Text distanceLabel;
    [SerializeField] private RectTransform arrowIndicator;

    // --- Serive References (No longer manages these itself) ---

    private DatabaseReference dbRef;
    private AuthManager authManager;
    private LocationManager locationManager;

    // --- State ---
    private bool servicesReady = false;
    private Dictionary<string, Treasure> localTreasures = new Dictionary<string, Treasure>();
    private string currentTargetKey; // The key of the treasure we are currently hunting.
    private string currentRoomId; // For future multiplayer room management.

    void Start()
    {
        // Get references to our singleton managers
        authManager = AuthManager.Instance;
        locationManager = LocationManager.Instance;

        if (authManager == null || locationManager == null)
        {
            LogToUI("ERROR: AuthManager or LocationManager not found in scene!");
            enabled = false;
            return;
        }

        if (arRaycastManager == null) arRaycastManager = FindObjectOfType<ARRaycastManager>();

        setTreasureButton.onClick.AddListener(SetTreasureHere);
        collectButton.onClick.AddListener(CollectTargetTreasure);
        if (modeToggleButton) modeToggleButton.onClick.AddListener(ToggleMode);

        SetupUI();
        LogToUI("Initializing...");

        // Start the initialization process. This is now much cleaner.
        StartCoroutine(WaitForServices());
    }

    void OnDestroy()
    {
        StopListeningForTreasures();
        CancelInvoke(nameof(UpdateFinderState));
    }

    void Update()
    {
        if (!servicesReady) return;

        if (!string.IsNullOrEmpty(currentTargetKey))
        {
            if (CanScanForTreasure() && localTreasures.ContainsKey(currentTargetKey) && localTreasures[currentTargetKey].instance == null)
            {
                TrySpawnTreasure();
            }
        }
        UpdateUIElements();
    }



    // --- MULTIPLAYER TREASURE SYSTEM ---

    private void SetTreasureHere()
    {
        if (!servicesReady) { LogToUI("Services not ready."); return; }

        string newId = dbRef.Child("treasures").Push().Key;
        TreasureData treasure = new TreasureData
        {
            lat = locationManager.Latitude,
            lon = locationManager.Longitude,
            collectedBy = new Dictionary<string, bool>() // Initialize with an empty dictionary
        };

        dbRef.Child("treasures").Child(newId).SetRawJsonValueAsync(JsonUtility.ToJson(treasure))
            .ContinueWithOnMainThread(t =>
            {
                if (t.IsFaulted) LogToUI("Error adding treasure: " + t.Exception);
                else LogToUI("Treasure placed successfully!");
            });
    }

    // --- MERGED & REFACTORED: Using efficient child listeners ---
    private void StartListeningForTreasures()
    {
        if (dbRef == null || string.IsNullOrEmpty(currentRoomId)) return;

        // FIXED: The path MUST point to the 'gameState' inside the current room.
        DatabaseReference gameStateRef = dbRef.Child("rooms").Child(currentRoomId).Child("gameState");

        gameStateRef.ChildAdded += HandleTreasureAdded;
        gameStateRef.ChildChanged += HandleTreasureChanged;
    }

    private void StopListeningForTreasures()
    {
        if (dbRef == null || string.IsNullOrEmpty(currentRoomId)) return;

        // FIXED: Point to the correct path to unsubscribe.
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

        if (data.collectedBy == null || !data.collectedBy.ContainsKey(authManager.UserId))
        {
            localTreasures[key] = new Treasure { key = key, data = data };
            LogToUI($"New treasure '{data.name}' appeared!");
        }
    }

    private void HandleTreasureChanged(object sender, ChildChangedEventArgs args)
    {
        if (args.DatabaseError != null) return;
        string key = args.Snapshot.Key;
        if (!localTreasures.ContainsKey(key)) return;

        TreasureData newData = JsonUtility.FromJson<TreasureData>(args.Snapshot.GetRawJsonValue());
        localTreasures[key].data = newData;

        if (newData.collectedBy != null && newData.collectedBy.ContainsKey(authManager.UserId))
        {
            LogToUI($"You have collected '{newData.name}'.");
            if (localTreasures[key].instance != null)
            {
                Destroy(localTreasures[key].instance);
            }
            if (currentTargetKey == key)
            {
                currentTargetKey = null;
            }
        }
    }

    private void CollectTargetTreasure()
    {
        if (!servicesReady || string.IsNullOrEmpty(currentTargetKey) || !localTreasures.ContainsKey(currentTargetKey)) return;

        string myUserId = authManager.UserId;
        Treasure target = localTreasures[currentTargetKey];

        DatabaseReference treasureRef = dbRef.Child("rooms").Child(currentRoomId).Child("gameState").Child(target.key);

        // --- STEP 1: Transaction to claim the treasure ---
        // RunTransaction returns a Task<DataSnapshot>
        treasureRef.RunTransaction(mutableData =>
        {
            if (mutableData.Value == null) return TransactionResult.Abort();

            var data = (Dictionary<string, object>)mutableData.Value;
            var collectedByDict = data.ContainsKey("collectedBy") ? (Dictionary<string, object>)data["collectedBy"] : new Dictionary<string, object>();

            if (collectedByDict.ContainsKey(myUserId)) return TransactionResult.Abort();

            collectedByDict[myUserId] = true;
            data["collectedBy"] = collectedByDict;

            mutableData.Value = data;
            return TransactionResult.Success(mutableData);

        }).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                LogToUI("Error collecting treasure: " + task.Exception);
                return;
            }

            // --- FIXED: This is the correct way to check for a successful transaction ---
            // 1. Get the final data state from the completed task's result.
            DataSnapshot snapshot = task.Result;

            // 2. Deserialize it into our data class.
            TreasureData finalData = JsonUtility.FromJson<TreasureData>(snapshot.GetRawJsonValue());

            // 3. The transaction is "committed" if our change is present in the final data.
            if (finalData != null && finalData.collectedBy != null && finalData.collectedBy.ContainsKey(myUserId))
            {
                LogToUI("Treasure collected! Updating score...");

                // --- STEP 2: Transaction to safely update the score ---
                DatabaseReference playerScoreRef = dbRef.Child("rooms").Child(currentRoomId).Child("players").Child(myUserId).Child("score");

                playerScoreRef.RunTransaction(scoreData =>
                {
                    long currentScore = 0;
                    if (scoreData.Value != null)
                    {
                        currentScore = (long)scoreData.Value;
                    }
                    long pointsToAdd = target.data.points;
                    scoreData.Value = currentScore + pointsToAdd;

                    return TransactionResult.Success(scoreData);

                }).ContinueWithOnMainThread(scoreTask =>
                {
                    if (scoreTask.IsFaulted)
                    {
                        LogToUI("Score update failed!");
                    }
                    else
                    {
                        LogToUI("Score updated successfully!");
                    }
                });
            }
            else
            {
                LogToUI("Could not collect treasure (likely already collected by you).");
            }
        });
    }

    // --- FINDER LOGIC ---

    // MERGED & REFACTORED: This runs periodically, not every frame.
    private void UpdateFinderState()
    {
        // This logic remains the same, but now it only operates on treasures in the current room.
        if (!servicesReady) return;

        double myLat = locationManager.Latitude;
        double myLon = locationManager.Longitude;
        string nearestKey = null;
        float minDistance = float.MaxValue;

        foreach (var treasure in localTreasures.Values)
        {
            if (treasure.data.collectedBy != null && treasure.data.collectedBy.ContainsKey(authManager.UserId))
            {
                continue;
            }

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

        // FIXED: Use the correct Haversine calculation for distance.
        double distance = LocationManager.Haversine(target.data.lat, target.data.lon, locationManager.Latitude, locationManager.Longitude);
        return distance <= spawnRange;
    }

    private void TrySpawnTreasure()
    {
        if (string.IsNullOrEmpty(currentTargetKey) || !localTreasures.ContainsKey(currentTargetKey)) return;
        Treasure target = localTreasures[currentTargetKey];
        if (target.instance != null) return;

        var hits = new List<ARRaycastHit>();
        var screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

        if (arRaycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;
            // FIXED: Call the correct GPSToUnityPosition method.
            Vector3 treasureGpsPos = GPSToUnityPosition(target.data.lat, target.data.lon);
            float distanceToTargetSpot = Vector3.Distance(new Vector3(hitPose.position.x, 0, hitPose.position.z), new Vector3(treasureGpsPos.x, 0, treasureGpsPos.z));

            if (distanceToTargetSpot <= revealRadius)
            {
                target.instance = Instantiate(treasurePrefab, hitPose.position, hitPose.rotation);
            }
        }
    }

    // --- MODE SWITCHING & UI ---

    private void ToggleMode()
    {
        mode = (mode == PlayerMode.Setter) ? PlayerMode.Finder : PlayerMode.Setter;

        // Clean up state
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
        setTreasureButton.gameObject.SetActive(mode == PlayerMode.Setter);
        modeLabel.text = $"Mode: {mode}";
        bool finderUIActive = mode == PlayerMode.Finder;
        distanceLabel.gameObject.SetActive(finderUIActive);
        arrowIndicator.gameObject.SetActive(finderUIActive);
        collectButton.gameObject.SetActive(false); // Default to off
    }

    private void UpdateUIElements()
    {
        if (mode != PlayerMode.Finder) return;

        if (string.IsNullOrEmpty(currentTargetKey) || !localTreasures.ContainsKey(currentTargetKey))
        {
            distanceLabel.text = "Searching...";
            arrowIndicator.gameObject.SetActive(false);
            collectButton.gameObject.SetActive(false);

            // Check if any huntable treasures exist at all
            int huntableCount = 0;
            foreach (var t in localTreasures.Values)
            {
                if (t.data.collectedBy == null || !t.data.collectedBy.ContainsKey(authManager.UserId))
                {
                    huntableCount++;
                }
            }
            statusText.text = huntableCount == 0 ? "No active treasures found." : "Finding closest treasure...";

            return;
        }

        Treasure target = localTreasures[currentTargetKey];
        // FIXED: Call the correct GPSToUnityPosition method.
        Vector3 targetGpsPos = GPSToUnityPosition(target.data.lat, target.data.lon);
        float distanceToShow;
        Vector3 direction;

        if (target.instance != null)
        {
            distanceToShow = Vector3.Distance(Camera.main.transform.position, target.instance.transform.position);
            direction = target.instance.transform.position - Camera.main.transform.position;
            collectButton.gameObject.SetActive(distanceToShow <= collectDistance);
        }
        else
        {
            distanceToShow = targetGpsPos.magnitude;
            direction = targetGpsPos;
            collectButton.gameObject.SetActive(false);
        }

        distanceLabel.text = $"{distanceToShow:F1} m";
        arrowIndicator.gameObject.SetActive(true);
        direction.y = 0;
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
        dbRef = FirebaseDatabase.DefaultInstance.RootReference;
        servicesReady = true;

        // Now that everything is ready, start the game logic
        StartListeningForTreasures();
        InvokeRepeating(nameof(UpdateFinderState), 1.0f, updateInterval);
    }
}