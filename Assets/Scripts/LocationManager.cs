using UnityEngine;
using System.Collections;
using System; // Required for Action

public class LocationManager : MonoBehaviour
{
    public static LocationManager Instance;

    public enum LocationStatus { Initializing, Ready, Failed, PermissionDenied }

    // --- Public Properties ---
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public float Accuracy { get; private set; }
    public LocationStatus Status { get; private set; } = LocationStatus.Initializing;

    // --- C# Events for other scripts to subscribe to ---
    public event Action OnLocationReady;
    public event Action<double, double> OnLocationUpdated; // Passes new Lat, Lng
    public event Action<LocationStatus> OnStatusChanged;

    // --- Inspector Settings ---
    [Header("Location Settings")]
    [SerializeField] private float desiredAccuracyInMeters = 5f;
    [SerializeField] private float updateDistanceInMeters = 5f;
    [SerializeField] private float smoothFactor = 5.0f;

    [Header("Update Throttling")]
    [Tooltip("Minimum distance in meters the user must move to trigger the OnLocationUpdated event.")]
    [SerializeField] private float locationUpdateThreshold = 20f;

    private double lastUpdateLatitude;
    private double lastUpdateLongitude;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private IEnumerator Start()
    {
        
        // On a real device, run the normal initialization
        yield return InitializeLocationService();
    }

    private IEnumerator InitializeLocationService()
    {
        SetStatus(LocationStatus.Initializing);

        if (!Input.location.isEnabledByUser)
        {
            Debug.LogError("Location service not enabled by user. Please enable it in settings.");
            SetStatus(LocationStatus.PermissionDenied);
            yield break;
        }

        Input.location.Start(desiredAccuracyInMeters, updateDistanceInMeters);

        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (maxWait <= 0 || Input.location.status == LocationServiceStatus.Failed)
        {
            Debug.LogError("Unable to start location service: Timed out or failed.");
            SetStatus(LocationStatus.Failed);
            yield break;
        }

        // --- Success ---
        // Set initial values and trigger the Ready event
        Latitude = Input.location.lastData.latitude;
        Longitude = Input.location.lastData.longitude;
        Accuracy = Input.location.lastData.horizontalAccuracy;
        lastUpdateLatitude = Latitude;
        lastUpdateLongitude = Longitude;

        SetStatus(LocationStatus.Ready);
        Debug.Log($"Location service started successfully: ({Latitude}, {Longitude})");
    }

    private void Update()
    {
        if (Status != LocationStatus.Ready) return;



        // Smooth the location values every frame for jitter-free visuals
        double smoothT = Time.deltaTime * smoothFactor;
        Latitude = Lerp(Latitude, Input.location.lastData.latitude, smoothT);
        Longitude = Lerp(Longitude, Input.location.lastData.longitude, smoothT);
        Accuracy = Input.location.lastData.horizontalAccuracy;

        // Check if we've moved enough to trigger the throttled update event
        double distanceMoved = Haversine(lastUpdateLatitude, lastUpdateLongitude, Latitude, Longitude);
        if (distanceMoved >= locationUpdateThreshold)
        {
            Debug.Log($"User moved {distanceMoved:F1}m. Firing OnLocationUpdated event.");
            lastUpdateLatitude = Latitude;
            lastUpdateLongitude = Longitude;
            OnLocationUpdated?.Invoke(Latitude, Longitude);
        }
    }

    

    private void SetStatus(LocationStatus newStatus)
    {
        if (Status == newStatus) return;

        Status = newStatus;
        OnStatusChanged?.Invoke(newStatus); // Notify subscribers of any status change

        if (newStatus == LocationStatus.Ready)
        {
            // The ?.Invoke is a null-check to ensure we only call the event if something is subscribed
            OnLocationReady?.Invoke();
        }
    }

    private void OnDisable()
    {
        if (Input.location.status == LocationServiceStatus.Running)
        {
            Input.location.Stop();
        }
    }

    // --- Helper Functions ---
    private double Lerp(double a, double b, double t)
    {
        return a + (b - a) * Math.Clamp(t, 0.0, 1.0);
    }

    // Calculate distance between two lat/lng points in meters.
    // Making it static allows other scripts (like TreasureManager) to use it easily.
    public static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = (lat2 - lat1) * (Math.PI / 180);
        var dLon = (lon2 - lon1) * (Math.PI / 180);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * (Math.PI / 180)) * Math.Cos(lat2 * (Math.PI / 180)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }
}