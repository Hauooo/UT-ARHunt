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

    // ── Top-Right Buttons ─────────────────────────────────────────────────
    [Header("Top Right Buttons")]
    [SerializeField] private Button newButton;
    [SerializeField] private Button editButton;
    [SerializeField] private Button deleteButton;
    [SerializeField] private Button uploadButton;  // ← MOVED HERE
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

    // ── Challenge Panel ───────────────────────────────────────────────────
    [Header("Challenge Config")]
    [SerializeField] private ChallengeConfigController challengeConfig;

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
        tileLoader.CenterMapOn(lat, lon);
        RefreshAllPinsOnMap();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region READ — Load All Sets onto Map

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

        SetStatus($"Editing '{set.setName}'. Tap a pin to set its challenge, or place new pins.");

        if (workingPins.Count > 0 && challengeConfig != null)
            challengeConfig.Show(0, workingPins[0].challenge, OnChallengeSaved);
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
        if (!string.IsNullOrEmpty(editingSetId) && loadedSets.ContainsKey(editingSetId))
        {
            return loadedSets[editingSetId];
        }

        if (workingPins.Count > 0)
        {
            return new TreasureSetData
            {
                setName = newSetNameInput?.text ?? "Unnamed Set",
                treasures = new List<TreasureManagerGPS_Multiplayer.TreasureData>(workingPins)
            };
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
        if (selectedSet == null || selectedSet.treasures.Count == 0)
        {
            ShowUploadFeedback("No treasures to upload.");
            return;
        }

        if (uploadConfirmButton != null)
            uploadConfirmButton.interactable = false;

        ShowUploadFeedback("Uploading level...");
        UploadLevelToFirebase(levelName, description, selectedSet.treasures);
    }

    private void UploadLevelToFirebase(string levelName, string description, List<TreasureManagerGPS_Multiplayer.TreasureData> treasures)
    {
        var user = FirebaseAuth.DefaultInstance?.CurrentUser;
        if (user == null)
        {
            ShowUploadFeedback("Not signed in.");
            if (uploadConfirmButton != null) uploadConfirmButton.interactable = true;
            return;
        }

        string levelId = dbRef.Child("levels").Push().Key;

        var levelData = new Dictionary<string, object>
        {
            { "levelId", levelId },
            { "name", levelName },
            { "description", description },
            { "createdBy", user.UserId },
            { "creatorName", user.DisplayName ?? "Anonymous" },
            { "createdAt", ServerValue.Timestamp },
            { "treasureCount", treasures.Count },
            { "plays", 0 },
            { "rating", 0 },
            { "difficulty", "Medium" }
        };

        dbRef.Child("levels").Child(levelId)
             .SetValueAsync(levelData)
             .ContinueWithOnMainThread(task =>
             {
                 if (task.IsFaulted || task.IsCanceled)
                 {
                     ShowUploadFeedback("Failed to upload level.");
                     Debug.LogError("[CreatorMapController] Upload failed: " + task.Exception);
                     if (uploadConfirmButton != null) uploadConfirmButton.interactable = true;
                     return;
                 }

                 Debug.Log($"[CreatorMapController] Level metadata uploaded: {levelId}");
                 UploadTreasuresToLevel(levelId, treasures);
             });
    }

    private void UploadTreasuresToLevel(string levelId, List<TreasureManagerGPS_Multiplayer.TreasureData> treasures)
    {
        int uploadedCount = 0;
        int totalCount = treasures.Count;

        Debug.Log($"[CreatorMapController] Starting to upload {totalCount} treasures");

        foreach (var treasure in treasures)
        {
            string treasureId = dbRef.Child("levels").Child(levelId).Child("treasures").Push().Key;

            var treasureData = new Dictionary<string, object>
            {
                { "name", treasure.name },
                { "lat", treasure.lat },
                { "lon", treasure.lon },
                { "points", treasure.points }
            };

            dbRef.Child("levels").Child(levelId)
                 .Child("treasures").Child(treasureId)
                 .SetValueAsync(treasureData)
                 .ContinueWithOnMainThread(task =>
                 {
                     uploadedCount++;

                     if (task.IsFaulted)
                     {
                         Debug.LogError("[CreatorMapController] Treasure upload failed: " + task.Exception);
                     }
                     else
                     {
                         Debug.Log($"[CreatorMapController] Uploaded treasure {uploadedCount}/{totalCount}");
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
        float tileSize = mapContainer.rect.width / tileLoader.tileGridSize;
        Vector2 offset = tileLoader.GpsToPixelOffset(treasure.lat, treasure.lon, tileSize);
        GameObject pin = Instantiate(pinPrefab, pinsLayer);
        pin.GetComponent<RectTransform>().anchoredPosition = offset;

        var label = pin.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = treasure.name;

        var img = pin.GetComponent<Image>();
        if (img != null) img.color = Color.yellow;

        var btn = pin.GetComponent<Button>() ?? pin.AddComponent<Button>();
        int capturedIndex = workingPins.Count - 1;
        btn.onClick.AddListener(() =>
        {
            challengeConfig.Show(
                capturedIndex,
                workingPins[capturedIndex].challenge,
                OnChallengeSaved
            );
        });

        previewPinObjects.Add(pin);
        UpdatePinBadge(pin, treasure.challenge);
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