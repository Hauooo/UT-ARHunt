using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System;

public class OSMTileLoader : MonoBehaviour
{
    [Header("Map Settings")]
    public int zoom = 16;
    public int tileGridSize = 3; // 3x3 tile grid around center

    [Header("UI")]
    public RectTransform mapContainer;
    public GameObject tilePrefab; // RawImage prefab

    private double centerLat;
    private double centerLon;

    public void CenterMapOn(double lat, double lon)
    {
        centerLat = lat;
        centerLon = lon;
        LoadTiles();
    }

    private void LoadTiles()
    {
        foreach (Transform child in mapContainer) Destroy(child.gameObject);

        // ✅ Force layout rebuild so rect.width/height are correct before reading
        Canvas.ForceUpdateCanvases();

        float mapWidth = mapContainer.rect.width;
        float mapHeight = mapContainer.rect.height;

        // Use the SMALLER dimension so tiles always cover both axes
        int safeGridSize = Mathf.Max(1, tileGridSize);
        float tileSize = Mathf.Max(mapWidth, mapHeight) / safeGridSize;

        int centerX = LonToTileX(centerLon, zoom);
        int centerY = LatToTileY(centerLat, zoom);

        // Use enough tiles to cover the full screen even if not square
        int halfX = Mathf.CeilToInt((mapWidth / tileSize) / 2);
        int halfY = Mathf.CeilToInt((mapHeight / tileSize) / 2);

        for (int dx = -halfX; dx <= halfX; dx++)
        {
            for (int dy = -halfY; dy <= halfY; dy++)
            {
                int tileX = centerX + dx;
                int tileY = centerY + dy;
                StartCoroutine(FetchTile(tileX, tileY, dx, dy, tileSize));
            }
        }
    }

    private IEnumerator FetchTile(int x, int y, int gridX, int gridY, float tileSize)
    {
        string url = $"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png";

        using var request = UnityWebRequestTexture.GetTexture(url);
        request.SetRequestHeader("User-Agent", "AR-HUNT/1.0 (Unity6)");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success) yield break;

        Texture2D tex = DownloadHandlerTexture.GetContent(request);
        GameObject tile = Instantiate(tilePrefab, mapContainer);
        tile.GetComponent<RawImage>().texture = tex;

        var rect = tile.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(tileSize, tileSize);

        // Centre the tile grid on the map container's centre (0,0)
        rect.anchoredPosition = new Vector2(gridX * tileSize, -gridY * tileSize);
    }

    // OSM tile coordinate math
    public static int LonToTileX(double lon, int z) =>
        (int)Math.Floor((lon + 180.0) / 360.0 * (1 << z));

    public static int LatToTileY(double lat, int z)
    {
        double latRad = lat * Math.PI / 180.0;
        return (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * (1 << z));
    }

    // Convert GPS to pixel offset from map center (for marker placement)
    public Vector2 GpsToPixelOffset(double lat, double lon, float tileSize)
    {
        int centerTileX = LonToTileX(centerLon, zoom);
        int centerTileY = LatToTileY(centerLat, zoom);
        int targetTileX = LonToTileX(lon, zoom);
        int targetTileY = LatToTileY(lat, zoom);

        float dx = (targetTileX - centerTileX) * tileSize;
        float dy = -(targetTileY - centerTileY) * tileSize;

        return new Vector2(dx, dy);
    }
}