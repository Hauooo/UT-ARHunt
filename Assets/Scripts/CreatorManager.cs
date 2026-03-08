using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;
using System.Collections;
using System.Collections.Generic;
using System;

/// <summary>
/// Controls the redesigned CreatorScene:
/// - Shows OSM map centred on user's GPS location
/// - Top-right buttons: New, Edit, Delete
/// - Each button opens a modal panel for the respective CRUD operation
/// </summary>
public class CreatorMapController : MonoBehaviour
{
    // ── Map ───────────────────────────────────────────────────────────────
    [Header("Map")]
    [SerializeField] private OSMTileLoader tileLoader;
    [SerializeField] private RectTransform mapContainer;
    [SerializeField] private GameObject tilePrefab;
    [SerializeField] private GameObject pinPrefab;          // treasure pin
    [SerializeField] private RectTransform playerMarker;    // blue dot
    [SerializeField] private RectTransform pinsLayer;      // parent for all treasure pins (so they appear above tiles)

    // ── Status ────────────────────────────────────────────────────────────
    [Header("Status")]
    [SerializeField] private TMP_Text statusText;

    // ── Top-Right Buttons ─────────────────────────────────────────────────
    [Header("Top Right Buttons")]
    [SerializeField] private Button newButton;
    [SerializeField] private Button editButton;
    [SerializeField] private Button deleteButton;
    [SerializeField] private Button backButton;

    // ── New Set Panel ─────────────────────────────────────────────────────
    [Header("New Set Panel")]
    [SerializeField] private GameObject newSetPanel;
    [SerializeField] private TMP_InputField newSetNameInput;
    [SerializeField] private Button newPlacePinButton;
    [SerializeField] private TMP_Text newPinCountText;
    [SerializeField] private Button newSaveButton;
    [SerializeField] private Button newCancelButton;

    // ── Edit Set Panel ────────────────────────────────────────────────────
    [Header("Edit Set Panel")]
    [SerializeField] private GameObject editSetPanel;
    [SerializeField] private TMP_Dropdown editSetDropdown;
    [SerializeField] private TMP_InputField editSetNameInput;
    [SerializeField] private Button editPlacePinButton;
    [SerializeField] private TMP_Text editPinCountText;
    [SerializeField] private Button editSaveButton;
    [SerializeField] private Button editCancelButton;

    // ── Delete Panel ──────────────────────────────────────────────────────
    [Header("Delete Set Panel")]
    [SerializeField] private GameObject deleteSetPanel;
    [SerializeField] private TMP_Dropdown deleteSetDropdown;
    [SerializeField] private TMP_Text deleteConfirmText;
    [SerializeField] private Button confirmDeleteButton;
    [SerializeField] private Button deleteCancelButton;

    // ── Private State ─────────────────────────────────────────────────────
    private GameManager gameManager;
    private LocationManager locationManager;
    private DatabaseReference dbRef;

    private bool gpsReady = false;
    private Dictionary<string, TreasureSetData> loadedSets = new();

    // Working list of pins being placed (for New / Edit)
    private List<TreasureManagerGPS_Multiplayer.TreasureData> workingPins = new();
    private List<GameObject> previewPinObjects = new();   // map pin GameObjects for the current session
    private Dictionary<string, List<GameObject>> setMapPins = new(); // all loaded set pins on map

    private string editingSetId = null; // which set is being edited

    // ─────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Start()
    {
        gameManager = GameManager.Instance;
        locationManager = LocationManager.Instance;

        if (gameManager == null) { Debug.LogError("[CreatorMapController] No GameManager!"); return; }

        // Init Firebase
        try
        {
            dbRef = FirebaseDatabase
                .GetInstance("https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/")
                .RootReference;
        }
        catch (Exception ex)
        {
            Debug.LogError("[CreatorMapController] Firebase init failed: " + ex.Message);
        }

        // Wire top-right buttons
        newButton.onClick.AddListener(OnNewClicked);
        editButton.onClick.AddListener(OnEditClicked);
        deleteButton.onClick.AddListener(OnDeleteClicked);
        backButton.onClick.AddListener(() => gameManager.ReturnToMenu());

        // Wire New panel
        newPlacePinButton.onClick.AddListener(PlacePinAtCurrentLocation);
        newSaveButton.onClick.AddListener(SaveNewSet);
        newCancelButton.onClick.AddListener(CloseAllPanels);

        // Wire Edit panel
        editPlacePinButton.onClick.AddListener(PlacePinAtCurrentLocation);
        editSaveButton.onClick.AddListener(SaveEditedSet);
        editCancelButton.onClick.AddListener(CloseAllPanels);

        // Wire Delete panel
        confirmDeleteButton.onClick.AddListener(ConfirmDelete);
        deleteCancelButton.onClick.AddListener(CloseAllPanels);

        CloseAllPanels();
        SetStatus("Acquiring GPS...");

        // Wait for GPS then initialise map
        StartCoroutine(WaitForGPSThenInit());
    }

