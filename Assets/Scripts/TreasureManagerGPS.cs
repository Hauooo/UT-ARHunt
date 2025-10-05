using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TreasureManagerGPS : MonoBehaviour
{
    public enum PlayerMode { Setter, Finder }
    public PlayerMode mode = PlayerMode.Setter;

    [Header("UI References")]
    public Button setTreasureButton;
    public Button collectButton;
    public Button modeToggleButton; // NEW: Toggle button
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI modeLabel; // Optional text label to show current mode

    [Header("Gameplay")]
    public GameObject treasurePrefab;
    public Transform arrowIndicator;

    private GameObject currentTreasure;
    private bool treasureSet = false;

    private float treasureLat;
    private float treasureLon;

    public float collectDistance = 5f;
    public bool debugIndoorTest = true;
    public float indoorSpawnDistance = 2f;

    void Start()
    {
        SetupUI();
        Input.location.Start();

        if (modeToggleButton != null)
            modeToggleButton.onClick.AddListener(ToggleMode);
    }

    void SetupUI()
    {
        if (mode == PlayerMode.Setter)
        {
            setTreasureButton.gameObject.SetActive(true);
            collectButton.gameObject.SetActive(false);
            setTreasureButton.onClick.RemoveAllListeners();
            setTreasureButton.onClick.AddListener(SetTreasureHere);
            LogToUI("Mode: Setter");
            if (modeLabel) modeLabel.text = "Mode: Player A (Setter)";
        }
        else
        {
            setTreasureButton.gameObject.SetActive(false);
            collectButton.gameObject.SetActive(true);
            collectButton.onClick.RemoveAllListeners();
            collectButton.onClick.AddListener(CollectTreasure);
            LogToUI("Mode: Finder");
            if (modeLabel) modeLabel.text = "Mode: Player B (Finder)";
        }
    }

    void ToggleMode()
    {
        mode = (mode == PlayerMode.Setter) ? PlayerMode.Finder : PlayerMode.Setter;

        // Clean up old treasure when switching
        if (currentTreasure != null)
        {
            Destroy(currentTreasure);
            currentTreasure = null;
        }

        SetupUI();
    }

    void Update()
    {
        UpdateTreasurePosition();
    }

    void LogToUI(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
        Debug.Log(msg);
    }

    void SetTreasureHere()
    {
        if (Input.location.status != LocationServiceStatus.Running)
        {
            LogToUI("GPS not running!");
            return;
        }

        treasureLat = (float)Input.location.lastData.latitude;
        treasureLon = (float)Input.location.lastData.longitude;

        PlayerPrefs.SetFloat("TreasureLat", treasureLat);
        PlayerPrefs.SetFloat("TreasureLon", treasureLon);
        PlayerPrefs.Save();

        treasureSet = true;
        LogToUI($"Treasure set at: {treasureLat}, {treasureLon}");
    }

    void UpdateTreasurePosition()
    {
        if (mode != PlayerMode.Finder) return;

        treasureLat = PlayerPrefs.GetFloat("TreasureLat", 0f);
        treasureLon = PlayerPrefs.GetFloat("TreasureLon", 0f);

        if (treasureLat == 0f && treasureLon == 0f)
        {
            LogToUI("No treasure set (PlayerPrefs empty).");
            return;
        }

        if (Input.location.status != LocationServiceStatus.Running)
        {
            LogToUI("GPS not running in Finder.");
            return;
        }

        double playerLat = Input.location.lastData.latitude;
        double playerLon = Input.location.lastData.longitude;

        Vector3 treasurePos = GPSToUnityPosition(treasureLat, treasureLon, playerLat, playerLon);

        if (currentTreasure == null)
        {
            if (debugIndoorTest && treasurePos.magnitude < 0.5f)
            {
                treasurePos = Camera.main.transform.position + Camera.main.transform.forward * indoorSpawnDistance;
                LogToUI("Indoor test: treasure spawned in front of camera.");
            }

            currentTreasure = Instantiate(treasurePrefab, treasurePos, Quaternion.identity);
            LogToUI($"Treasure spawned at: {treasurePos}");
        }
        else
        {
            currentTreasure.transform.position = treasurePos;
        }

        if (arrowIndicator != null)
        {
            Vector3 dir = treasurePos - Camera.main.transform.position;
            dir.y = 0;
            if (dir.magnitude > 0.1f)
                arrowIndicator.rotation = Quaternion.LookRotation(dir);
        }

        float distance = Vector3.Distance(Camera.main.transform.position, treasurePos);
        LogToUI($"Distance to treasure: {distance:F1} m");
        collectButton.gameObject.SetActive(distance <= collectDistance);
    }

    void CollectTreasure()
    {
        if (currentTreasure != null)
        {
            Destroy(currentTreasure);
            LogToUI("Treasure collected!");
        }
        else
        {
            LogToUI("No treasure to collect.");
        }
    }

    Vector3 GPSToUnityPosition(double targetLat, double targetLon, double playerLat, double playerLon)
    {
        float earthRadius = 6371000f;
        float dLat = Mathf.Deg2Rad * (float)(targetLat - playerLat);
        float dLon = Mathf.Deg2Rad * (float)(targetLon - playerLon);
        float lat1 = Mathf.Deg2Rad * (float)playerLat;

        float x = dLon * Mathf.Cos(lat1) * earthRadius;
        float z = dLat * earthRadius;

        return new Vector3(x, 0, z);
    }
}
