using UnityEngine;
using System.Collections;

public class LocationManager : MonoBehaviour
{
    public static LocationManager Instance;

    // SUGGESTION: Use a detailed enum for status instead of a simple boolean.
    public enum LocationStatus { Initializing, Ready, Failed, PermissionDenied }

    // Public properties for other scripts to access
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public float Accuracy { get; private set; }
    public LocationStatus Status { get; private set; } = LocationStatus.Initializing;

    // Settings for smoothing
    [SerializeField] private float smoothFactor = 5.0f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Good practice for a manager
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private IEnumerator Start()
    {
        // Start the initialization process
        Status = LocationStatus.Initializing;

        // 1. Check for user permission
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogError("Location service not enabled by user. Please enable it in settings.");
            Status = LocationStatus.PermissionDenied;
            yield break;
        }

        // 2. Start the location service
        Input.location.Start(5f, 5f);

        // 3. Wait for initialization with a timeout
        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        // 4. Handle failures
        if (maxWait <= 0 || Input.location.status == LocationServiceStatus.Failed)
        {
            Debug.LogError("Unable to start location service: Timed out or failed.");
            Status = LocationStatus.Failed;
            yield break;
        }

        // 5. Success!
        Status = LocationStatus.Ready;
        // Set initial values immediately without smoothing
        Latitude = Input.location.lastData.latitude;
        Longitude = Input.location.lastData.longitude;
        Accuracy = Input.location.lastData.horizontalAccuracy;
        Debug.Log("Location service started successfully.");
    }

    private void Update()
    {
        // Only update if the service is ready
        if (Status == LocationStatus.Ready)
        {
            // Use a double-based Lerp for better precision
            double smoothT = Time.deltaTime * smoothFactor;
            Latitude = Lerp(Latitude, Input.location.lastData.latitude, smoothT);
            Longitude = Lerp(Longitude, Input.location.lastData.longitude, smoothT);
            Accuracy = Input.location.lastData.horizontalAccuracy;
        }
    }

    private void OnDisable()
    {
        // FIX: Safely stop the service only if it's running
        if (Input.location.status == LocationServiceStatus.Running)
        {
            Input.location.Stop();
        }
    }

    // Helper function for double-precision linear interpolation
    private double Lerp(double a, double b, double t)
    {
        return a + (b - a) * System.Math.Clamp(t, 0.0, 1.0);
    }
}