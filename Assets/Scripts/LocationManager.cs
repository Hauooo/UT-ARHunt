using UnityEngine;
using System.Collections;

public class LocationManager : MonoBehaviour
{
    public static LocationManager Instance;

    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public bool IsReady { get; private set; } = false;

    private Vector2 smoothedGPS;
    private float smoothFactor = 0.1f; // Adjust for more or less smoothing

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    IEnumerator Start()
    {
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogError("Location service not enabled by user.");
            yield break;
        }

        Input.location.Start(10f, 10f); // accuracy, minDistance

        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (Input.location.status != LocationServiceStatus.Running)
        {
            Debug.LogError("Unable to start location service.");
            yield break;
        }

        IsReady = true;
        Debug.Log("Location service started.");
    }

    void Update()
    {
        if (Input.location.status == LocationServiceStatus.Running)
        {
            double smoothLatitude = Input.location.lastData.latitude;
            double smoothLongitude = Input.location.lastData.longitude;

            smoothedGPS = Vector2.Lerp(smoothedGPS, new Vector2((float)smoothLatitude, (float)smoothLongitude), smoothFactor);
        }
    }

    private void OnDisable()
    {
        Input.location.Stop();
    }
}
