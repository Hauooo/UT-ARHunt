using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class TreasureSetter : MonoBehaviour
{
    [Header("References")]
    public Button setTreasureButton;        // Button for Player A to set treasure
    public TextMeshProUGUI statusText;      // Status text

    void Start()
    {
        setTreasureButton.onClick.AddListener(SetTreasureHere);
        statusText.text = "Initializing GPS...";
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

        statusText.text = "GPS ready! Tap button to set treasure.";
    }

    public void SetTreasureHere()
    {
        if (Input.location.status != LocationServiceStatus.Running)
        {
            statusText.text = "GPS not ready!";
            return;
        }

        float lat = Input.location.lastData.latitude;
        float lon = Input.location.lastData.longitude;

        // Save into PlayerPrefs (for Player B to fetch)
        PlayerPrefs.SetFloat("TreasureLat", lat);
        PlayerPrefs.SetFloat("TreasureLon", lon);
        PlayerPrefs.Save();

        statusText.text = $"Treasure set!\nLat: {lat:F6}, Lon: {lon:F6}";
    }
}
