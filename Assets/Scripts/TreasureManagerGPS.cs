// --- TreasureManagerGPS.cs (DEBUG VERSION) ---
// Please copy this entire script to replace your current one.

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;
using System;
using System.Collections.Generic;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class TreasureManagerGPS : MonoBehaviour
{
    [Serializable]
    public class TreasureData { public double lat; public double lon; public bool collected; }

    [Header("Game Mode & Settings")]
    public PlayerMode mode = PlayerMode.Setter;
    public enum PlayerMode { Setter, Finder }
    [Tooltip("How accurate the GPS signal must be (in meters) for the SETTER to place a treasure.")]
    public float requiredAccuracy = 15.0f;
    public float collectDistance = 5f;
    public float updateIntervalSeconds = 2f;

    [Header("Discovery Settings")]
    [Tooltip("The player must be within this distance (meters) to be considered 'arrived' at the treasure location.")]
    public float arrivalDistance = 5f;

    [Header("Component References")]
    public Compass compass;
    public ARRaycastManager arRaycastManager;
    public GameObject treasurePrefab;

    [Header("UI References")]
    public Button setTreasureButton;
    public Button collectButton;
    public Button modeToggleButton;
    public TMP_Text statusText;
    public TMP_Text modeLabel;

    private DatabaseReference dbRef;
    private bool firebaseInitialized = false;
    private GameObject currentTreasure;
    private double treasureLat;
    private double treasureLon;
    private bool isTreasureCollected = false;
    private bool hasPlayerArrived = false;

    void Awake() { Screen.sleepTimeout = SleepTimeout.NeverSleep; }

    void Start()
    {
        if (arRaycastManager == null) arRaycastManager = FindObjectOfType<ARRaycastManager>();
        Input.location.Start();
        setTreasureButton.onClick.AddListener(SetTreasureHere);
        collectButton.onClick.AddListener(CollectTreasure);
        if (modeToggleButton != null) modeToggleButton.onClick.AddListener(ToggleMode);
        LogToUI("Initializing Firebase...");
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                string dbUrl = "https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/";
                dbRef = FirebaseDatabase.GetInstance(dbUrl).RootReference;
                firebaseInitialized = true;
                LogToUI("Firebase Initialized.");
                SetupForCurrentMode();
            }
            else { LogToUI($"Firebase initialization failed: {task.Exception}"); }
        });
        SetupUI();
    }

    void OnApplicationPause(bool pauseStatus) { Screen.sleepTimeout = pauseStatus ? SleepTimeout.SystemSetting : SleepTimeout.NeverSleep; }

    void OnDestroy()
    {
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
        if (dbRef != null) dbRef.Child("treasure").ValueChanged -= OnTreasureDataChanged;
        if (Input.location.isEnabledByUser) Input.location.Stop();
    }

    void Update()
    {
        if (CanPlaceTreasure())
        {
            TryPlaceTreasureOnNearbySurface();
        }
    }

    private void UpdateFinderLogic()
    {
        if (mode != PlayerMode.Finder || isTreasureCollected || treasureLat == 0) return;
        if (Input.location.status != LocationServiceStatus.Running) { LogToUI("Waiting for GPS signal..."); return; }

        if (currentTreasure != null)
        {
            float threeDDistance = Vector3.Distance(Camera.main.transform.position, currentTreasure.transform.position);
            collectButton.gameObject.SetActive(threeDDistance <= collectDistance);
            LogToUI($"Distance: {threeDDistance:F1}m");
//            if (compass != null) compass.SetTreasureDirection(currentTreasure.transform.position - Camera.main.transform.position);
        }
        else
        {
            UpdateHiddenTreasure(); // MODIFIED: No longer passes parameters
        }
    }

    // ===================================================================
    // 🔹 THIS IS THE MODIFIED FUNCTION WITH DEBUG LOGS
    // ===================================================================
    private void UpdateHiddenTreasure()
    {
        double playerLat = GetPlayerLatitude();
        double playerLon = GetPlayerLongitude();
        float currentAccuracy = Input.location.lastData.horizontalAccuracy;

        // treasureGpsPos is the treasure's position relative to the player.
        // The player's position in this coordinate system is always (0, 0, 0).
        Vector3 treasureGpsPos = GPSToUnityPosition(treasureLat, treasureLon, playerLat, playerLon);

        // Therefore, the distance to the treasure is simply the magnitude of this offset vector.
        float horizontalDistance = new Vector2(treasureGpsPos.x, treasureGpsPos.z).magnitude;

        // This is now a meaningful distance in meters.
        hasPlayerArrived = (horizontalDistance <= arrivalDistance);

        if (hasPlayerArrived)
        {
            LogToUI($"You've arrived! (Accuracy: {currentAccuracy:F1}m). Point at a surface to reveal.");
        }
        else
        {
            LogToUI($"Distance: {horizontalDistance:F1}m (Signal Accuracy: {currentAccuracy:F1}m)");
        }

        // You can now also correctly update the compass
        // The direction is simply the treasure's position vector, since the player is at the origin.
        // if (compass != null) compass.SetTreasureDirection(treasureGpsPos);
    }

    private bool CanPlaceTreasure() { return mode == PlayerMode.Finder && currentTreasure == null && hasPlayerArrived; }

    private void TryPlaceTreasureOnNearbySurface()
    {
        if (arRaycastManager == null) return;
        var screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
        var hits = new List<ARRaycastHit>();
        if (arRaycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;
            currentTreasure = Instantiate(treasurePrefab, hitPose.position, hitPose.rotation);
            LogToUI("You found the treasure!");
            hasPlayerArrived = false;
        }
    }

    public void SetTreasureHere()
    {
        if (!firebaseInitialized) { LogToUI("Firebase not ready."); return; }
        if (Input.location.status != LocationServiceStatus.Running) { LogToUI("GPS not ready."); return; }
        float currentAccuracy = Input.location.lastData.horizontalAccuracy;
        if (currentAccuracy > requiredAccuracy)
        {
            LogToUI($"GPS signal too weak (Accuracy: {currentAccuracy:F1}m). Move to an open area and wait.");
            return;
        }
        double currentLat = GetPlayerLatitude();
        double currentLon = GetPlayerLongitude();
        var treasureData = new TreasureData { lat = currentLat, lon = currentLon, collected = false };
        dbRef.Child("treasure").SetRawJsonValueAsync(JsonUtility.ToJson(treasureData));
        LogToUI($"Treasure set with {currentAccuracy:F1}m accuracy.");
    }

    private void StartListeningForTreasure() { dbRef.Child("treasure").ValueChanged += OnTreasureDataChanged; }
    private void OnTreasureDataChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null) { LogToUI("Firebase error: " + args.DatabaseError.Message); return; }
        if (!args.Snapshot.Exists) return;
        TreasureData data = JsonUtility.FromJson<TreasureData>(args.Snapshot.GetRawJsonValue());
        treasureLat = data.lat; treasureLon = data.lon; isTreasureCollected = data.collected;
        if (isTreasureCollected && currentTreasure != null) { Destroy(currentTreasure); }
    }
    private void CollectTreasure()
    {
        if (!firebaseInitialized || currentTreasure == null) return;
        dbRef.Child("treasure/collected").SetValueAsync(true);
        Destroy(currentTreasure);
        currentTreasure = null;
        collectButton.gameObject.SetActive(false);
    }
    public void ToggleMode()
    {
        mode = (mode == PlayerMode.Setter) ? PlayerMode.Finder : PlayerMode.Setter;
        if (currentTreasure != null) Destroy(currentTreasure);
        CancelInvoke(nameof(UpdateFinderLogic));
        if (dbRef != null) dbRef.Child("treasure").ValueChanged -= OnTreasureDataChanged;
        SetupUI();
        if (firebaseInitialized) SetupForCurrentMode();
    }
    private void SetupForCurrentMode()
    {
        if (mode == PlayerMode.Setter) { LogToUI("You are the Setter. Tap 'Set Treasure'."); }
        else
        {
            LogToUI("You are the Finder. Waiting for treasure...");
            StartListeningForTreasure();
            InvokeRepeating(nameof(UpdateFinderLogic), 1f, updateIntervalSeconds);
        }
    }
    private void SetupUI()
    {
        setTreasureButton.gameObject.SetActive(mode == PlayerMode.Setter);
        collectButton.gameObject.SetActive(false);
        if (modeLabel != null) modeLabel.text = $"Mode: {mode}";
    }
    private double GetPlayerLatitude() { return Input.location.lastData.latitude; }
    private double GetPlayerLongitude() { return Input.location.lastData.longitude; }
    private Vector3 GPSToUnityPosition(double targetLat, double targetLon, double playerLat, double playerLon)
    {
        const double R = 6371000.0;
        double dLat = (targetLat - playerLat) * (Math.PI / 180.0);
        double dLon = (targetLon - playerLon) * (Math.PI / 180.0);
        double x = dLon * R * Math.Cos(playerLat * (Math.PI / 180.0));
        double z = dLat * R;
        return new Vector3((float)x, 0, (float)z);
    }
    private void LogToUI(string message) { if (statusText != null) statusText.text = message; Debug.Log(message); }
}