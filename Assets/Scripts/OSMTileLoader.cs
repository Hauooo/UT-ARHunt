using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;

public class OSMTileLoader : MonoBehaviour
{
    [Header("Map Settings")]
    public int zoom = 16;
    public int tileGridSize = 3;

    [Header("UI")]
    public RectTransform mapContainer;
    public GameObject tilePrefab;

    private double centerLat;
    private double centerLon;
    private string cacheFolderPath;

    // --- Dynamic Loading State ---
    private Dictionary<Vector2Int, GameObject> activeTiles = new Dictionary<Vector2Int, GameObject>();
    private float tileSize;
    private double baseFracX;
    private double baseFracY;
    private bool isMapReady = false;

    private void Awake()
    {
        cacheFolderPath = Path.Combine(Application.persistentDataPath, "OSMTileCache");
        if (!Directory.Exists(cacheFolderPath))
        {
            Directory.CreateDirectory(cacheFolderPath);
        }
    }

    public void CenterMapOn(double lat, double lon)
    {
        centerLat = lat;
        centerLon = lon;

        // 1. Reset the map container to the center to prevent drifting math
        mapContainer.anchoredPosition = Vector2.zero;

        // 2. Base tileSize on the screen/viewport, NOT the infinite mapContainer
        RectTransform parentRect = mapContainer.parent.GetComponent<RectTransform>();
        float viewWidth = parentRect != null ? parentRect.rect.width : Screen.width;
        float viewHeight = parentRect != null ? parentRect.rect.height : Screen.height;

        int safeGridSize = Mathf.Max(1, tileGridSize);
        tileSize = Mathf.Max(viewWidth, viewHeight) / safeGridSize;

        // 3. Calculate the exact fractional tile of our starting center
        baseFracX = LonToTileXDecimal(centerLon, zoom);
        baseFracY = LatToTileYDecimal(centerLat, zoom);

        // 4. Clear old tiles
        foreach (var tile in activeTiles.Values)
        {
            if (tile != null) Destroy(tile);
        }
        activeTiles.Clear();

        isMapReady = true;
        UpdateVisibleTiles(); // Load the initial view
    }

    private void Update()
    {
        if (isMapReady)
        {
            UpdateVisibleTiles();
        }
    }

