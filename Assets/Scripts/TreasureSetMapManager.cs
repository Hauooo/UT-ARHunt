using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;
using System.Collections.Generic;

public class TreasureSetMapManager : MonoBehaviour
{
    [Header("References")]
    public OSMTileLoader tileLoader;
    public GameObject markerPrefab;         // UI pin prefab
    public RectTransform mapContainer;
    public TMP_Text feedbackText;

    private DatabaseReference dbRef;
    private Dictionary<string, List<GameObject>> setMarkers = new();
    private float tileSize => mapContainer.rect.width / tileLoader.tileGridSize;

    private void Awake()
    {
        dbRef = Firebase.Database.FirebaseDatabase
            .GetInstance("https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/")
            .RootReference;
    }

    // ── READ ──────────────────────────────────────────────────────────────
    public void LoadAndDisplaySets(string userId, double centerLat, double centerLon)
    {
        tileLoader.CenterMapOn(centerLat, centerLon);

        dbRef.Child("treasureSets")
            .OrderByChild("createdBy").EqualTo(userId)
            .GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted) { feedbackText.text = "Error loading sets."; return; }
                ClearAllMarkers();
                foreach (var child in task.Result.Children)
                {
                    var set = JsonUtility.FromJson<TreasureSetData>(child.GetRawJsonValue());
                    set.setId = child.Key;
                    PlaceMarkersForSet(set);
                }
            });
    }

    // ── CREATE marker (called after SaveNewTreasureSet succeeds) ──────────
    public void PlaceMarkersForSet(TreasureSetData set)
    {
        setMarkers[set.setId] = new List<GameObject>();
        foreach (var treasure in set.treasures)
        {
            Vector2 offset = tileLoader.GpsToPixelOffset(treasure.lat, treasure.lon, tileSize);
            GameObject pin = Instantiate(markerPrefab, mapContainer);
            pin.GetComponent<RectTransform>().anchoredPosition = offset;
            pin.GetComponentInChildren<TMP_Text>().text = treasure.name;
            setMarkers[set.setId].Add(pin);
        }
    }

    // ── UPDATE: rename set ────────────────────────────────────────────────
    public void RenameSet(string setId, string newName)
    {
        dbRef.Child("treasureSets").Child(setId).Child("setName")
            .SetValueAsync(newName)
            .ContinueWithOnMainThread(t =>
                feedbackText.text = t.IsFaulted ? "Rename failed." : $"Renamed to '{newName}'.");
    }

    // ── UPDATE: reposition a single treasure within a set ─────────────────
    public void UpdateTreasurePosition(string setId, int index, double newLat, double newLon)
    {
        dbRef.Child("treasureSets").Child(setId).GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted) return;
                var set = JsonUtility.FromJson<TreasureSetData>(task.Result.GetRawJsonValue());
                set.setId = setId;
                if (index < 0 || index >= set.treasures.Count) return;

                set.treasures[index].lat = newLat;
                set.treasures[index].lon = newLon;

                dbRef.Child("treasureSets").Child(setId)
                    .SetRawJsonValueAsync(JsonUtility.ToJson(set))
                    .ContinueWithOnMainThread(t =>
                    {
                        if (!t.IsFaulted) RefreshMarkers(set);
                    });
            });
    }

    // ── DELETE ────────────────────────────────────────────────────────────
    public void DeleteSet(string setId)
    {
        dbRef.Child("treasureSets").Child(setId).RemoveValueAsync()
            .ContinueWithOnMainThread(t =>
            {
                if (t.IsFaulted) { feedbackText.text = "Delete failed."; return; }
                RemoveMarkersForSet(setId);
                feedbackText.text = "Set deleted.";
            });
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private void RefreshMarkers(TreasureSetData set)
    {
        RemoveMarkersForSet(set.setId);
        PlaceMarkersForSet(set);
    }

    private void RemoveMarkersForSet(string setId)
    {
        if (!setMarkers.ContainsKey(setId)) return;
        foreach (var pin in setMarkers[setId]) Destroy(pin);
        setMarkers.Remove(setId);
    }

    private void ClearAllMarkers()
    {
        foreach (var key in new List<string>(setMarkers.Keys)) RemoveMarkersForSet(key);
    }
}