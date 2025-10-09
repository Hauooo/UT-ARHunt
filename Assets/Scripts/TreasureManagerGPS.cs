// --- TreasureManagerGPS (Suggested Merged Version) ---
// This script combines the best features of both previous versions.
// It includes GPS accuracy checks while restoring the "scan-to-reveal" AR gameplay.
// The Firebase logic has been fixed to be consistent.

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

public class TreasureManagerGPS_Merged : MonoBehaviour // Renamed to avoid conflicts
{
    [Serializable]
    public class TreasureData { public double lat; public double lon; public bool collected; }

    [Header("Game Mode & Settings")]
    public PlayerMode mode = PlayerMode.Setter;
    public enum PlayerMode { Setter, Finder }
    public float collectDistance = 5f;
    public float updateIntervalSeconds = 1.5f;

    [Header("Spawning & Discovery (Restored Logic)")]
    [Tooltip("How close (in meters) the Finder must be to start scanning for the treasure in AR.")]
    public float spawnRange = 15f;
    [Tooltip("How close (in meters) the camera's aim must be to the treasure's true location to reveal it.")]
    public float revealRadius = 2.0f;

    [Header("GPS Settings & Debugging")]
    [Tooltip("How accurate the GPS signal must be (in meters) for the Setter to place a treasure.")]
    public float requiredAccuracy = 15.0f;
    [Tooltip("Bypasses GPS for indoor testing. Uses fake coordinates.")]
    public bool useIndoorDebugMode = true;

    [Header("Component References")]
    public ARRaycastManager arRaycastManager;
    public GameObject treasurePrefab;

    [Header("UI References")]
    public Button setTreasureButton;
    public Button collectButton;
    public Button modeToggleButton;
    public TMP_Text statusText;
    public TMP_Text modeLabel;
    public TMP_Text distanceLabel;
    public RectTransform arrowIndicator;

    [Header("Audio & Visual Feedback")]
    public AudioClip foundSound;
    public AudioClip collectSound;
    public ParticleSystem treasureFoundEffect;

    // --- Private State ---
    private DatabaseReference dbRef;
    private bool firebaseInitialized = false;
    private GameObject currentTreasure;
    private double treasureLat;
    private double treasureLon;
    private bool isTreasureCollected = false;
    private bool isCloseEnoughToSpawn = false;
    private AudioSource audioSource;
    private float lastKnownDistance = -1f;

