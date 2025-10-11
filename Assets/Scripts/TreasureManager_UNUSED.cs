using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;
using Unity.Collections;

public class TreasureManager : MonoBehaviour
{
    [Header("References")]
    public GameObject treasurePrefab;       // Your treasure prefab
    public Button collectButton;            // Collect button
    public TextMeshProUGUI statusText;      // Status text
    public ARPlaneManager arPlaneManager;   // AR Plane Manager from AR Session Origin

    private GameObject currentTreasure;

    void OnEnable()
    {
        arPlaneManager.planesChanged += OnPlanesChanged;
    }

    void OnDisable()
    {
        arPlaneManager.planesChanged -= OnPlanesChanged;
    }

    void Start()
    {
        collectButton.gameObject.SetActive(false);
        collectButton.onClick.AddListener(CollectTreasure);
        statusText.text = "Searching for planes...";
    }

    void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // If no treasure yet and at least one plane added, spawn
        if (currentTreasure == null && arPlaneManager.trackables.count > 0)
        {
            SpawnTreasureOnRandomPlane();
        }
    }

    void SpawnTreasureOnRandomPlane()
    {
        // Collect all currently tracked and actively tracked planes
        var planes = new List<ARPlane>();
        foreach (var plane in arPlaneManager.trackables)
        {
            if (plane.trackingState == UnityEngine.XR.ARSubsystems.TrackingState.Tracking)
                planes.Add(plane);
        }

        if (planes.Count == 0)
        {
            statusText.text = "No planes detected yet!";
            return;
        }

        // Pick a random plane
        ARPlane selectedPlane = planes[Random.Range(0, planes.Count)];

        // Pick a random point inside the plane boundary
        Vector3 spawnPosition = GetRandomPointInPlane(selectedPlane);
        spawnPosition.y += treasureHeightOffset; // raise slightly above plane

        // Destroy previous treasure if any
        if (currentTreasure != null)
            Destroy(currentTreasure);

        // Spawn new treasure
        currentTreasure = Instantiate(treasurePrefab, spawnPosition, Quaternion.identity);

        // Update UI
        statusText.text = "A treasure appeared!";
        collectButton.gameObject.SetActive(true);
    }


    Vector3 GetRandomPointInPlane(ARPlane plane)
    {
        Vector2[] boundary = plane.boundary.ToArray();
        int attempts = 10;

        for (int i = 0; i < attempts; i++)
        {
            // Pick a random point in the plane's local space
            Vector2 randomPoint = new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f));
            Vector3 localPoint = new Vector3(randomPoint.x * plane.size.x, 0, randomPoint.y * plane.size.y);
            // Convert to world space
            Vector3 worldPoint = plane.transform.TransformPoint(localPoint);
            // Check if the point is inside the boundary polygon
            if (IsPointInPolygon(worldPoint, boundary, plane.transform))
            {
                return worldPoint;
            }
        }

        // Fallback to plane center if no valid point found
        return plane.transform.position;

        // Helper to check if a point is inside the polygon defined by the boundary

        bool IsPointInPolygon(Vector3 point, Vector2[] polygon, Transform planeTransform)
        {
            int j = polygon.Length - 1;
            bool inside = false;
            Vector3 localPoint = planeTransform.InverseTransformPoint(point);
            for (int i = 0; i < polygon.Length; j = i++)
            {
                if ((polygon[i].y > localPoint.z) != (polygon[j].y > localPoint.z) &&
                    (localPoint.x < (polygon[j].x - polygon[i].x) * (localPoint.z - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

    }

    [Header("Settings")]
    public float treasureHeightOffset = 0.1f; // Height offset above the plane
    public float collectDistance = 0.5f;    // Distance within which the treasure can be collected

    void CollectTreasure()
    {
        if (currentTreasure == null)
        {
            return;
        }

        float distance = Vector3.Distance(Camera.main.transform.position, currentTreasure.transform.position);

        if (distance <= collectDistance)
        {
            Destroy(currentTreasure);
            currentTreasure = null;
            statusText.text = "Treasure collected!";
            collectButton.gameObject.SetActive(false);
            Invoke(nameof(SpawnTreasureOnRandomPlane), 2f); // Spawn new treasure after delay
        }
        else
        {
            statusText.text = "Move closer to collect the treasure!";
        }
    }
}
