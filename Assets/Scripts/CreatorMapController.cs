using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;
using System.Collections;
using System.Collections.Generic;
using System;
using Firebase.Auth;

/// <summary>
/// Controls the redesigned CreatorScene:
/// - Shows OSM map centred on user's GPS location
/// - Top-right buttons: New, Edit, Delete, Upload
/// - Each button opens a modal panel for the respective operation
/// </summary>
public class CreatorMapController : MonoBehaviour
{
    // ── Map ───────────────────────────────────────────────────────────────
    [Header("Map")]
    [SerializeField] private OSMTileLoader tileLoader;
    [SerializeField] private RectTransform mapContainer;
    [SerializeField] private GameObject tilePrefab;
    [SerializeField] private GameObject pinPrefab;
    [SerializeField] private RectTransform playerMarker;
    [SerializeField] private RectTransform pinsLayer;

    // ── Status ────────────────────────────────────────────────────────────
    [Header("Status")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Dropdown setSelectionDropdown;

    // ── Top-Right Buttons ─────────────────────────────────────────────────
    [Header("Top Right Buttons")]
    [SerializeField] private Button newButton;
    [SerializeField] private Button editButton;
    [SerializeField] private Button deleteButton;
    [SerializeField] private Button uploadButton;
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

    // ── Upload Panel ──────────────────────────────────────────────────────
    [Header("Upload Panel")]
    [SerializeField] private GameObject uploadPanelRoot;
    [SerializeField] private TMP_InputField levelNameInput;
    [SerializeField] private TMP_InputField levelDescriptionInput;
    [SerializeField] private Button uploadConfirmButton;
    [SerializeField] private Button uploadCancelButton;
    [SerializeField] private TMP_Text uploadFeedbackText;
    [SerializeField] private Scrollbar uploadProgressBar;

    //Treasure Editor
    [Header("Treasure Editor")]
    [SerializeField] private GameObject treasureEditorPanel;
    [SerializeField] private TMP_InputField treasureNameInput;
    [SerializeField] private TMP_Text treasureLocationText;
    [SerializeField] private TMP_Dropdown treasureSelectDropdown;
    [SerializeField] private Button deleteTreasureButton;
    [SerializeField] private Button saveTreasureButton;
    [SerializeField] private Button closeTreasureEditorButton;

    [Header("Checkpoint Editor")]
    [SerializeField] private CheckpointEditorController checkpointEditor;  // ← Fixed typo
    [SerializeField] private GameObject checkpointEditorPanel;  // ← Fixed typo
    [SerializeField] private Button openCheckpointEditorButton;
    [SerializeField] private Button saveCheckpointEditsButton;

    private int selectedTreasureIndex = -1;

    // ── Challenge Panel ───────────────────────────────────────────────────
    [Header("Challenge Config")]
    [SerializeField] private ChallengeConfigController challengeConfig;


    // Collection mode (0 = Free Order, 1 = In Order) waiting to be implemented in UI
    [Header("Collection Rule")]
    [SerializeField] private TMP_Dropdown newCollectionModeDropdown;  // ← For New Set
    [SerializeField] private TMP_Dropdown editCollectionModeDropdown; // ← For Edit Set

    [Header("Map Pan/Zoom")]
    [SerializeField] private MapPanZoomController mapPanZoom;

    // ── Private State ─────────────────────────────────────────────────────
    private GameManager gameManager;
    private LocationManager locationManager;
    private DatabaseReference dbRef;

    private bool gpsReady = false;
    private Dictionary<string, TreasureSetData> loadedSets = new();

    private List<TreasureManagerGPS_Multiplayer.TreasureData> workingPins = new();
    private List<GameObject> previewPinObjects = new();
    private Dictionary<string, List<GameObject>> setMapPins = new();

    private string editingSetId = null;

    

    // ─────────────────────────────────────────────────────────────────────

    #region Unity Lifecycle

    private void Start()
    {
        gameManager = GameManager.Instance;
        locationManager = LocationManager.Instance;

        if (gameManager == null) { Debug.LogError("[CreatorMapController] No GameManager!"); return; }

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

        // Wire existing buttons
        newButton.onClick.AddListener(OnNewClicked);
        editButton.onClick.AddListener(OnEditClicked);
        deleteButton.onClick.AddListener(OnDeleteClicked);
        backButton.onClick.AddListener(() => gameManager.ReturnToMenu());

        // Wire upload buttons
        if (uploadButton != null)
        {
            uploadButton.onClick.RemoveAllListeners();
            uploadButton.onClick.AddListener(OnUploadLevelClicked);
        }

        if (uploadConfirmButton != null)
        {
            uploadConfirmButton.onClick.RemoveAllListeners();
            uploadConfirmButton.onClick.AddListener(UploadSetAsLevel);
        }

        if (uploadCancelButton != null)
        {
            uploadCancelButton.onClick.RemoveAllListeners();
            uploadCancelButton.onClick.AddListener(CloseUploadPanel);
        }

        if (setSelectionDropdown != null)
        {
            setSelectionDropdown.onValueChanged.RemoveAllListeners();
            setSelectionDropdown.onValueChanged.AddListener(OnSetSelected);
        }

        // Wire treasure editor buttons
        if (closeTreasureEditorButton != null)
        {
            closeTreasureEditorButton.onClick.RemoveAllListeners();
            closeTreasureEditorButton.onClick.AddListener(CloseTreasureEditor);
        }

        if (saveTreasureButton != null)
        {
            saveTreasureButton.onClick.RemoveAllListeners();
            saveTreasureButton.onClick.AddListener(SaveTreasureChanges);
        }

        if (deleteTreasureButton != null)
        {
            deleteTreasureButton.onClick.RemoveAllListeners();
            deleteTreasureButton.onClick.AddListener(DeleteTreasure);
        }

        // Wire checkpoint editor buttons
        if (openCheckpointEditorButton != null)
        {
            openCheckpointEditorButton.onClick.RemoveAllListeners();
            openCheckpointEditorButton.onClick.AddListener(OpenCheckpointEditor);
        }

        if (saveCheckpointEditsButton != null)
        {
            saveCheckpointEditsButton.onClick.RemoveAllListeners();
            saveCheckpointEditsButton.onClick.AddListener(SaveCheckpointEditsToSet);
        }

        // Wire checkpoint editor close button
        if (checkpointEditor != null)
        {
            checkpointEditor.SetOnCloseCallback(OnCheckpointEditorClosed);
        }

        if (newCollectionModeDropdown != null)
        {
            newCollectionModeDropdown.ClearOptions();
            newCollectionModeDropdown.AddOptions(new List<string> { "Free Order", "In Order" });
            newCollectionModeDropdown.value = 0;
        }

        // Setup Edit Set collection mode
        if (editCollectionModeDropdown != null)
        {
            editCollectionModeDropdown.ClearOptions();
            editCollectionModeDropdown.AddOptions(new List<string> { "Free Order", "In Order" });
            editCollectionModeDropdown.value = 0;
        }

        if (mapPanZoom == null)
            mapPanZoom = mapContainer.GetComponent<MapPanZoomController>();

        if (mapPanZoom == null)
        {
            mapPanZoom = mapContainer.gameObject.AddComponent<MapPanZoomController>();
            Debug.Log("[CreatorMapController] Added MapPanZoomController for Android");
        }

        newPlacePinButton.onClick.AddListener(PlacePinAtCurrentLocation);
        newSaveButton.onClick.AddListener(SaveNewSet);
        newCancelButton.onClick.AddListener(CloseAllPanels);

        editPlacePinButton.onClick.AddListener(PlacePinAtCurrentLocation);
        editSaveButton.onClick.AddListener(SaveEditedSet);
        editCancelButton.onClick.AddListener(CloseAllPanels);

        confirmDeleteButton.onClick.AddListener(ConfirmDelete);
        deleteCancelButton.onClick.AddListener(CloseAllPanels);

        CloseAllPanels();
        SetStatus("Acquiring GPS...");
        StartCoroutine(WaitForGPSThenInit());
    }

    private void Update()
    {
        if (gpsReady && playerMarker != null)
            playerMarker.anchoredPosition = Vector2.zero;
    }

    private void OnDestroy()
    {
        if (locationManager != null)
            locationManager.OnLocationUpdated -= OnLocationUpdated;

        CancelInvoke(nameof(CloseUploadPanel));
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region GPS & Map Init

    private IEnumerator WaitForGPSThenInit()
    {
        while (locationManager == null || locationManager.Status != LocationManager.LocationStatus.Ready)
        {
            SetStatus($"Acquiring GPS... ({locationManager?.Status})");
            yield return new WaitForSeconds(0.5f);
        }

        gpsReady = true;
        SetStatus("GPS Ready ✓");

        double lat = locationManager.Latitude;
        double lon = locationManager.Longitude;

        tileLoader.CenterMapOn(lat, lon);
        locationManager.OnLocationUpdated += OnLocationUpdated;
        LoadAllSetsOntoMap();
    }

    private void OnLocationUpdated(double lat, double lon)
    {
        Debug.Log($"[CreatorMapController] Location updated: ({lat:F6}, {lon:F6})");

        // Center map on new location
        tileLoader.CenterMapOn(lat, lon);

        // Refresh pins if sets are loaded
        if (loadedSets.Count > 0)
        {
            RefreshAllPinsOnMap();
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region READ — Load Sets onto Map

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
                if (task.IsFaulted) { SetStatus("Error loading sets."); return; }

                ClearAllMapPins();
                loadedSets.Clear();

                foreach (var child in task.Result.Children)
                {
                    var set = JsonUtility.FromJson<TreasureSetData>(child.GetRawJsonValue());
                    set.setId = child.Key;
                    loadedSets[set.setId] = set;
                    // ← Don't spawn pins here anymore
                }

                // ← Setup dropdown FIRST (this will trigger OnSetSelected)
                SetupSetSelectionDropdown();

                SetStatus(loadedSets.Count == 0
                    ? "No sets yet. Tap [New] to create one."
                    : $"{loadedSets.Count} set(s) loaded. GPS Ready ✓");
            });
    }





    /// <summary>
    /// Spawn pins for a specific set on the map
    /// </summary>
    private void SpawnPinsForSet(TreasureSetData set)
    {
        if (set == null || set.treasures == null || set.treasures.Count == 0)
        {
            Debug.LogWarning("[CreatorMapController] Set has no treasures");
            return;
        }

        if (tileLoader == null || mapContainer == null)
        {
            Debug.LogError("[CreatorMapController] tileLoader or mapContainer is null");
            return;
        }

        setMapPins[set.setId] = new List<GameObject>();

        foreach (var treasure in set.treasures)
        {
            // ← Use tileLoader.GpsToWorldPosition()
            Vector3 worldPos = tileLoader.GpsToWorldPosition(treasure.lat, treasure.lon);

            GameObject pin = Instantiate(pinPrefab, mapContainer);
            pin.name = $"Pin_{treasure.name}";

            RectTransform pinRect = pin.GetComponent<RectTransform>();
            if (pinRect != null)
            {
                pin.transform.position = worldPos;
                Debug.Log($"[CreatorMapController] Spawned pin '{treasure.name}' at world pos: {worldPos}");
            }

            var label = pin.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.text = treasure.name;

            var button = pin.GetComponent<Button>();
            if (button != null)
            {
                int treasureIndex = set.treasures.IndexOf(treasure);
                button.onClick.AddListener(() => OnTreasurePinClicked(set, treasureIndex));
            }

            setMapPins[set.setId].Add(pin);
        }

        Debug.Log($"[CreatorMapController] Spawned {setMapPins[set.setId].Count} pins for set: {set.setName}");
    }

    

    /// <summary>
    /// Called when user clicks on a treasure pin
    /// </summary>
    private void OnTreasurePinClicked(TreasureSetData set, int treasureIndex)
    {
        if (treasureIndex < 0 || treasureIndex >= set.treasures.Count)
            return;

        var treasure = set.treasures[treasureIndex];
        Debug.Log($"[CreatorMapController] Clicked treasure: {treasure.name}");

        // ← Open treasure editor or challenge config
        if (challengeConfig != null)
        {
            challengeConfig.Show(treasureIndex, treasure.challenge, (index, challenge) =>
            {
                set.treasures[index].challenge = challenge;
                Debug.Log($"[CreatorMapController] Challenge saved for {treasure.name}");
            });
        }
    }

    private void RefreshAllPinsOnMap()
    {
        Debug.Log($"[CreatorMapController] Refreshing pins. Sets loaded: {loadedSets.Count}");

        // If we're in selection mode, refresh the selected set
        if (setSelectionDropdown != null && setSelectionDropdown.interactable)
        {
            int selectedIndex = setSelectionDropdown.value;
            OnSetSelected(selectedIndex);
        }
        // If we're editing, refresh the current edit
        else if (!string.IsNullOrEmpty(editingSetId) && loadedSets.ContainsKey(editingSetId))
        {
            ClearAllMapPins();
            SpawnPinsForSet(loadedSets[editingSetId]);
        }
    }

    private void ClearAllMapPins()
    {
        foreach (var pinList in setMapPins.Values)
        {
            foreach (var pin in pinList)
            {
                if (pin != null)
                {
                    Destroy(pin);
                }
            }
        }
        setMapPins.Clear();
        Debug.Log("[CreatorMapController] Cleared all map pins");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region CREATE — New Set

    private void OnNewClicked()
    {
        workingPins.Clear();
        ClearPreviewPins();
        newSetNameInput.text = "";
        UpdateNewPinCountText();
        newSetPanel.SetActive(true);
        editSetPanel.SetActive(false);
        deleteSetPanel.SetActive(false);
        uploadPanelRoot.SetActive(false);
    }

    private void SaveNewSet()
    {
        int mode = newCollectionModeDropdown != null ? newCollectionModeDropdown.value : 0;
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
            collectionMode = mode,
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

    private void OnEditClicked()
    {
        if (loadedSets.Count == 0) { SetStatus("No sets to edit. Create one first."); return; }

        editSetDropdown.ClearOptions();
        var options = new List<string>();
        foreach (var set in loadedSets.Values) options.Add(set.setName);
        editSetDropdown.AddOptions(options);

        editSetDropdown.onValueChanged.RemoveAllListeners();
        editSetDropdown.onValueChanged.AddListener(idx => LoadSetIntoEditPanel(GetSetByDropdownIndex(idx)));

        editSetPanel.SetActive(true);
        newSetPanel.SetActive(false);
        deleteSetPanel.SetActive(false);
        uploadPanelRoot.SetActive(false);

        LoadSetIntoEditPanel(GetSetByDropdownIndex(0));
    }

    private void LoadSetIntoEditPanel(TreasureSetData set)
    {
        if (set == null) return;

        editingSetId = set.setId;
        editSetNameInput.text = set.setName;

        workingPins = new List<TreasureManagerGPS_Multiplayer.TreasureData>(set.treasures);
        ClearPreviewPins();
        RedrawPreviewPins();
        UpdateEditPinCountText();

        if (setMapPins.ContainsKey(set.setId))
        {
            foreach (var p in setMapPins[set.setId]) Destroy(p);
            setMapPins.Remove(set.setId);
        }

        if (editCollectionModeDropdown != null)
            editCollectionModeDropdown.value = Mathf.Clamp(set.collectionMode, 0, 1);

        SetStatus($"Editing '{set.setName}'. Tap a pin to set its challenge, or place new pins.");

        if (workingPins.Count > 0 && challengeConfig != null)
            challengeConfig.Show(0, workingPins[0].challenge, OnChallengeSaved);
    }

    private void SaveEditedSet()
    {
        int mode = editCollectionModeDropdown != null ? editCollectionModeDropdown.value : 0;
        if (string.IsNullOrEmpty(editingSetId)) return;

        string setName = editSetNameInput.text.Trim();
        if (string.IsNullOrEmpty(setName)) { SetStatus("Please enter a set name."); return; }
        if (workingPins.Count == 0) { SetStatus("A set must have at least one pin."); return; }

        editSaveButton.interactable = false;
        SetStatus("Saving changes...");

        var existingSet = loadedSets[editingSetId];
        var updatedSet = new TreasureSetData
        {
            setId = editingSetId,
            setName = existingSet.setName,
            createdBy = existingSet.createdBy,
            collectionMode = mode,
            treasures = new List<TreasureManagerGPS_Multiplayer.TreasureData>(workingPins)
        };

        dbRef.Child("treasureSets").Child(editingSetId)
        .SetRawJsonValueAsync(JsonUtility.ToJson(updatedSet))
                .ContinueWithOnMainThread(task =>
            {
                editSaveButton.interactable = true;
                if (task.IsFaulted) { SetStatus("Update failed."); return; }

                loadedSets[editingSetId] = updatedSet;

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

    /// <summary>
    /// Open treasure editor to modify a specific checkpoint
    /// </summary>
    private void OpenTreasureEditor(int treasureIndex)
    {
        if (treasureIndex < 0 || treasureIndex >= workingPins.Count)
        {
            SetStatus("Invalid treasure selected");
            return;
        }

        selectedTreasureIndex = treasureIndex;
        var treasure = workingPins[treasureIndex];

        if (treasureEditorPanel != null)
            treasureEditorPanel.SetActive(true);

        if (treasureNameInput != null)
            treasureNameInput.text = treasure.name;

        if (treasureLocationText != null)
            treasureLocationText.text = $"Location: ({treasure.lat:F4}, {treasure.lon:F4})\nPoints: {treasure.points}";

        SetStatus($"Editing treasure #{treasureIndex + 1}: {treasure.name}");
        Debug.Log($"[CreatorMapController] Opened editor for treasure {treasureIndex}: {treasure.name}");
    }

    private void SaveTreasureChanges()
    {
        if (selectedTreasureIndex < 0 || selectedTreasureIndex >= workingPins.Count)
            return;

        string newName = treasureNameInput.text.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            SetStatus("Treasure name cannot be empty");
            return;
        }

        workingPins[selectedTreasureIndex].name = newName;

        // Update preview pins
        if (selectedTreasureIndex < previewPinObjects.Count)
        {
            var label = previewPinObjects[selectedTreasureIndex].GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.text = newName;
        }

        SetStatus($"✓ Treasure '{newName}' updated!");
        CloseTreasureEditor();
        Debug.Log($"[CreatorMapController] Saved treasure {selectedTreasureIndex}: {newName}");
        RefreshAllPinsOnMap(); // <-- ensure map pins also reflect changes
    }

    private void DeleteTreasure()
    {
        if (selectedTreasureIndex < 0 || selectedTreasureIndex >= workingPins.Count)
            return;

        string treasureName = workingPins[selectedTreasureIndex].name;
        workingPins.RemoveAt(selectedTreasureIndex);

        // Refresh preview pins
        RedrawPreviewPins();
        UpdateNewPinCountText();
        UpdateEditPinCountText();

        SetStatus($"✓ Treasure '{treasureName}' deleted!");
        CloseTreasureEditor();
        Debug.Log($"[CreatorMapController] Deleted treasure {selectedTreasureIndex}: {treasureName}");
    }

    private void CloseTreasureEditor()
    {
        if (treasureEditorPanel != null)
            treasureEditorPanel.SetActive(false);

        selectedTreasureIndex = -1;
        treasureNameInput.text = "";
    }

    private void OpenCheckpointEditor()
    {
        if (string.IsNullOrEmpty(editingSetId) || !loadedSets.ContainsKey(editingSetId))
        {
            SetStatus("⚠️ No set selected in Edit mode");
            return;
        }

        var selectedSet = loadedSets[editingSetId];
        workingPins = new List<TreasureManagerGPS_Multiplayer.TreasureData>(selectedSet.treasures);

        checkpointEditorPanel.SetActive(true);
        checkpointEditor.LoadCheckpoints(workingPins, OnCheckpointEditorSaved);

        SetStatus($"Editing checkpoints for '{selectedSet.setName}'");
    }

    private void OnCheckpointEditorSaved(List<TreasureManagerGPS_Multiplayer.TreasureData> editedTreasures)
    {
        workingPins = new List<TreasureManagerGPS_Multiplayer.TreasureData>(editedTreasures);
        RefreshAllPinsOnMap();
        RedrawPreviewPins();
        UpdateNewPinCountText();
        UpdateEditPinCountText();

        SetStatus($"✓ {workingPins.Count} checkpoints updated");
        Debug.Log("[CreatorMapController] Checkpoints updated from editor");

        SaveCheckpointEditsToSet(); // <-- required for Firebase persistence
    }

    private void OnCheckpointEditorClosed()
    {
        // Get the edited treasures from checkpoint editor
        workingPins = checkpointEditor.GetEditedTreasures();
        RedrawPreviewPins();
        UpdateNewPinCountText();
        UpdateEditPinCountText();

        // Make sure editor panel is hidden
        if (checkpointEditorPanel != null)
            checkpointEditorPanel.SetActive(false);

        SetStatus($"✓ Updated {workingPins.Count} checkpoints");
        Debug.Log("[CreatorMapController] Checkpoint editor closed, treasures updated");
    }

    private void SaveCheckpointEditsToSet()
    {
        Debug.Log($"[SaveCheckpointEditsToSet] editingSetId={editingSetId}");
        Debug.Log($"[SaveCheckpointEditsToSet] loadedSets has key? {loadedSets.ContainsKey(editingSetId)}");
        Debug.Log($"[SaveCheckpointEditsToSet] workingPins count={workingPins?.Count ?? 0}");
        if (loadedSets.ContainsKey(editingSetId))
        {
            Debug.Log($"[SaveCheckpointEditsToSet] target setName={loadedSets[editingSetId].setName}");
        }

        if (string.IsNullOrEmpty(editingSetId) || !loadedSets.ContainsKey(editingSetId))
        {
            SetStatus("⚠️ No set selected");
            return;
        }

        if (workingPins == null || workingPins.Count == 0)
        {
            SetStatus("⚠️ No checkpoints to save");
            return;
        }

        saveCheckpointEditsButton.interactable = false;
        SetStatus("Saving checkpoints to Firebase...");

        var existingSet = loadedSets[editingSetId];
        var updatedSet = new TreasureSetData
        {
            setId = editingSetId,
            setName = existingSet.setName,      // keep current set name
            createdBy = existingSet.createdBy,  // keep owner
            collectionMode = existingSet.collectionMode, // keep collection mode
            treasures = new List<TreasureManagerGPS_Multiplayer.TreasureData>(workingPins)
        };

        dbRef.Child("treasureSets").Child(editingSetId)
            .SetRawJsonValueAsync(JsonUtility.ToJson(updatedSet))
            .ContinueWithOnMainThread(task =>
            {
                saveCheckpointEditsButton.interactable = true;

                if (task.IsFaulted)
                {
                    SetStatus("❌ Firebase update failed");
                    Debug.LogError("[CreatorMapController] " + task.Exception);
                    return;
                }

                loadedSets[editingSetId] = updatedSet;

                if (setMapPins.ContainsKey(editingSetId))
                {
                    foreach (var p in setMapPins[editingSetId]) Destroy(p);
                    setMapPins.Remove(editingSetId);
                }
                SpawnPinsForSet(updatedSet);

                SetStatus($"✅ Saved {workingPins.Count} checkpoints!");
                Debug.Log($"[CreatorMapController] ✅ Replaced treasure set {editingSetId} with updated checkpoints");
            });
    }



    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region DELETE — Remove Set

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
        uploadPanelRoot.SetActive(false);
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
    #region UPLOAD — Upload Set as Level

    private void OnUploadLevelClicked()
    {
        var selectedSet = GetCurrentlySelectedSet();

        if (selectedSet == null || selectedSet.treasures.Count == 0)
        {
            SetStatus("Select or create a set with treasures to upload.");
            return;
        }

        if (uploadPanelRoot != null)
        {
            uploadPanelRoot.SetActive(true);
            levelNameInput.text = selectedSet.setName;
            levelDescriptionInput.text = "";
        }

        newSetPanel.SetActive(false);
        editSetPanel.SetActive(false);
        deleteSetPanel.SetActive(false);

        Debug.Log($"[CreatorMapController] Opening upload panel for set: {selectedSet.setName}");
    }

    private TreasureSetData GetCurrentlySelectedSet()
    {
        // Priority 1: Currently editing a set
        if (!string.IsNullOrEmpty(editingSetId) && loadedSets.ContainsKey(editingSetId))
        {
            return loadedSets[editingSetId];
        }

        // Priority 2: Currently creating new treasures
        if (workingPins.Count > 0)
        {
            return new TreasureSetData
            {
                setName = newSetNameInput?.text ?? "Unnamed Set",
                treasures = new List<TreasureManagerGPS_Multiplayer.TreasureData>(workingPins)
            };
        }

        // Priority 3: Get set from dropdown selection
        if (setSelectionDropdown != null && loadedSets.Count > 0)
        {
            var selectedSet = GetSetByDropdownIndex(setSelectionDropdown.value);
            if (selectedSet != null)
                return selectedSet;
        }

        return null;
    }

    public void UploadSetAsLevel()
    {
        string levelName = levelNameInput?.text?.Trim() ?? "";
        string description = levelDescriptionInput?.text?.Trim() ?? "";

        if (string.IsNullOrEmpty(levelName))
        {
            ShowUploadFeedback("Enter a level name.");
            return;
        }

        var selectedSet = GetCurrentlySelectedSet();
        if (selectedSet == null || selectedSet.treasures == null || selectedSet.treasures.Count == 0)
        {
            ShowUploadFeedback("No treasures to upload.");
            return;
        }

        if (uploadConfirmButton != null)
            uploadConfirmButton.interactable = false;

        ShowUploadFeedback("Uploading level...");

        // Re-upload edited set if already linked, else create new level.
        UpsertLevelFromSet(selectedSet, levelName, description);
    }

    private void UpsertLevelFromSet(TreasureSetData selectedSet, string levelName, string description)
    {
        var user = FirebaseAuth.DefaultInstance?.CurrentUser;
        if (user == null)
        {
            ShowUploadFeedback("Not signed in.");
            if (uploadConfirmButton != null) uploadConfirmButton.interactable = true;
            return;
        }

        bool isUpdate = !string.IsNullOrEmpty(selectedSet.linkedLevelId);
        string levelId = isUpdate ? selectedSet.linkedLevelId : dbRef.Child("levels").Push().Key;
        bool isInOrder = selectedSet.collectionMode == (int)CollectionMode.InOrder;

        var levelData = new Dictionary<string, object>
    {
        { "levelId", levelId },
        { "name", levelName },
        { "description", description },
        { "createdBy", user.UserId },
        { "creatorName", user.DisplayName ?? "Anonymous" },
        { "collectionMode", selectedSet.collectionMode == 1 ? "In Order" : "Free Order" },
        { "createdAt", isUpdate ? (object)ServerValue.Timestamp : ServerValue.Timestamp },
        { "updatedAt", ServerValue.Timestamp },
        { "treasureCount", selectedSet.treasures.Count },
        { "plays", 0 },
        { "rating", 0 },
        { "difficulty", "Medium" }
    };

        // 1) Save level metadata
        dbRef.Child("levels").Child(levelId).UpdateChildrenAsync(levelData).ContinueWithOnMainThread(metaTask =>
        {
            if (metaTask.IsFaulted || metaTask.IsCanceled)
            {
                ShowUploadFeedback("Failed to upload level metadata.");
                Debug.LogError("[CreatorMapController] Metadata upsert failed: " + metaTask.Exception);
                if (uploadConfirmButton != null) uploadConfirmButton.interactable = true;
                return;
            }

            // 2) Clear old treasures so edited set fully replaces old content
            dbRef.Child("levels").Child(levelId).Child("treasures").RemoveValueAsync().ContinueWithOnMainThread(clearTask =>
            {
                if (clearTask.IsFaulted || clearTask.IsCanceled)
                {
                    ShowUploadFeedback("Failed to clear old treasures.");
                    Debug.LogError("[CreatorMapController] Clear treasures failed: " + clearTask.Exception);
                    if (uploadConfirmButton != null) uploadConfirmButton.interactable = true;
                    return;
                }

                // 3) Upload current treasures
                UploadTreasuresToLevel(levelId, selectedSet.treasures, isInOrder);

                // 4) Persist link set -> level for future re-uploads
                if (string.IsNullOrEmpty(selectedSet.setId))
                    return;

                selectedSet.linkedLevelId = levelId;
                loadedSets[selectedSet.setId] = selectedSet;

                dbRef.Child("treasureSets").Child(selectedSet.setId).Child("linkedLevelId")
                    .SetValueAsync(levelId)
                    .ContinueWithOnMainThread(linkTask =>
                    {
                        if (linkTask.IsFaulted)
                            Debug.LogWarning("[CreatorMapController] linkedLevelId save failed: " + linkTask.Exception);
                    });
            });
        });
    }



    private void UploadTreasuresToLevel(
    string levelId,
    List<TreasureManagerGPS_Multiplayer.TreasureData> treasures,
    bool isInOrder)
    {
        dbRef.Child("levels").Child(levelId).Child("treasures")
            .GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    ShowUploadFeedback("Error reading existing treasures");
                    Debug.LogError("[CreatorMapController] Error reading treasures: " + task.Exception);
                    if (uploadConfirmButton != null) uploadConfirmButton.interactable = true;
                    return;
                }

                var existingKeys = new Dictionary<int, string>();
                int keyIndex = 0;

                if (task.Result != null && task.Result.Exists)
                {
                    foreach (var snapshot in task.Result.Children)
                    {
                        existingKeys[keyIndex] = snapshot.Key;
                        keyIndex++;
                    }
                }

                int uploadedCount = 0;
                int totalCount = treasures?.Count ?? 0;

                if (totalCount == 0)
                {
                    ShowUploadFeedback("No treasures to upload.");
                    if (uploadConfirmButton != null) uploadConfirmButton.interactable = true;
                    return;
                }

                for (int i = 0; i < totalCount; i++)
                {
                    var treasure = treasures[i];

                    if (isInOrder)
                        treasures[i].orderIndex = i;

                    string treasureId = existingKeys.ContainsKey(i)
                        ? existingKeys[i]
                        : dbRef.Child("levels").Child(levelId).Child("treasures").Push().Key;

                    var treasureData = new Dictionary<string, object>
                    {
                    { "name", treasure.name ?? $"Treasure #{i + 1}" },
                    { "lat", treasure.lat },
                    { "lon", treasure.lon },
                    { "points", treasure.points }
                    };

                    if (isInOrder)
                        treasureData["orderIndex"] = treasures[i].orderIndex;

                    if (treasure.challenge != null && treasure.challenge.type != ChallengeType.None)
                    {
                        var challengeData = new Dictionary<string, object>
                        {
                        { "type", (int)treasure.challenge.type },
                        { "bonusPoints", treasure.challenge.bonusPoints },
                        { "maxAttempts", treasure.challenge.maxAttempts },
                        { "timeLimitSeconds", treasure.challenge.timeLimitSeconds },
                        { "minigameId", treasure.challenge.minigameId ?? "" }
                        };

                        // ← NEW: Handle both MCQ and ARMCQ
                        if (treasure.challenge.type == ChallengeType.MCQ ||
                            treasure.challenge.type == ChallengeType.ARMCQ)
                        {
                            challengeData["question"] = treasure.challenge.question ?? "";

                            var optionsData = new List<object>();
                            if (treasure.challenge.options != null)
                            {
                                foreach (var option in treasure.challenge.options)
                                {
                                    optionsData.Add(new Dictionary<string, object>
                                    {
                                    { "text", option.text ?? "" },
                                    { "isCorrect", option.isCorrect }
                                    });
                                }
                            }

                            challengeData["options"] = optionsData;

                            Debug.Log($"[UploadTreasure] Uploading {treasure.challenge.type} challenge: " +
                                      $"question='{challengeData["question"]}', " +
                                      $"options={optionsData.Count}");
                        }
                        // ← NEW: Log minigame challenges
                        else if (treasure.challenge.type == ChallengeType.MemoryMatch ||
                                 treasure.challenge.type == ChallengeType.OrderSequence)
                        {
                            Debug.Log($"[UploadTreasure] Uploading {treasure.challenge.type} challenge: " +
                                      $"minigameId='{treasure.challenge.minigameId}', " +
                                      $"timeLimit={treasure.challenge.timeLimitSeconds}s");
                        }

                        treasureData["challenge"] = challengeData;
                    }

                    dbRef.Child("levels").Child(levelId)
                        .Child("treasures").Child(treasureId)
                        .SetValueAsync(treasureData)
                        .ContinueWithOnMainThread(uploadTask =>
                        {
                            uploadedCount++;

                            if (uploadTask.IsFaulted || uploadTask.IsCanceled)
                            {
                                Debug.LogError("[CreatorMapController] Treasure upload failed: " + uploadTask.Exception);
                            }
                            else
                            {
                                Debug.Log($"[CreatorMapController] Uploaded treasure {uploadedCount}/{totalCount} (key: {treasureId})");
                            }

                            if (uploadProgressBar != null)
                                uploadProgressBar.value = (float)uploadedCount / totalCount;

                            if (uploadedCount >= totalCount)
                            {
                                ShowUploadFeedback($"Level '{levelNameInput.text}' uploaded successfully!");
                                Debug.Log($"[CreatorMapController] All treasures uploaded for level: {levelId}");

                                Invoke(nameof(CloseUploadPanel), 2f);
                                if (uploadConfirmButton != null) uploadConfirmButton.interactable = true;
                            }
                        });
                }
            });
    }

    private void CloseUploadPanel()
    {
        if (uploadPanelRoot != null)
            uploadPanelRoot.SetActive(false);

        levelNameInput.text = "";
        levelDescriptionInput.text = "";

        Debug.Log("[CreatorMapController] Upload panel closed");
    }

    private void ShowUploadFeedback(string message)
    {
        if (uploadFeedbackText != null)
            uploadFeedbackText.text = message;

        Debug.Log("[CreatorMapController] " + message);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region SET SELECTION — Dropdown for choosing sets

    /// <summary>
    /// Setup the set selection dropdown with all loaded sets
    /// </summary>
    private void SetupSetSelectionDropdown()
    {
        if (setSelectionDropdown == null) return;

        setSelectionDropdown.ClearOptions();
        var options = new List<string>();

        foreach (var set in loadedSets.Values)
        {
            options.Add($"{set.setName} ({set.treasures.Count} treasures)");
        }

        if (options.Count == 0)
        {
            setSelectionDropdown.AddOptions(new List<string> { "No sets available" });
            setSelectionDropdown.interactable = false;
            return;
        }

        setSelectionDropdown.AddOptions(options);
        setSelectionDropdown.interactable = true;

        // ← Unsubscribe first to avoid duplicate listeners
        setSelectionDropdown.onValueChanged.RemoveAllListeners();

        // ← Subscribe to dropdown changes
        setSelectionDropdown.onValueChanged.AddListener(OnSetSelected);

        // ← Load first set by default
        OnSetSelected(0);

        Debug.Log($"[CreatorMapController] Setup dropdown with {options.Count} sets");
    }

    /// <summary>
    /// Called when user selects a set from dropdown
    /// </summary>
    private void OnSetSelected(int index)
    {
        var selectedSet = GetSetByDropdownIndex(index);
        if (selectedSet == null) return;

        Debug.Log($"[CreatorMapController] Set selected: {selectedSet.setName}");

        // ← Clear old pins first
        ClearAllMapPins();

        // ← Spawn pins for the selected set
        SpawnPinsForSet(selectedSet);

        SetStatus($"Selected: {selectedSet.setName} ({selectedSet.treasures.Count} treasures)");
    }

    #endregion


    // ─────────────────────────────────────────────────────────────────────
    #region Shared Pin Placement (New & Edit)

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

        UpdateNewPinCountText();
        UpdateEditPinCountText();

        int newPinIndex = workingPins.Count - 1;
        if (challengeConfig != null)
            challengeConfig.Show(newPinIndex, workingPins[newPinIndex].challenge, OnChallengeSaved);
    }

    private void SpawnPreviewPin(TreasureManagerGPS_Multiplayer.TreasureData treasure)
    {
        // ← Use tileLoader.GpsToWorldPosition()
        Vector3 worldPos = tileLoader.GpsToWorldPosition(treasure.lat, treasure.lon);

        GameObject pin = Instantiate(pinPrefab, mapContainer);
        pin.name = $"PreviewPin_{treasure.name}";
        pin.transform.position = worldPos;

        var label = pin.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = treasure.name;

        var img = pin.GetComponent<Image>();
        if (img != null) img.color = Color.yellow;

        var btn = pin.GetComponent<Button>() ?? pin.AddComponent<Button>();
        int capturedIndex = workingPins.Count - 1;
        btn.onClick.AddListener(() =>
        {
            if (challengeConfig != null)
                challengeConfig.Show(capturedIndex, workingPins[capturedIndex].challenge, OnChallengeSaved);
        });

        var longPressHandler = pin.AddComponent<LongPressHandler>();
        longPressHandler.OnLongPress += () => OpenTreasureEditor(capturedIndex);

        previewPinObjects.Add(pin);
        UpdatePinBadge(pin, treasure.challenge);

        Debug.Log($"[CreatorMapController] Spawned preview pin '{treasure.name}' at world pos: {worldPos}");
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

    public class LongPressHandler : MonoBehaviour
    {
        private float holdTime = 0f;
        private const float LONG_PRESS_TIME = 0.5f;

        public delegate void LongPressDelegate();
        public event LongPressDelegate OnLongPress;

        private void Update()
        {
            if (Input.GetMouseButton(0))
            {
                holdTime += Time.deltaTime;
                if (holdTime >= LONG_PRESS_TIME)
                {
                    OnLongPress?.Invoke();
                    holdTime = 0f;
                }
            }
            else if (Input.GetMouseButtonUp(0))
            {
                holdTime = 0f;
            }
        }
    }

    

    private void CloseAllPanels()
    {
        newSetPanel.SetActive(false);
        editSetPanel.SetActive(false);
        deleteSetPanel.SetActive(false);
        uploadPanelRoot.SetActive(false);
        workingPins.Clear();
        ClearPreviewPins();
        editingSetId = null;
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log("[CreatorMapController] " + msg);
    }

    private void UpdatePinBadge(GameObject pin, ChallengeData challenge)
    {
        var label = pin.GetComponentInChildren<TMP_Text>();
        if (label == null) return;

        string badge = challenge?.type switch
        {
            ChallengeType.MCQ => " ❓",
            ChallengeType.MemoryMatch => " 🎮",
            ChallengeType.OrderSequence => " 🎮",
            _ => ""
        };

        var treasure = workingPins[previewPinObjects.IndexOf(pin)];
        label.text = treasure.name + badge;
    }

    private void UpdateNewPinCountText()
    {
        if (newPinCountText == null)
        {
            Debug.LogWarning("[CreatorMapController] newPinCountText is not assigned in the Inspector!");
            return;
        }
        newPinCountText.text = $"Pins placed: {workingPins.Count}";
    }

    private void UpdateEditPinCountText()
    {
        if (editPinCountText == null)
        {
            Debug.LogWarning("[CreatorMapController] editPinCountText is not assigned in the Inspector!");
            return;
        }
        editPinCountText.text = $"Pins placed: {workingPins.Count}";
    }

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

    private void OnChallengeSaved(int pinIndex, ChallengeData challengeData)
    {
        if (pinIndex < 0 || pinIndex >= workingPins.Count) return;

        workingPins[pinIndex].challenge = challengeData;

        if (pinIndex < previewPinObjects.Count)
            UpdatePinBadge(previewPinObjects[pinIndex], challengeData);

        UpdateNewPinCountText();
        UpdateEditPinCountText();

        Debug.Log($"[CreatorMapController] Challenge saved for pin {pinIndex}: {challengeData?.type}");
    }

    #endregion
}