    void Awake()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    void Start()
    {
        if (arRaycastManager == null) arRaycastManager = FindObjectOfType<ARRaycastManager>();
        if (!useIndoorDebugMode)
        {
            Input.location.Start();
        }

        setTreasureButton.onClick.AddListener(SetTreasureHere);
        collectButton.onClick.AddListener(CollectTreasure);
        if (modeToggleButton != null) modeToggleButton.onClick.AddListener(ToggleMode);

        audioSource = gameObject.AddComponent<AudioSource>();

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

    // MERGED: Added from second script for better mobile UX.
    void OnApplicationPause(bool pauseStatus)
    {
        Screen.sleepTimeout = pauseStatus ? SleepTimeout.SystemSetting : SleepTimeout.NeverSleep;
    }

    void OnDestroy()
    {
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
        if (dbRef != null) dbRef.Child("treasure").ValueChanged -= OnTreasureDataChanged;
        if (!useIndoorDebugMode && Input.location.isEnabledByUser) Input.location.Stop();
    }

    void Update()
    {
        if (CanScanForTreasure())
        {
            TrySpawnTreasure();
        }
        UpdateUIElements();
    }

    // This logic runs periodically for the Finder.
    private void UpdateFinderLogic()
    {
        if (mode != PlayerMode.Finder || isTreasureCollected || treasureLat == 0) return;

        if (!useIndoorDebugMode && Input.location.status != LocationServiceStatus.Running)
        {
            LogToUI("Waiting for GPS signal...");
            return;
        }

        // We only need to calculate distance and update UI when the treasure is hidden.
        // Once spawned, the Update() loop handles the UI.
        if (currentTreasure == null)
        {
            float currentAccuracy = useIndoorDebugMode ? 5.0f : Input.location.lastData.horizontalAccuracy;
            Vector3 treasureGpsPos = GPSToUnityPosition(treasureLat, treasureLon, GetPlayerLatitude(), GetPlayerLongitude());

            // The distance is the magnitude of the relative position vector.
            lastKnownDistance = new Vector2(treasureGpsPos.x, treasureGpsPos.z).magnitude;

            isCloseEnoughToSpawn = (lastKnownDistance <= spawnRange);

            if (isCloseEnoughToSpawn)
            {
                LogToUI($"You're close! Scan the area to find the treasure. (Signal: {currentAccuracy:F1}m)");
            }
            else
            {
                LogToUI($"Follow the arrow. (Signal: {currentAccuracy:F1}m)");
            }
        }
    }

    // MERGED & REVERTED: Restored the engaging "scan-to-reveal" mechanic from the first script.
    private bool CanScanForTreasure()
    {
        return mode == PlayerMode.Finder && currentTreasure == null && isCloseEnoughToSpawn && !isTreasureCollected;
    }

    private void TrySpawnTreasure()
    {
        if (arRaycastManager == null) return;

        var screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
        var hits = new List<ARRaycastHit>();

        if (arRaycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;
            Vector3 treasureGpsPos = GPSToUnityPosition(treasureLat, treasureLon, GetPlayerLatitude(), GetPlayerLongitude());

            // This is the critical check: Is the spot you're looking at in AR close to the treasure's actual GPS location?
            float distanceToTargetSpot = Vector3.Distance(
                new Vector3(hitPose.position.x, 0, hitPose.position.z),
                new Vector3(treasureGpsPos.x, 0, treasureGpsPos.z)
            );

            if (distanceToTargetSpot <= revealRadius)
            {
                currentTreasure = Instantiate(treasurePrefab, hitPose.position, hitPose.rotation);
                LogToUI("🎉 You found the treasure!");
                if (foundSound) audioSource.PlayOneShot(foundSound);
                if (treasureFoundEffect) Instantiate(treasureFoundEffect, hitPose.position, Quaternion.identity);
                isCloseEnoughToSpawn = false; // Stop trying to spawn.
            }
        }
    }

    // 🔹 Firebase Logic 🔹

    // MERGED: Using the improved version with accuracy checks.
    public void SetTreasureHere()
    {
        if (!firebaseInitialized) { LogToUI("Firebase not ready."); return; }
        if (!useIndoorDebugMode && Input.location.status != LocationServiceStatus.Running) { LogToUI("GPS not ready."); return; }

        float currentAccuracy = useIndoorDebugMode ? 5.0f : Input.location.lastData.horizontalAccuracy;
        if (currentAccuracy > requiredAccuracy)
        {
            LogToUI($"GPS signal too weak ({currentAccuracy:F1}m). Required: <{requiredAccuracy}m. Move to an open area.");
            return;
        }

        double currentLat = GetPlayerLatitude();
        double currentLon = GetPlayerLongitude();

        var treasureData = new TreasureData { lat = currentLat, lon = currentLon, collected = false };

        // FIXED: Writing to a simple, single "/treasure" path for consistency.
        dbRef.Child("treasure").SetRawJsonValueAsync(JsonUtility.ToJson(treasureData)).ContinueWithOnMainThread(task => {
            if (task.IsFaulted || task.IsCanceled)
            {
                LogToUI("Error setting treasure: " + task.Exception);
            }
            else
            {
                LogToUI("Treasure set and uploaded successfully!");
            }
        });
    }

    private void StartListeningForTreasure()
    {
        // FIXED: Subscribing to the correct event handler name.
        dbRef.Child("treasure").ValueChanged += OnTreasureDataChanged;
    }

    // FIXED: Reverted to the correct logic for handling a SINGLE treasure object.
    private void OnTreasureDataChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null) { LogToUI("Firebase error: " + args.DatabaseError.Message); return; }
        if (!args.Snapshot.Exists)
        {
            LogToUI("No treasure has been set yet.");
            return;
        }

        TreasureData data = JsonUtility.FromJson<TreasureData>(args.Snapshot.GetRawJsonValue());
        treasureLat = data.lat;
        treasureLon = data.lon;
        isTreasureCollected = data.collected;

