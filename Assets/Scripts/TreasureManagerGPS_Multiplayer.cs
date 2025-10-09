// --- Merged & Refactored Multiplayer Treasure Manager ---
// This script merges your latest version with a more performant and robust architecture.
// It fixes critical efficiency issues to ensure a smooth multiplayer experience.

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
        // MERGED: The ID is the database key, so it doesn't need to be stored inside the data itself.
        public double lat;
        public double lon;
        public bool collected;
        public string setterName; // Using a readable name is often better than a device ID.
    }

    // A local class to hold all information about a treasure, including its key and GameObject instance.
    public class Treasure
    {
        public string key;
        public TreasureData data;
        public GameObject instance;
    }

    public PlayerMode mode = PlayerMode.Finder;
    public enum PlayerMode { Setter, Finder }

    [Header("AR & GPS Settings")]
    public ARRaycastManager arRaycastManager;
    public GameObject treasurePrefab;
    public float spawnRange = 15f;
    public float revealRadius = 2.0f;
    public float collectDistance = 5f;
    public bool useIndoorDebugMode = true;
    public float updateInterval = 2.0f; // How often to check for the nearest treasure

    [Header("UI")]
    public Button setTreasureButton;
    public Button collectButton;
    public Button modeToggleButton;
    public TMP_Text statusText;
    public TMP_Text modeLabel;
    public TMP_Text distanceLabel;
    public RectTransform arrowIndicator;

    private DatabaseReference dbRef;
    private bool firebaseReady = false;
    private AudioSource audioSource;

    // --- MERGED & REFACTORED ---
    // This is the local cache. It holds the full state of all treasures.
    private Dictionary<string, Treasure> localTreasures = new Dictionary<string, Treasure>();
    private string currentTargetKey; // The key of the treasure we are currently hunting.

    void Start()
    {
        if (arRaycastManager == null) arRaycastManager = FindObjectOfType<ARRaycastManager>();
        audioSource = gameObject.AddComponent<AudioSource>();

        setTreasureButton.onClick.AddListener(SetTreasureHere);
        collectButton.onClick.AddListener(CollectTargetTreasure);
        if (modeToggleButton) modeToggleButton.onClick.AddListener(ToggleMode);

        LogToUI("Initializing...");
        StartCoroutine(LocationServiceRoutine()); // Use robust GPS startup

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                string dbUrl = "https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/";
                dbRef = FirebaseDatabase.GetInstance(dbUrl).RootReference;
                firebaseReady = true;
                SetupForCurrentMode();
                LogToUI("Firebase ready.");
            }
            else LogToUI("Firebase not available.");
        });

        SetupUI();
    }

    void OnDestroy()
    {
        // MERGED & REFACTORED: Ensure we unsubscribe from the efficient child listeners.
        if (dbRef != null)
        {
            dbRef.Child("treasures").ChildAdded -= HandleTreasureAdded;
            dbRef.Child("treasures").ChildChanged -= HandleTreasureChanged;
        }
        if (!useIndoorDebugMode && Input.location.isEnabledByUser) Input.location.Stop();
    }

    void Update()
    {
        // The Update loop is now very lightweight. It only handles AR spawning and UI updates.
        if (mode == PlayerMode.Finder && !string.IsNullOrEmpty(currentTargetKey))
        {
            if (CanScanForTreasure())
            {
                TrySpawnTreasure();
            }
        }
        UpdateUIElements();
    }

    // --- MERGED & REFACTORED: Robust GPS Initialization ---
    private IEnumerator LocationServiceRoutine()
    {
        if (!useIndoorDebugMode)
        {
            if (!Input.location.isEnabledByUser)
            {
                LogToUI("GPS disabled by user.");
                yield break;
            }
            Input.location.Start();
            int maxWait = 20;
            while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
            {
                yield return new WaitForSeconds(1);
                maxWait--;
            }
            if (Input.location.status != LocationServiceStatus.Running)
            {
                LogToUI("GPS failed to start.");
                yield break;
            }
        }
        LogToUI("GPS Ready.");
    }

    // --- MULTIPLAYER TREASURE SYSTEM ---

    private void SetTreasureHere()
    {
        if (!firebaseReady) { LogToUI("Firebase not ready."); return; }
        if (!useIndoorDebugMode && Input.location.status != LocationServiceStatus.Running)
        {
            LogToUI("GPS not ready.");
            return;
        }

        string newId = dbRef.Child("treasures").Push().Key;
        TreasureData treasure = new TreasureData
        {
            lat = GetPlayerLatitude(),
            lon = GetPlayerLongitude(),
            collected = false,
            setterName = SystemInfo.deviceName
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
        dbRef.Child("treasures").ChildAdded += HandleTreasureAdded;
        dbRef.Child("treasures").ChildChanged += HandleTreasureChanged;
        // Optionally, you can also listen for ChildRemoved.
    }

    private void HandleTreasureAdded(object sender, ChildChangedEventArgs args)
    {
        if (args.DatabaseError != null) return;

        string key = args.Snapshot.Key;
        if (localTreasures.ContainsKey(key)) return;

        TreasureData data = JsonUtility.FromJson<TreasureData>(args.Snapshot.GetRawJsonValue());
        if (!data.collected)
        {
            localTreasures[key] = new Treasure { key = key, data = data, instance = null };
            LogToUI($"New treasure from {data.setterName} appeared!");
        }
    }

    private void HandleTreasureChanged(object sender, ChildChangedEventArgs args)
    {
        if (args.DatabaseError != null) return;

        string key = args.Snapshot.Key;
        if (!localTreasures.ContainsKey(key)) return;

        TreasureData newData = JsonUtility.FromJson<TreasureData>(args.Snapshot.GetRawJsonValue());
        localTreasures[key].data = newData;

        if (newData.collected)
        {
            // If the treasure was collected by someone, destroy our local instance of it.
            if (localTreasures[key].instance != null)
            {
                Destroy(localTreasures[key].instance);
            }
            localTreasures.Remove(key); // Remove from huntable treasures
            LogToUI($"Treasure from {newData.setterName} was collected!");
        }
    }

    private void CollectTargetTreasure()
    {
        if (string.IsNullOrEmpty(currentTargetKey) || !firebaseReady || !localTreasures.ContainsKey(currentTargetKey)) return;

        Treasure target = localTreasures[currentTargetKey];

        // Mark as collected in the database
        dbRef.Child("treasures").Child(target.key).Child("collected").SetValueAsync(true);

        // No need to wait for the callback, we can act immediately for responsiveness.
        if (target.instance != null)
        {
            Destroy(target.instance);
        }
        localTreasures.Remove(target.key); // Immediately remove it from our local list
        LogToUI("Treasure collected!");
        currentTargetKey = null; // Find a new target
    }

    // --- FINDER LOGIC ---

    // MERGED & REFACTORED: This runs periodically, not every frame.
    private void UpdateFinderState()
    {
        if (mode != PlayerMode.Finder) return;

        // This is the efficient way to find the nearest treasure.
        // It runs on the local cache with ZERO database calls.
        double myLat = GetPlayerLatitude();
        double myLon = GetPlayerLongitude();

        string nearestKey = null;
        float minDistance = float.MaxValue;

        foreach (var treasure in localTreasures.Values)
        {
            // The GPSToUnityPosition result vector's magnitude is the distance
            float distance = GPSToUnityPosition(treasure.data.lat, treasure.data.lon, myLat, myLon).magnitude;
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
        if (target.instance != null) return false; // Already spawned

        return GPSToUnityPosition(target.data.lat, target.data.lon, GetPlayerLatitude(), GetPlayerLongitude()).magnitude <= spawnRange;
    }

    private void TrySpawnTreasure()
    {
        if (string.IsNullOrEmpty(currentTargetKey) || !localTreasures.ContainsKey(currentTargetKey)) return;

        Treasure target = localTreasures[currentTargetKey];
        if (target.instance != null) return; // Already spawned

        var hits = new List<ARRaycastHit>();
        var screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

        if (arRaycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;
            Vector3 treasureGpsPos = GPSToUnityPosition(target.data.lat, target.data.lon, GetPlayerLatitude(), GetPlayerLongitude());
            float distanceToTargetSpot = Vector3.Distance(new Vector3(hitPose.position.x, 0, hitPose.position.z), new Vector3(treasureGpsPos.x, 0, treasureGpsPos.z));

            if (distanceToTargetSpot <= revealRadius)
            {
                target.instance = Instantiate(treasurePrefab, hitPose.position, hitPose.rotation);
                LogToUI($"Treasure from {target.data.setterName} revealed!");
            }
        }
    }

    // --- MODE SWITCHING & UI ---

    private void ToggleMode()
    {
        mode = (mode == PlayerMode.Setter) ? PlayerMode.Finder : PlayerMode.Setter;

        // Clean up state
        CancelInvoke(nameof(UpdateFinderState));
        dbRef.Child("treasures").ChildAdded -= HandleTreasureAdded;
        dbRef.Child("treasures").ChildChanged -= HandleTreasureChanged;

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
        if (mode == PlayerMode.Finder)
        {
            LogToUI("Finder mode: listening for treasures...");
            StartListeningForTreasures();
            // Start the periodic update
            InvokeRepeating(nameof(UpdateFinderState), 1.0f, updateInterval);
        }
        else
        {
            LogToUI("Setter mode: tap to add treasures.");
            CancelInvoke(nameof(UpdateFinderState));
            // Stop listening if we are a setter to save resources
            dbRef.Child("treasures").ChildAdded -= HandleTreasureAdded;
            dbRef.Child("treasures").ChildChanged -= HandleTreasureChanged;
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
            statusText.text = localTreasures.Count == 0 ? "No active treasures found." : "Finding closest treasure...";
            return;
        }

        Treasure target = localTreasures[currentTargetKey];
        Vector3 targetGpsPos = GPSToUnityPosition(target.data.lat, target.data.lon, GetPlayerLatitude(), GetPlayerLongitude());

        float distanceToShow;
        Vector3 direction;

        if (target.instance != null)
        {
            // When spawned, UI points to the visible GameObject
            distanceToShow = Vector3.Distance(Camera.main.transform.position, target.instance.transform.position);
            direction = target.instance.transform.position - Camera.main.transform.position;
            collectButton.gameObject.SetActive(distanceToShow <= collectDistance);
        }
        else
        {
            // When hidden, UI points to the calculated GPS position
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


    // --- HELPERS ---
    private double GetPlayerLatitude() => useIndoorDebugMode ? 4.3298 : Input.location.lastData.latitude; // Kampar, Perak
    private double GetPlayerLongitude() => useIndoorDebugMode ? 101.1422 : Input.location.lastData.longitude;

    private Vector3 GPSToUnityPosition(double targetLat, double targetLon, double playerLat, double playerLon)
    {
        const double R = 6371000.0;
        double dLat = (targetLat - playerLat) * (Math.PI / 180.0);
        double dLon = (targetLon - playerLon) * (Math.PI / 180.0);
        double x = dLon * R * Math.Cos(playerLat * (Math.PI / 180.0));
        double z = dLat * R;
        return new Vector3((float)x, 0, (float)z);
    }

    private void LogToUI(string msg)
    {
        if (statusText) statusText.text = msg;
        Debug.Log(msg);
    }
}