    private void Update()
    {
        // Keep the player marker centred on the map (GPS updates smoothly)
        if (gpsReady && playerMarker != null)
        {
            // Player is always at the map centre — the tile loader re-centres on them
            playerMarker.anchoredPosition = Vector2.zero;
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region GPS & Map Init
    // ─────────────────────────────────────────────────────────────────────

    private IEnumerator WaitForGPSThenInit()
    {
        // Poll until LocationManager is ready
        while (locationManager == null || locationManager.Status != LocationManager.LocationStatus.Ready)
        {
            SetStatus($"Acquiring GPS... ({locationManager?.Status})");
            yield return new WaitForSeconds(0.5f);
        }

        gpsReady = true;
        SetStatus("GPS Ready ✓");

        double lat = locationManager.Latitude;
        double lon = locationManager.Longitude;

        // Centre the OSM map on the player's location
        tileLoader.CenterMapOn(lat, lon);

        // Subscribe to location updates to keep map centred while user moves
        locationManager.OnLocationUpdated += OnLocationUpdated;

        // Load and display all of this user's existing treasure sets on the map
        LoadAllSetsOntoMap();
    }

    private void OnLocationUpdated(double lat, double lon)
    {
        // Re-centre map as user moves
        tileLoader.CenterMapOn(lat, lon);
        RefreshAllPinsOnMap();
    }

    private void OnDestroy()
    {
        if (locationManager != null)
            locationManager.OnLocationUpdated -= OnLocationUpdated;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region READ — Load All Sets onto Map
    // ─────────────────────────────────────────────────────────────────────

    private void LoadAllSetsOntoMap()
    {
        if (dbRef == null || AuthManager.Instance == null) return;

        string userId = AuthManager.Instance.UserId;
        SetStatus("Loading your treasure sets...");

        dbRef.Child("treasureSets")
            .OrderByChild("createdBy").EqualTo(userId)
            .GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    SetStatus("Error loading sets.");
                    return;
                }

                ClearAllMapPins();
                loadedSets.Clear();

                foreach (var child in task.Result.Children)
                {
                    var set = JsonUtility.FromJson<TreasureSetData>(child.GetRawJsonValue());
                    set.setId = child.Key;
                    loadedSets[set.setId] = set;
                    SpawnPinsForSet(set);
                }

                SetStatus(loadedSets.Count == 0
                    ? "No sets yet. Tap [New] to create one."
                    : $"{loadedSets.Count} set(s) loaded. GPS Ready ✓");
            });
    }

    private void SpawnPinsForSet(TreasureSetData set)
    {
        float tileSize = mapContainer.rect.width / tileLoader.tileGridSize;
        setMapPins[set.setId] = new List<GameObject>();

        foreach (var treasure in set.treasures)
        {
            Vector2 offset = tileLoader.GpsToPixelOffset(treasure.lat, treasure.lon, tileSize);
            GameObject pin = Instantiate(pinPrefab, pinsLayer);
            pin.GetComponent<RectTransform>().anchoredPosition = offset;

            var label = pin.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = treasure.name;

            setMapPins[set.setId].Add(pin);
        }
    }

    private void RefreshAllPinsOnMap()
    {
        ClearAllMapPins();
        foreach (var set in loadedSets.Values) SpawnPinsForSet(set);
        // Re-draw in-progress preview pins too
        RedrawPreviewPins();
    }

