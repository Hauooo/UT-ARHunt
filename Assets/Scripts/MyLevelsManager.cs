using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;
using System.Collections.Generic;

/// <summary>
/// Manage user's created levels from MenuScene
/// Display OSM map with treasure locations
/// </summary>
public class MyLevelsManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform levelsListContent;
    [SerializeField] private GameObject levelItemPrefab;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text feedbackText;

    [Header("OSM Map")]
    [SerializeField] private OSMTileLoader tileLoader;
    [SerializeField] private RectTransform mapContainer;
    [SerializeField] private GameObject pinPrefab;
    [SerializeField] private RectTransform pinsLayer;
    [SerializeField] private RectTransform playerMarker;

    [Header("Level Details")]
    [SerializeField] private TMP_Text levelNameText;
    [SerializeField] private TMP_Text treasureCountText;
    [SerializeField] private TMP_Text difficultyText;
    [SerializeField] private TMP_Text creatorText;

    [Header("Action Buttons")]
    [SerializeField] private Button editButton;
    [SerializeField] private Button deleteButton;

    [Header("Edit Panel")]
    [SerializeField] private GameObject editPanel;
    [SerializeField] private TMP_InputField levelNameInput;
    [SerializeField] private TMP_InputField descriptionInput;
    [SerializeField] private TMP_Dropdown difficultyDropdown;
    [SerializeField] private Button editSaveButton;
    [SerializeField] private Button editCancelButton;

    [Header("Delete Confirmation")]
    [SerializeField] private GameObject deleteConfirmPanel;
    [SerializeField] private TMP_Text deleteConfirmText;
    [SerializeField] private Button deleteConfirmButton;
    [SerializeField] private Button deleteCancelButton;

    [Header("Firebase")]
    [SerializeField]
    private string firebaseDatabaseUrl =
        "https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/";

    [Header("Menu Integration")]
    [SerializeField] private MenuManager menuManager;
    [SerializeField] private GameObject mainMenuPanel;

    private DatabaseReference dbRef;
    private AuthManager authManager;
    private LocationManager locationManager;
    private string selectedLevelId;
    private LevelData selectedLevelData;
    private Dictionary<string, LevelData> userLevels = new Dictionary<string, LevelData>();
    private Dictionary<string, List<GameObject>> levelPins = new Dictionary<string, List<GameObject>>();

    [System.Serializable]
    public class LevelData
    {
        public string levelId;
        public string name;
        public string description;
        public string difficulty;
        public string creatorName;
        public int treasureCount;
        public int plays;
        public int rating;
    }

    private void Start()
    {
        if (authManager == null)
            authManager = FindObjectOfType<AuthManager>();

        if (menuManager == null)
            menuManager = FindObjectOfType<MenuManager>();

        locationManager = LocationManager.Instance;

        SetupButtons();
        SetupLayout();

        // Initialize Firebase with delay
        Invoke(nameof(InitializeFirebase), 0.5f);
    }

    private void InitializeFirebase()
    {
        try
        {
            dbRef = FirebaseDatabase.GetInstance(firebaseDatabaseUrl).RootReference;
            Debug.Log("[MyLevelsManager] Firebase initialized ✓");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[MyLevelsManager] Firebase init failed: " + ex);
            // Retry after 1 second
            Invoke(nameof(InitializeFirebase), 1f);
        }
    }

    private void SetupButtons()
    {
        if (backButton != null)
            backButton.onClick.AddListener(OnBackButton);

        if (editButton != null)
            editButton.onClick.AddListener(() => OpenEditPanel(selectedLevelData));

        if (deleteButton != null)
            deleteButton.onClick.AddListener(() => OpenDeleteConfirm(selectedLevelData));

        if (editSaveButton != null)
            editSaveButton.onClick.AddListener(SaveLevelEdit);

        if (editCancelButton != null)
            editCancelButton.onClick.AddListener(CancelEdit);

        if (deleteConfirmButton != null)
            deleteConfirmButton.onClick.AddListener(ConfirmDelete);

        if (deleteCancelButton != null)
            deleteCancelButton.onClick.AddListener(CancelDelete);

        Debug.Log("[MyLevelsManager] Buttons setup ✓");
    }

    private void SetupLayout()
    {
        if (levelsListContent == null) return;

        VerticalLayoutGroup layoutGroup = levelsListContent.GetComponent<VerticalLayoutGroup>();
        if (layoutGroup == null)
        {
            layoutGroup = levelsListContent.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childForceExpandWidth = true;
        layoutGroup.spacing = 10f;
        layoutGroup.padding = new RectOffset(10, 10, 10, 10);

        ContentSizeFitter fitter = levelsListContent.GetComponent<ContentSizeFitter>();
        if (fitter == null)
        {
            fitter = levelsListContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // Validate map setup
        if (tileLoader == null)
            Debug.LogError("[MyLevelsManager] OSMTileLoader not assigned!");

        if (mapContainer == null)
            Debug.LogError("[MyLevelsManager] mapContainer not assigned!");

        if (pinsLayer == null)
            Debug.LogError("[MyLevelsManager] pinsLayer not assigned!");

        Debug.Log("[MyLevelsManager] Layout setup ✓");
    }

    /// <summary>
    /// Open My Levels (called from MenuManager)
    /// </summary>
    public void OpenMyLevels()
    {
        if (levelsListContent == null)
        {
            Debug.LogError("[MyLevelsManager] levelsListContent is null!");
            return;
        }

        if (editPanel != null)
            editPanel.SetActive(false);

        if (deleteConfirmPanel != null)
            deleteConfirmPanel.SetActive(false);

        selectedLevelId = null;
        selectedLevelData = null;

        LoadUserLevels();
        Debug.Log("[MyLevelsManager] My Levels opened ✓");
    }

    /// <summary>
    /// Back to main menu
    /// </summary>
    private void OnBackButton()
    {
        if (menuManager != null && mainMenuPanel != null)
        {
            menuManager.ShowPanel(mainMenuPanel);
            Debug.Log("[MyLevelsManager] Back to main menu");
        }
        else
        {
            Debug.LogError("[MyLevelsManager] MenuManager or mainMenuPanel not assigned");
        }
    }

    /// <summary>
    /// Load all levels created by current user
    /// </summary>
    /// <summary>
    /// Load all levels created by current user
    /// </summary>
    private void LoadUserLevels()
    {
        if (levelsListContent == null)
        {
            Debug.LogError("[MyLevelsManager] levelsListContent is null!");
            return;
        }

        if (dbRef == null)
        {
            ShowFeedback("Initializing Firebase... Please wait");
            Debug.LogWarning("[MyLevelsManager] Firebase not ready yet, retrying in 1 second");
            Invoke(nameof(LoadUserLevels), 1f);
            return;
        }

        var currentUser = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser;
        if (currentUser == null)
        {
            ShowFeedback("Not logged in");
            Debug.LogError("[MyLevelsManager] No Firebase user");
            return;
        }

        string creatorName = currentUser.DisplayName ?? currentUser.UserId;
        ShowFeedback("Loading your levels...");
        Debug.Log($"[MyLevelsManager] Loading levels for creator: {creatorName}");

        // Clear old list
        foreach (Transform child in levelsListContent)
            Destroy(child.gameObject);

        // Clear old map pins
        ClearAllMapPins();
        userLevels.Clear();

        dbRef.Child("levels")
             .GetValueAsync()
             .ContinueWithOnMainThread(task =>
             {
                 if (task.IsFaulted)
                 {
                     ShowFeedback("Failed to load levels");
                     Debug.LogError("[MyLevelsManager] Error: " + task.Exception);
                     return;
                 }

                 if (!task.Result.Exists)
                 {
                     ShowFeedback("You haven't created any levels yet");
                     Debug.Log("[MyLevelsManager] No levels in database");
                     return;
                 }

                 int count = 0;
                 foreach (var levelSnapshot in task.Result.Children)
                 {
                     try
                     {
                         if (levelSnapshot.HasChild("creatorName"))
                         {
                             string levelCreator = levelSnapshot.Child("creatorName").Value?.ToString();
                             if (levelCreator == creatorName)
                             {
                                 var levelData = ParseLevelData(levelSnapshot);
                                 if (levelData != null)
                                 {
                                     userLevels[levelSnapshot.Key] = levelData;
                                     CreateLevelItem(levelData);
                                     count++;
                                     Debug.Log($"[MyLevelsManager] Added level: {levelData.name}");
                                 }
                             }
                         }
                     }
                     catch (System.Exception ex)
                     {
                         Debug.LogWarning("[MyLevelsManager] Error parsing level: " + ex);
                     }
                 }

                 ShowFeedback($"You have {count} level(s)");
             });
    }

    private LevelData ParseLevelData(Firebase.Database.DataSnapshot snapshot)
    {
        if (!snapshot.Exists) return null;

        try
        {
            var level = new LevelData
            {
                levelId = snapshot.Key,
                name = snapshot.HasChild("name") ? snapshot.Child("name").Value?.ToString() ?? "Unknown" : "Unknown",
                description = snapshot.HasChild("description") ? snapshot.Child("description").Value?.ToString() ?? "" : "",
                difficulty = snapshot.HasChild("difficulty") ? snapshot.Child("difficulty").Value?.ToString() ?? "Medium" : "Medium",
                creatorName = snapshot.HasChild("creatorName") ? snapshot.Child("creatorName").Value?.ToString() ?? "Unknown" : "Unknown",
                treasureCount = snapshot.HasChild("treasureCount") ? int.TryParse(snapshot.Child("treasureCount").Value?.ToString(), out int count) ? count : 0 : 0,
                plays = snapshot.HasChild("plays") ? int.TryParse(snapshot.Child("plays").Value?.ToString(), out int plays) ? plays : 0 : 0,
                rating = snapshot.HasChild("rating") ? int.TryParse(snapshot.Child("rating").Value?.ToString(), out int rating) ? rating : 0 : 0
            };

            return level;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[MyLevelsManager] Error parsing level data: " + ex);
            return null;
        }
    }

    /// <summary>
    /// Create a selectable level item in the list
    /// </summary>
    private void CreateLevelItem(LevelData levelData)
    {
        if (levelItemPrefab == null)
        {
            Debug.LogError("[MyLevelsManager] levelItemPrefab not assigned!");
            return;
        }

        if (levelsListContent == null)
        {
            Debug.LogError("[MyLevelsManager] levelsListContent is null!");
            return;
        }

        GameObject itemObj = Instantiate(levelItemPrefab, levelsListContent);

        LayoutElement layoutElement = itemObj.GetComponent<LayoutElement>();
        if (layoutElement == null)
            layoutElement = itemObj.AddComponent<LayoutElement>();

        layoutElement.preferredHeight = 100f;
        layoutElement.flexibleWidth = 1f;

        Button button = itemObj.GetComponent<Button>();
        if (button != null)
        {
            var textComponent = itemObj.GetComponentInChildren<TextMeshProUGUI>();
            if (textComponent != null)
            {
                textComponent.text = $"{levelData.name}\n({levelData.treasureCount} treasures, {levelData.difficulty})";
            }

            button.onClick.AddListener(() => SelectLevel(levelData));
        }

        Debug.Log($"[MyLevelsManager] Created item for level: {levelData.name}");
    }

    /// <summary>
    /// Select a level to view on map
    /// </summary>
    private void SelectLevel(LevelData levelData)
    {
        selectedLevelId = levelData.levelId;
        selectedLevelData = levelData;

        if (levelNameText != null)
            levelNameText.text = levelData.name;
        if (treasureCountText != null)
            treasureCountText.text = $"{levelData.treasureCount} treasures";
        if (difficultyText != null)
            difficultyText.text = $"Difficulty: {levelData.difficulty}";
        if (creatorText != null)
            creatorText.text = $"Created by: {levelData.creatorName}";

        if (editButton != null) editButton.interactable = true;
        if (deleteButton != null) deleteButton.interactable = true;

        LoadLevelOnMap(levelData.levelId);

        ShowFeedback($"Selected: {levelData.name}");
        Debug.Log($"[MyLevelsManager] Selected level: {levelData.name}");
    }

    /// <summary>
    /// Load level treasures and display on OSM map
    /// </summary>
    /// <summary>
    /// Load level treasures and display on OSM map
    /// </summary>
    /// <summary>
    /// Load level treasures and display on OSM map
    /// </summary>
    private void LoadLevelOnMap(string levelId)
    {
        if (tileLoader == null || pinsLayer == null)
        {
            Debug.LogError("[MyLevelsManager] tileLoader or pinsLayer not assigned!");
            return;
        }

        // Clear old pins
        if (levelPins.ContainsKey(levelId))
        {
            foreach (var pin in levelPins[levelId])
                Destroy(pin);
            levelPins.Remove(levelId);
        }

        if (dbRef == null) return;

        ShowFeedback("Loading map...");

        dbRef.Child("levels").Child(levelId).Child("treasures")
             .GetValueAsync()
             .ContinueWithOnMainThread(task =>
             {
                 if (task.IsFaulted || !task.Result.Exists)
                 {
                     ShowFeedback("No treasures in this level");
                     return;
                 }

                 // Get first treasure to center map
                 double centerLat = 0;
                 double centerLon = 0;
                 bool foundFirst = false;

                 foreach (var treasureSnapshot in task.Result.Children)
                 {
                     if (!foundFirst)
                     {
                         centerLat = double.TryParse(treasureSnapshot.Child("lat").Value?.ToString(), out double l) ? l : 0;
                         centerLon = double.TryParse(treasureSnapshot.Child("lon").Value?.ToString(), out double lo) ? lo : 0;
                         foundFirst = true;

                         // Center map on first treasure (same as CreatorScene)
                         if (tileLoader != null && centerLat != 0 && centerLon != 0)
                         {
                             tileLoader.CenterMapOn(centerLat, centerLon);
                             Debug.Log($"[MyLevelsManager] Map centered on: ({centerLat}, {centerLon})");
                         }
                         break;
                     }
                 }

                 // Calculate tileSize exactly like CreatorScene
                 float tileSize = mapContainer.rect.width / tileLoader.tileGridSize;

                 levelPins[levelId] = new List<GameObject>();
                 int count = 0;

                 foreach (var treasureSnapshot in task.Result.Children)
                 {
                     try
                     {
                         double lat = double.TryParse(treasureSnapshot.Child("lat").Value?.ToString(), out double l) ? l : 0;
                         double lon = double.TryParse(treasureSnapshot.Child("lon").Value?.ToString(), out double lo) ? lo : 0;
                         string name = treasureSnapshot.Child("name").Value?.ToString() ?? "Treasure";
                         int points = int.TryParse(treasureSnapshot.Child("points").Value?.ToString(), out int p) ? p : 100;

                         // Use EXACT same method as CreatorScene's SpawnPinsForSet()
                         Vector2 offset = tileLoader.GpsToPixelOffset(lat, lon, tileSize);

                         GameObject pin = Instantiate(pinPrefab, pinsLayer);
                         pin.GetComponent<RectTransform>().anchoredPosition = offset;

                         var label = pin.GetComponentInChildren<TMP_Text>();
                         if (label != null)
                             label.text = name;

                         levelPins[levelId].Add(pin);
                         count++;

                         Debug.Log($"[MyLevelsManager] Treasure '{name}' at GPS({lat:F4}, {lon:F4}) -> offset({offset.x:F0}, {offset.y:F0})");
                     }
                     catch (System.Exception ex)
                     {
                         Debug.LogWarning("[MyLevelsManager] Error parsing treasure: " + ex);
                     }
                 }

                 ShowFeedback($"Displaying {count} treasures on map");
                 Debug.Log($"[MyLevelsManager] Loaded {count} treasures using tileSize={tileSize}");
             });
    }

    /// <summary>
    /// Clear all map pins
    /// </summary>
    private void ClearAllMapPins()
    {
        foreach (var pinList in levelPins.Values)
        {
            foreach (var pin in pinList)
                Destroy(pin);
        }
        levelPins.Clear();
    }

    /// <summary>
    /// Open edit panel for selected level
    /// </summary>
    private void OpenEditPanel(LevelData levelData)
    {
        if (levelData == null || editPanel == null)
        {
            ShowFeedback("Select a level first");
            return;
        }

        selectedLevelData = levelData;
        editPanel.SetActive(true);

        levelNameInput.text = levelData.name;
        descriptionInput.text = levelData.description;

        int difficultyIndex = System.Array.IndexOf(
            new[] { "Easy", "Medium", "Hard" },
            levelData.difficulty
        );
        if (difficultyIndex >= 0)
            difficultyDropdown.value = difficultyIndex;

        ShowFeedback($"Editing: {levelData.name}");
        Debug.Log($"[MyLevelsManager] Opened edit panel for: {levelData.name}");
    }

    /// <summary>
    /// Save level edits
    /// </summary>
    private void SaveLevelEdit()
    {
        if (selectedLevelData == null)
        {
            ShowFeedback("No level selected");
            return;
        }

        ShowFeedback("Saving changes...");

        string newName = levelNameInput.text.Trim();
        string newDescription = descriptionInput.text.Trim();
        string newDifficulty = difficultyDropdown.options[difficultyDropdown.value].text;

        var updates = new Dictionary<string, object>
        {
            { "name", newName },
            { "description", newDescription },
            { "difficulty", newDifficulty }
        };

        dbRef.Child("levels").Child(selectedLevelData.levelId).UpdateChildrenAsync(updates)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    ShowFeedback("Save failed!");
                    Debug.LogError("[MyLevelsManager] Save error: " + task.Exception);
                    return;
                }

                selectedLevelData.name = newName;
                selectedLevelData.description = newDescription;
                selectedLevelData.difficulty = newDifficulty;

                ShowFeedback("✓ Level updated!");
                editPanel.SetActive(false);

                Invoke(nameof(LoadUserLevels), 1f);

                Debug.Log($"[MyLevelsManager] ✓ Level updated: {newName}");
            });
    }

    /// <summary>
    /// Cancel editing
    /// </summary>
    private void CancelEdit()
    {
        editPanel.SetActive(false);
        ShowFeedback("Edit cancelled");
    }

    /// <summary>
    /// Open delete confirmation dialog
    /// </summary>
    private void OpenDeleteConfirm(LevelData levelData)
    {
        if (levelData == null || deleteConfirmPanel == null)
        {
            ShowFeedback("Select a level first");
            return;
        }

        selectedLevelData = levelData;
        deleteConfirmPanel.SetActive(true);

        if (deleteConfirmText != null)
            deleteConfirmText.text = $"Delete '{levelData.name}'?\nThis cannot be undone.";

        Debug.Log($"[MyLevelsManager] Opened delete confirmation for: {levelData.name}");
    }

    /// <summary>
    /// Confirm and delete level
    /// </summary>
    private void ConfirmDelete()
    {
        if (selectedLevelData == null)
        {
            ShowFeedback("No level selected");
            return;
        }

        ShowFeedback($"Deleting '{selectedLevelData.name}'...");

        string levelIdToDelete = selectedLevelData.levelId;
        string levelNameToDelete = selectedLevelData.name;

        dbRef.Child("levels").Child(levelIdToDelete).RemoveValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    ShowFeedback("Delete failed!");
                    Debug.LogError("[MyLevelsManager] Delete error: " + task.Exception);
                    return;
                }

                ShowFeedback($"✓ '{levelNameToDelete}' deleted!");
                deleteConfirmPanel.SetActive(false);

                selectedLevelId = null;
                selectedLevelData = null;
                Invoke(nameof(LoadUserLevels), 1f);

                Debug.Log($"[MyLevelsManager] ✓ Level deleted: {levelNameToDelete}");
            });
    }

    /// <summary>
    /// Cancel deletion
    /// </summary>
    private void CancelDelete()
    {
        deleteConfirmPanel.SetActive(false);
        selectedLevelData = null;
        ShowFeedback("Delete cancelled");
    }

    private void ShowFeedback(string message)
    {
        if (feedbackText != null)
            feedbackText.text = message;
        Debug.Log("[MyLevelsManager] " + message);
    }
}