    private void UpdateVisibleTiles()
    {
        if (tileSize == 0) return;

        // 1. Find the current center of the screen in the map's local space.
        // If mapContainer moved RIGHT (x > 0), the screen view is moving LEFT over the map (x < 0).
        Vector2 viewCenterLocal = -mapContainer.anchoredPosition / mapContainer.localScale.x;

        // 2. Convert the local offset back into OSM tile coordinates
        double currentFracX = baseFracX + (viewCenterLocal.x / tileSize);
        double currentFracY = baseFracY - (viewCenterLocal.y / tileSize); // Unity Y up, OSM Y down

        int currentCenterX = (int)Math.Floor(currentFracX);
        int currentCenterY = (int)Math.Floor(currentFracY);

        // 3. Determine how many tiles we need to draw based on screen size and zoom
        RectTransform parentRect = mapContainer.parent.GetComponent<RectTransform>();
        float viewWidth = parentRect != null ? parentRect.rect.width : Screen.width;
        float viewHeight = parentRect != null ? parentRect.rect.height : Screen.height;

        float currentTileSize = tileSize * mapContainer.localScale.x;

        // Add +1 buffer so tiles spawn just off-screen before sliding in
        int halfX = Mathf.CeilToInt((viewWidth / currentTileSize) / 2f) + 1;
        int halfY = Mathf.CeilToInt((viewHeight / currentTileSize) / 2f) + 1;

        HashSet<Vector2Int> visibleKeys = new HashSet<Vector2Int>();

        // 4. Loop through the visible grid and spawn missing tiles
        for (int dx = -halfX; dx <= halfX; dx++)
        {
            for (int dy = -halfY; dy <= halfY; dy++)
            {
                int tX = currentCenterX + dx;
                int tY = currentCenterY + dy;
                Vector2Int key = new Vector2Int(tX, tY);

                visibleKeys.Add(key);

                if (!activeTiles.ContainsKey(key))
                {
                    // Mark as "loading" (null) to prevent duplicate downloads
                    activeTiles[key] = null;
                    StartCoroutine(FetchTile(tX, tY));
                }
            }
        }

        // 5. Cleanup tiles that are no longer visible
        List<Vector2Int> keysToRemove = new List<Vector2Int>();
        foreach (var kvp in activeTiles)
        {
            if (!visibleKeys.Contains(kvp.Key))
            {
                if (kvp.Value != null) Destroy(kvp.Value); // Destroy UI GameObject
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            activeTiles.Remove(key);
        }
    }

    private IEnumerator FetchTile(int x, int y)
    {
        Vector2Int key = new Vector2Int(x, y);
        string tileFilename = $"{zoom}_{x}_{y}.png";
        string filePath = Path.Combine(cacheFolderPath, tileFilename);
        Texture2D tex = null;

        if (File.Exists(filePath))
        {
            byte[] fileData = File.ReadAllBytes(filePath);
            tex = new Texture2D(2, 2);
            tex.LoadImage(fileData);
        }
        else
        {
            string url = $"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png";
            using var request = UnityWebRequestTexture.GetTexture(url);
            request.SetRequestHeader("User-Agent", "AR-HUNT/1.0 (Unity6; Mobile)");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // Remove from dictionary so it can try again later if the user pans back
                if (activeTiles.ContainsKey(key)) activeTiles.Remove(key);
                yield break;
            }

            tex = DownloadHandlerTexture.GetContent(request);
            File.WriteAllBytes(filePath, tex.EncodeToPNG());
        }

        // Check if the user swiped really fast and this tile was culled while downloading
        if (!activeTiles.ContainsKey(key))
        {
            Destroy(tex);
            yield break;
        }

        GameObject tile = Instantiate(tilePrefab, mapContainer);
        tile.name = $"Tile_{zoom}_{x}_{y}";
        tile.GetComponent<RawImage>().texture = tex;

        var rect = tile.GetComponent<RectTransform>();
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(tileSize, tileSize);

        // Position the tile perfectly relative to our starting center
        float offsetX = (float)((x + 0.5) - baseFracX) * tileSize;
        float offsetY = (float)(baseFracY - (y + 0.5)) * tileSize;

        rect.anchoredPosition = new Vector2(offsetX, offsetY);

        // Assign the finished GameObject to the dictionary
        activeTiles[key] = tile;
    }

    // ─────────────────────────────────────────────────────────────────────
    // OSM Web Mercator Math
    // ─────────────────────────────────────────────────────────────────────

    public static int LonToTileX(double lon, int z) => (int)Math.Floor(LonToTileXDecimal(lon, z));
    public static int LatToTileY(double lat, int z) => (int)Math.Floor(LatToTileYDecimal(lat, z));

    public static double LonToTileXDecimal(double lon, int z) => (lon + 180.0) / 360.0 * (1 << z);

    public static double LatToTileYDecimal(double lat, int z)
    {
        double latRad = lat * Math.PI / 180.0;
        return (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * (1 << z);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Marker Positioning
    // ─────────────────────────────────────────────────────────────────────

    public Vector2 GpsToLocalAnchorPosition(double lat, double lon)
    {
        double targetX = LonToTileXDecimal(lon, zoom);
        double targetY = LatToTileYDecimal(lat, zoom);

        return new Vector2(
            (float)(targetX - baseFracX) * tileSize,
            (float)(baseFracY - targetY) * tileSize
        );
    }

    public Vector3 GpsToWorldPosition(double lat, double lon)
    {
        Vector2 localPos = GpsToLocalAnchorPosition(lat, lon);
        return mapContainer.TransformPoint(localPos);
    }
}