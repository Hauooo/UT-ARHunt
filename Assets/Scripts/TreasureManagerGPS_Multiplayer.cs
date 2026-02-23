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
        public Dictionary<string, bool> collectedBy = new Dictionary<string, bool>();
    }

    // A local class to hold all information about a treasure, including its key and GameObject instance.
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
    [SerializeField] private float spawnRange = 100f;
    [SerializeField] private float revealRadius = 50.0f;
    [SerializeField] private float collectDistance = 5f;
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

    // --- Service References ---
    private DatabaseReference dbRef;
    private AuthManager authManager;
    private LocationManager locationManager;

    // --- State ---
    private bool servicesReady = false;
    private bool initialized = false;

    private Dictionary<string, Treasure> localTreasures = new Dictionary<string, Treasure>();
    private string currentTargetKey;
    private string currentRoomId;

    private Coroutine initCoroutine;

    private void Start()
    {
        // IMPORTANT: Do not auto-start initialization here.
        // In multiplayer, ARSceneController should call InitializeForRoom(roomId).
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
            if (CanScanForTreasure() &&
                localTreasures.ContainsKey(currentTargetKey) &&
                localTreasures[currentTargetKey].instance == null)
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

        if (string.IsNullOrEmpty(roomId))
        {
            Debug.LogError("[TreasureManagerGPS_Multiplayer] InitializeForRoom called with null/empty roomId.");
            enabled = false;
            return;
        }

        initialized = true;
        currentRoomId = roomId;

        // Get references to our singleton managers
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

        // Start init process (only once)
        initCoroutine = StartCoroutine(WaitForServices());
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

        string newId = dbRef.Child("treasures").Push().Key;
        TreasureData treasure = new TreasureData
        {
            lat = locationManager.Latitude,
            lon = locationManager.Longitude,
            collectedBy = new Dictionary<string, bool>()
        };

        dbRef.Child("treasures").Child(newId).SetRawJsonValueAsync(JsonUtility.ToJson(treasure))
            .ContinueWith(task =>
            {
                if (task.IsFaulted) LogToUI("Error adding treasure: " + task.Exception);
                else LogToUI("Treasure placed successfully!");
            });
    }

    private void StartListeningForTreasures()
    {
        if (dbRef == null || string.IsNullOrEmpty(currentRoomId)) return;

        DatabaseReference gameStateRef = dbRef.Child("rooms").Child(currentRoomId).Child("gameState");
        gameStateRef.ChildAdded += HandleTreasureAdded;
        gameStateRef.ChildChanged += HandleTreasureChanged;
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

        if (data.collectedBy == null || !data.collectedBy.ContainsKey(authManager.UserId))
        {
            localTreasures[key] = new Treasure { key = key, data = data };
            LogToUI($"New treasure '{data.name}' appeared!");
        }
        Debug.Log($"[TreasureAdded] room={currentRoomId} key={key}");
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
                Destroy(localTreasures[key].instance);

            if (currentTargetKey == key)
                currentTargetKey = null;
        }
    }

    private void CollectTargetTreasure()
    {
        if (!servicesReady || string.IsNullOrEmpty(currentTargetKey) || !localTreasures.ContainsKey(currentTargetKey))
            return;

        string myUserId = authManager.UserId;
        Treasure target = localTreasures[currentTargetKey];

        DatabaseReference treasureRef = dbRef
            .Child("rooms").Child(currentRoomId)
            .Child("gameState").Child(target.key);

        treasureRef.RunTransaction(mutableData =>
        {
            // If treasure node is missing, abort (and log)
            if (mutableData.Value == null)
            {
                Debug.LogWarning($"[Collect] ABORT: Treasure node missing at {treasureRef.Key}");
                return TransactionResult.Abort();
            }

            // Ensure we have a dictionary-like object
            if (mutableData.Value is not Dictionary<string, object> data)
            {
                Debug.LogWarning($"[Collect] ABORT: Treasure data not a Dictionary. Type={mutableData.Value.GetType()}");
                return TransactionResult.Abort();
            }

            // Get/create collectedBy as a dictionary
            Dictionary<string, object> collectedBy;

            if (data.TryGetValue("collectedBy", out object collectedByObj) && collectedByObj is Dictionary<string, object> existing)
                collectedBy = existing;
            else
                collectedBy = new Dictionary<string, object>();

            if (collectedBy.ContainsKey(myUserId))
            {
                Debug.LogWarning("[Collect] ABORT: already collected by this user.");
                return TransactionResult.Abort();
            }

            collectedBy[myUserId] = true;
            data["collectedBy"] = collectedBy;

            mutableData.Value = data;
            return TransactionResult.Success(mutableData);

        }).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                LogToUI("Error collecting treasure: " + task.Exception);
                return;
            }

            // IMPORTANT: Transaction can complete without fault but not commit
            // In Firebase Unity SDK, you should check task.Result exists AND read final state.
            DataSnapshot snapshot = task.Result;
            var json = snapshot.GetRawJsonValue();
            Debug.Log($"[Collect] Transaction result JSON: {json}");

            LogToUI("Treasure collected!");
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
            if (treasure.data.collectedBy != null && treasure.data.collectedBy.ContainsKey(authManager.UserId))
                continue;

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

        double distance = LocationManager.Haversine(target.data.lat, target.data.lon, locationManager.Latitude, locationManager.Longitude);
        return distance <= spawnRange;
    }

    private void TrySpawnTreasure()
    {
        if (string.IsNullOrEmpty(currentTargetKey) || !localTreasures.ContainsKey(currentTargetKey)) return;
        Treasure target = localTreasures[currentTargetKey];
        if (target.instance != null) return;

        if (arRaycastManager == null) return;

        var hits = new List<ARRaycastHit>();
        var screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

        if (arRaycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;
            Vector3 treasureGpsPos = GPSToUnityPosition(target.data.lat, target.data.lon);
            float distanceToTargetSpot = Vector3.Distance(
                new Vector3(hitPose.position.x, 0, hitPose.position.z),
                new Vector3(treasureGpsPos.x, 0, treasureGpsPos.z)
            );

            if (distanceToTargetSpot <= revealRadius)
            {
                if (treasurePrefab == null)
                {
                    Debug.LogError("[TreasureManagerGPS_Multiplayer] treasurePrefab not assigned.");
                    return;
                }

                target.instance = Instantiate(treasurePrefab, hitPose.position, hitPose.rotation);
            }
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
            collectButton.gameObject.SetActive(false);
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

            int huntableCount = 0;
            foreach (var t in localTreasures.Values)
            {
                if (t.data.collectedBy == null || !t.data.collectedBy.ContainsKey(authManager.UserId))
                    huntableCount++;
            }

            statusText.text = huntableCount == 0 ? "No active treasures found." : "Finding closest treasure...";
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