        if (isTreasureCollected)
        {
            if (currentTreasure != null) Destroy(currentTreasure);
            LogToUI("The treasure has already been collected!");
            collectButton.gameObject.SetActive(false);
        }
        else if (mode == PlayerMode.Finder)
        {
            LogToUI("Treasure location received! Start hunting.");
        }
    }

    private void CollectTreasure()
    {
        if (!firebaseInitialized || currentTreasure == null) return;

        // FIXED: Correctly targeting the 'collected' field of the single treasure.
        dbRef.Child("treasure/collected").SetValueAsync(true);
        if (collectSound) audioSource.PlayOneShot(collectSound);
        if (treasureFoundEffect) Instantiate(treasureFoundEffect, currentTreasure.transform.position, Quaternion.identity);
        Destroy(currentTreasure);
        currentTreasure = null;
        collectButton.gameObject.SetActive(false);
        LogToUI("💎 Treasure collected!");
    }

    // 🔹 UI & Mode Handling 🔹
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
        // Reset state
        isTreasureCollected = false;
        isCloseEnoughToSpawn = false;
        treasureLat = 0;
        treasureLon = 0;
        lastKnownDistance = -1f;

        if (mode == PlayerMode.Setter)
        {
            LogToUI("You are the Setter. Find a spot and tap 'Set Treasure'.");
        }
        else
        {
            LogToUI("You are the Finder. Waiting for treasure location...");
            StartListeningForTreasure();
            InvokeRepeating(nameof(UpdateFinderLogic), 1f, updateIntervalSeconds);
        }
    }

    private void SetupUI()
    {
        setTreasureButton.gameObject.SetActive(mode == PlayerMode.Setter);
        collectButton.gameObject.SetActive(false);
        if (modeLabel != null) modeLabel.text = $"Mode: {mode}";
        if (distanceLabel != null) distanceLabel.gameObject.SetActive(mode == PlayerMode.Finder);
        if (arrowIndicator != null) arrowIndicator.gameObject.SetActive(mode == PlayerMode.Finder);
    }

    // Centralized UI update method called every frame from Update().
    private void UpdateUIElements()
    {
        if (mode != PlayerMode.Finder || isTreasureCollected)
        {
            if (distanceLabel != null) distanceLabel.gameObject.SetActive(false);
            if (arrowIndicator != null) arrowIndicator.gameObject.SetActive(false);
            return;
        }

        Vector3 targetPosition;
        float distanceToShow;

        if (currentTreasure != null)
        {
            // When treasure is visible, point to the AR object.
            targetPosition = currentTreasure.transform.position;
            distanceToShow = Vector3.Distance(Camera.main.transform.position, targetPosition);
            collectButton.gameObject.SetActive(distanceToShow <= collectDistance);
        }
        else
        {
            // When hidden, point to the calculated GPS location.
            if (treasureLat == 0) return; // No treasure to point to.
            targetPosition = GPSToUnityPosition(treasureLat, treasureLon, GetPlayerLatitude(), GetPlayerLongitude());
            distanceToShow = lastKnownDistance;
            collectButton.gameObject.SetActive(false);
        }

        // Update Distance Label
        if (distanceLabel != null)
        {
            distanceLabel.gameObject.SetActive(true);
            distanceLabel.text = $"{distanceToShow:F1} m";
        }

        // Update Arrow Indicator
        if (arrowIndicator != null)
        {
            arrowIndicator.gameObject.SetActive(true);
            Vector3 dirToTarget = targetPosition - Camera.main.transform.position;
            dirToTarget.y = 0; // Keep arrow flat

            if (dirToTarget.sqrMagnitude > 0.01f)
            {
                float angle = Vector3.SignedAngle(Camera.main.transform.forward, dirToTarget, Vector3.up);
                arrowIndicator.localEulerAngles = new Vector3(0, 0, -angle);
            }
        }
    }

    // 🔹 Helpers 🔹

    // MERGED: Re-added debug mode functionality.
    private double GetPlayerLatitude() => useIndoorDebugMode ? 3.1390 : Input.location.lastData.latitude; // Fake coords for KL
    private double GetPlayerLongitude() => useIndoorDebugMode ? 101.6869 : Input.location.lastData.longitude; // Fake coords for KL

    private Vector3 GPSToUnityPosition(double targetLat, double targetLon, double playerLat, double playerLon)
    {
        const double R = 6371000.0;
        double dLat = (targetLat - playerLat) * (Math.PI / 180.0);
        double dLon = (targetLon - playerLon) * (Math.PI / 180.0);
        double x = dLon * R * Math.Cos(playerLat * (Math.PI / 180.0));
        double z = dLat * R;
        return new Vector3((float)x, Camera.main.transform.position.y, (float)z); // Place at camera height for distance calcs
    }

    private void LogToUI(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log(message);
    }
}