    private void ClearAllMapPins()
    {
        foreach (var pinList in setMapPins.Values)
            foreach (var pin in pinList) Destroy(pin);
        setMapPins.Clear();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region CREATE — New Set
    // ─────────────────────────────────────────────────────────────────────

    private void OnNewClicked()
    {
        workingPins.Clear();
        ClearPreviewPins();
        newSetNameInput.text = "";
        UpdateNewPinCountText();
        newSetPanel.SetActive(true);
        editSetPanel.SetActive(false);
        deleteSetPanel.SetActive(false);
    }

    private void SaveNewSet()
    {
        string setName = newSetNameInput.text.Trim();
        if (string.IsNullOrEmpty(setName)) { SetStatus("Please enter a set name."); return; }
        if (workingPins.Count == 0) { SetStatus("Place at least one pin."); return; }

        if (dbRef == null || AuthManager.Instance == null) return;

        newSaveButton.interactable = false;
        SetStatus("Saving...");

        string newSetId = dbRef.Child("treasureSets").Push().Key;
        var newSet = new TreasureSetData
        {
            setId = newSetId,
            setName = setName,
            createdBy = AuthManager.Instance.UserId,
            treasures = new List<TreasureManagerGPS_Multiplayer.TreasureData>(workingPins)
        };

        dbRef.Child("treasureSets").Child(newSetId)
            .SetRawJsonValueAsync(JsonUtility.ToJson(newSet))
            .ContinueWithOnMainThread(task =>
            {
                newSaveButton.interactable = true;
                if (task.IsFaulted)
                {
                    SetStatus("Save failed. Try again.");
                    Debug.LogError("[CreatorMapController] Save failed: " + task.Exception);
                    return;
                }

                SetStatus($"'{setName}' saved!");
                loadedSets[newSetId] = newSet;
                SpawnPinsForSet(newSet);
                ClearPreviewPins();
                workingPins.Clear();
                CloseAllPanels();
            });
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region UPDATE — Edit Set
    // ─────────────────────────────────────────────────────────────────────

    private void OnEditClicked()
    {
        if (loadedSets.Count == 0) { SetStatus("No sets to edit. Create one first."); return; }

        // Populate dropdown with set names
        editSetDropdown.ClearOptions();
        var options = new List<string>();
        foreach (var set in loadedSets.Values) options.Add(set.setName);
        editSetDropdown.AddOptions(options);

        // Pre-load first set's data
        LoadSetIntoEditPanel(GetSetByDropdownIndex(0));

        editSetDropdown.onValueChanged.RemoveAllListeners();
        editSetDropdown.onValueChanged.AddListener(idx => LoadSetIntoEditPanel(GetSetByDropdownIndex(idx)));

        workingPins.Clear();
        ClearPreviewPins();

        editSetPanel.SetActive(true);
        newSetPanel.SetActive(false);
        deleteSetPanel.SetActive(false);
    }

    private void LoadSetIntoEditPanel(TreasureSetData set)
    {
        if (set == null) return;
        editingSetId = set.setId;
        editSetNameInput.text = set.setName;

        // Copy existing treasures into workingPins so user can add more
        workingPins = new List<TreasureManagerGPS_Multiplayer.TreasureData>(set.treasures);
        ClearPreviewPins();
        RedrawPreviewPins();
        UpdateEditPinCountText();
    }

    private void SaveEditedSet()
    {
        if (string.IsNullOrEmpty(editingSetId)) return;

        string setName = editSetNameInput.text.Trim();
        if (string.IsNullOrEmpty(setName)) { SetStatus("Please enter a set name."); return; }
        if (workingPins.Count == 0) { SetStatus("A set must have at least one pin."); return; }

        editSaveButton.interactable = false;
        SetStatus("Saving changes...");

        var updatedSet = new TreasureSetData
        {
            setId = editingSetId,
            setName = setName,
            createdBy = AuthManager.Instance.UserId,
            treasures = new List<TreasureManagerGPS_Multiplayer.TreasureData>(workingPins)
        };

        dbRef.Child("treasureSets").Child(editingSetId)
            .SetRawJsonValueAsync(JsonUtility.ToJson(updatedSet))
            .ContinueWithOnMainThread(task =>
            {
                editSaveButton.interactable = true;
                if (task.IsFaulted) { SetStatus("Update failed."); return; }

                // Refresh local cache and map
                loadedSets[editingSetId] = updatedSet;

                // Remove old pins for this set and respawn
                if (setMapPins.ContainsKey(editingSetId))
                {
                    foreach (var p in setMapPins[editingSetId]) Destroy(p);
                    setMapPins.Remove(editingSetId);
                }
                SpawnPinsForSet(updatedSet);
                ClearPreviewPins();
                workingPins.Clear();
                editingSetId = null;
                SetStatus($"'{setName}' updated!");
                CloseAllPanels();
            });
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region DELETE — Remove Set
    // ─────────────────────────────────────────────────────────────────────

    private void OnDeleteClicked()
    {
        if (loadedSets.Count == 0) { SetStatus("No sets to delete."); return; }

        deleteSetDropdown.ClearOptions();
        var options = new List<string>();
        foreach (var set in loadedSets.Values) options.Add(set.setName);
        deleteSetDropdown.AddOptions(options);

        UpdateDeleteConfirmText(GetSetByDropdownIndex(0));

        deleteSetDropdown.onValueChanged.RemoveAllListeners();
        deleteSetDropdown.onValueChanged.AddListener(idx =>
            UpdateDeleteConfirmText(GetSetByDropdownIndex(idx)));

        deleteSetPanel.SetActive(true);
        newSetPanel.SetActive(false);
        editSetPanel.SetActive(false);
    }

    private void UpdateDeleteConfirmText(TreasureSetData set)
    {
        if (set == null) return;
        deleteConfirmText.text = $"Delete \"{set.setName}\"?\nThis cannot be undone.";
    }

    private void ConfirmDelete()
    {
        var setToDelete = GetSetByDropdownIndex(deleteSetDropdown.value);
        if (setToDelete == null) return;

        confirmDeleteButton.interactable = false;
        SetStatus("Deleting...");

        dbRef.Child("treasureSets").Child(setToDelete.setId)
            .RemoveValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                confirmDeleteButton.interactable = true;
                if (task.IsFaulted) { SetStatus("Delete failed."); return; }

                // Remove from map and local cache
                if (setMapPins.ContainsKey(setToDelete.setId))
                {
                    foreach (var p in setMapPins[setToDelete.setId]) Destroy(p);
                    setMapPins.Remove(setToDelete.setId);
                }

                loadedSets.Remove(setToDelete.setId);
                SetStatus($"'{setToDelete.setName}' deleted.");
                CloseAllPanels();
            });
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region Shared Pin Placement (New & Edit)
    // ─────────────────────────────────────────────────────────────────────

    private void PlacePinAtCurrentLocation()
    {
        if (!gpsReady) { SetStatus("GPS not ready yet."); return; }

        var newPin = new TreasureManagerGPS_Multiplayer.TreasureData
        {
            name = $"Treasure #{workingPins.Count + 1}",
            lat = locationManager.Latitude,
            lon = locationManager.Longitude,
            points = 100
        };

        workingPins.Add(newPin);
        SpawnPreviewPin(newPin);

        // Update the correct panel's counter
        UpdateNewPinCountText();
        UpdateEditPinCountText();
    }

    private void SpawnPreviewPin(TreasureManagerGPS_Multiplayer.TreasureData treasure)
    {
        float tileSize = mapContainer.rect.width / tileLoader.tileGridSize;
        Vector2 offset = tileLoader.GpsToPixelOffset(treasure.lat, treasure.lon, tileSize);
        GameObject pin = Instantiate(pinPrefab, pinsLayer);
        pin.GetComponent<RectTransform>().anchoredPosition = offset;

        var label = pin.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = treasure.name;

        // Tint preview pins differently (e.g. yellow) so user can distinguish them
        var img = pin.GetComponent<Image>();
        if (img != null) img.color = Color.yellow;

        previewPinObjects.Add(pin);
    }

    private void RedrawPreviewPins()
    {
        ClearPreviewPins();
        foreach (var treasure in workingPins) SpawnPreviewPin(treasure);
    }

    private void ClearPreviewPins()
    {
        foreach (var p in previewPinObjects) Destroy(p);
        previewPinObjects.Clear();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region Helpers
    // ───────────────────────────────────────────────────────────────��─────

    private void CloseAllPanels()
    {
        newSetPanel.SetActive(false);
        editSetPanel.SetActive(false);
        deleteSetPanel.SetActive(false);
        workingPins.Clear();
        ClearPreviewPins();
        editingSetId = null;
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log("[CreatorMapController] " + msg);
    }

    private void UpdateNewPinCountText()
    {
        if (newPinCountText != null)
            newPinCountText.text = $"Pins placed: {workingPins.Count}";
    }

    private void UpdateEditPinCountText()
    {
        if (editPinCountText != null)
            editPinCountText.text = $"Pins placed: {workingPins.Count}";
    }

    /// <summary>Returns the TreasureSetData matching a dropdown index (by insertion order).</summary>
    private TreasureSetData GetSetByDropdownIndex(int index)
    {
        int i = 0;
        foreach (var set in loadedSets.Values)
        {
            if (i == index) return set;
            i++;
        }
        return null;
    }

    #endregion
}