using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class TreasureFinder : MonoBehaviour
{
    [Header("References")]
    public GameObject treasurePrefab;       // Prefab for the treasure
    public Button collectButton;            // Collect button
    public TextMeshProUGUI statusText;      // Status text

    [Header("Settings")]
    public float collectDistance = 5f;      // Distance in meters to collect
    public float updateRate = 1f;           // GPS update interval

    private GameObject currentTreasure;
    private float treasureLat, treasureLon;

    void Start()
    {
        collectButton.gameObject.SetActive(false);
        collectButton.onClick.AddListener(CollectTreasure);
        StartCoroutine(StartLocationService());
    }

    IEnumerator StartLocationService()
    {
        if (!Input.location.isEnabledByUser)
        {
            statusText.text = "GPS is disabled!";
            yield break;
        }

        Input.location.Start();

        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (maxWait <= 0 || Input.location.status != LocationServiceStatus.Running)
        {
            statusText.text = "GPS failed to start!";
            yield break;
        }

        // Get treasure coordinates from PlayerPrefs (simulating Player A's save)
        treasureLat = PlayerPrefs.GetFloat("TreasureLat", 0f);
        treasureLon = PlayerPrefs.GetFloat("TreasureLon", 0f);

        if (treasureLat == 0f && treasureLon == 0f)
        {
            statusText.text = "No treasure set by Player A.";
            yield break;
        }

        statusText.text = "Treasure active!";

        // Start updating treasure position
        InvokeRepeating(nameof(UpdateTreasurePosition), 0f, updateRate);
    }

    void UpdateTreasurePosition()
    {
        double playerLat = Input.location.lastData.latitude;
        double playerLon = Input.location.lastData.longitude;

        Vector3 treasurePosition = GeoToWorldPosition(playerLat, playerLon, treasureLat, treasureLon);

        if (currentTreasure == null)
        {
            currentTreasure = Instantiate(treasurePrefab, treasurePosition, Quaternion.identity);
        }
        else
        {
            currentTreasure.transform.position = treasurePosition;
        }

        // Update distance
        float distance = Vector3.Distance(Camera.main.transform.position, treasurePosition);
        statusText.text = $"Distance: {distance:F1} m";

        // Enable collect button if close enough
        collectButton.gameObject.SetActive(distance <= collectDistance);
    }

    Vector3 GeoToWorldPosition(double playerLat, double playerLon, double targetLat, double targetLon)
    {
        float earthRadius = 6371000f; // in meters
        float dLat = Mathf.Deg2Rad * (float)(targetLat - playerLat);
        float dLon = Mathf.Deg2Rad * (float)(targetLon - playerLon);
        float lat1 = Mathf.Deg2Rad * (float)playerLat;
        float x = dLon * Mathf.Cos(lat1) * earthRadius;
        float z = dLat * earthRadius;
        return new Vector3(x, 0, z);
    }

    void CollectTreasure()
    {
        if (currentTreasure == null) return;

        float distance = Vector3.Distance(Camera.main.transform.position, currentTreasure.transform.position);
        if (distance <= collectDistance)
        {
            Destroy(currentTreasure);
            currentTreasure = null;
            statusText.text = "Treasure collected!";
            collectButton.gameObject.SetActive(false);
        }
        else
        {
            statusText.text = "Move closer to collect!";
        }
    }
}

