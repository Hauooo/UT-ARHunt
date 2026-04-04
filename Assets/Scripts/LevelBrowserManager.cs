using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Allows players to browse and play uploaded levels
/// Sorted by proximity to user's location
/// </summary>
public class LevelBrowserManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject levelBrowserPanel;
    [SerializeField] private Transform levelListContent;
    [SerializeField] private GameObject levelItemPrefab;
    [SerializeField] private GameObject pinPrefab;
    [SerializeField] private Button refreshButton;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private Button backButton;

    [Header("Firebase")]
    [SerializeField]
    private string firebaseDatabaseUrl =
        "https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/";

    [Header("Preview Panel")]
    [SerializeField] private GameObject previewPanel;
    [SerializeField] private OSMTileLoader previewTileLoader;
    [SerializeField] private RectTransform previewMapContainer;
    [SerializeField] private GameObject previewPinPrefab;
    [SerializeField] private RectTransform previewPinsLayer;
    [SerializeField] private Button playLevelButton;
    [SerializeField] private Button closePreviewButton;
    [SerializeField] private TMP_Text previewLevelNameText;
    [SerializeField] private TMP_Text previewTreasureCountText;


    private DatabaseReference dbRef;
    private LocationManager locationManager;
    private Dictionary<string, LevelData> availableLevels = new Dictionary<string, LevelData>();
    private bool isFirebaseReady = false;
    private string selectedLevelId;

    [System.Serializable]
    public class LevelData
    {
        public string levelId;
        public string name;
        public string description;
        public string creatorName;
        public int treasureCount;
        public int plays;
        public int rating;
        public string difficulty;
        public double firstTreasureLat;
        public double firstTreasureLon;
    }

    private void Start()
    {
        locationManager = LocationManager.Instance;
        SetupButtons();
        SetupLevelListLayout();
        InitializeFirebaseWithDelay();
    }

    private void SetupButtons()
    {
        if (refreshButton != null)
        {
            refreshButton.onClick.RemoveAllListeners();
            refreshButton.onClick.AddListener(LoadLevels);
            Debug.Log("[LevelBrowser] Refresh button setup");
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(ClosePanel);
            Debug.Log("[LevelBrowser] Back button setup");
        }

        if (playLevelButton != null)
        {
            playLevelButton.onClick.RemoveAllListeners();
            playLevelButton.onClick.AddListener(() =>
            {
                if (selectedLevelId != null)
                {
                    ClosePreview();
                    OnPlayLevelClicked(selectedLevelId);
                }
            });
            Debug.Log("[LevelBrowser] Play button setup");
        }

        if (closePreviewButton != null)
        {
            closePreviewButton.onClick.RemoveAllListeners();
            closePreviewButton.onClick.AddListener(ClosePreview);
            Debug.Log("[LevelBrowser] Close preview button setup");
        }
    }

    private void InitializeFirebaseWithDelay()
    {
        Invoke(nameof(InitializeFirebase), 0.5f);
    }

    private void InitializeFirebase()
    {
        try
        {
            dbRef = FirebaseDatabase.GetInstance(firebaseDatabaseUrl).RootReference;
            isFirebaseReady = true;
            Debug.Log("[LevelBrowser] Firebase initialized successfully");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[LevelBrowser] Failed to init Firebase: " + ex);
            isFirebaseReady = false;
        }
    }

    public void OpenBrowser()
    {
        if (levelBrowserPanel != null)
            levelBrowserPanel.SetActive(true);

        if (!isFirebaseReady)
        {
            Debug.LogWarning("[LevelBrowser] Firebase not ready yet. Waiting...");
            Invoke(nameof(LoadLevels), 1f);
            return;
        }

        LoadLevels();
        Debug.Log("[LevelBrowser] Level browser opened");
    }

    public void ClosePanel()
    {
        if (levelBrowserPanel != null)
            levelBrowserPanel.SetActive(false);

        Debug.Log("[LevelBrowser] Level browser closed");
    }

    private void SetupLevelListLayout()
    {
        if (levelListContent == null)
        {
            Debug.LogError("[LevelBrowser] levelListContent is not assigned!");
            return;
        }

        VerticalLayoutGroup layoutGroup = levelListContent.GetComponent<VerticalLayoutGroup>();
        if (layoutGroup == null)
        {
            layoutGroup = levelListContent.gameObject.AddComponent<VerticalLayoutGroup>();
            Debug.Log("[LevelBrowser] Added VerticalLayoutGroup to levelListContent");
        }

        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childForceExpandWidth = true;
        layoutGroup.spacing = 15f;
        layoutGroup.padding = new RectOffset(10, 10, 10, 10);

        Debug.Log("[LevelBrowser] Level list layout configured");
    }

    private void LoadLevels()
    {
        if (!isFirebaseReady || dbRef == null)
        {
            ShowFeedback("Firebase not initialized. Please try again.");
            Debug.LogError("[LevelBrowser] Firebase not ready!");
            return;
        }

        ShowFeedback("Loading levels...");
        Debug.Log("[LevelBrowser] Starting to load levels...");

        foreach (Transform child in levelListContent)
        {
            Debug.Log($"[LevelBrowser] Destroying old item: {child.name}");
            Destroy(child.gameObject);
        }

        availableLevels.Clear();

        dbRef.Child("levels")
             .GetValueAsync()
             .ContinueWithOnMainThread(task =>
             {
                 if (task.IsFaulted)
                 {
                     ShowFeedback("Failed to load levels. Check your connection.");
                     Debug.LogError("[LevelBrowser] Error: " + task.Exception);
                     return;
                 }

                 if (!task.Result.Exists)
                 {
                     ShowFeedback("No levels available yet. Be the first to upload!");
                     Debug.Log("[LevelBrowser] No levels in database");
                     return;
                 }

                 // Get user's current location
                 double userLat = locationManager?.Latitude ?? 0;
                 double userLon = locationManager?.Longitude ?? 0;
                 bool hasUserLocation = userLat != 0 && userLon != 0;

                 Debug.Log($"[LevelBrowser] User location: ({userLat:F4}, {userLon:F4}), HasLocation: {hasUserLocation}");

                 var levelDataList = new List<(LevelData data, double distance)>();
                 int levelCount = 0;

                 foreach (var levelSnapshot in task.Result.Children)
                 {
                     try
                     {
                         var levelData = ParseLevelData(levelSnapshot);
                         if (levelData != null)
                         {
                             availableLevels[levelSnapshot.Key] = levelData;

                             // Calculate distance to first treasure
                             double distance = double.MaxValue;
                             if (hasUserLocation && levelData.firstTreasureLat != 0 && levelData.firstTreasureLon != 0)
                             {
                                 distance = CalculateDistance(userLat, userLon, levelData.firstTreasureLat, levelData.firstTreasureLon);
                                 Debug.Log($"[LevelBrowser] Level '{levelData.name}' - Distance: {distance:F1}m");
                             }
                             else if (!hasUserLocation)
                             {
                                 Debug.LogWarning("[LevelBrowser] No user location available - cannot calculate distance");
                             }

                             levelDataList.Add((levelData, distance));
                             levelCount++;
                         }
                     }
                     catch (System.Exception ex)
                     {
                         Debug.LogWarning("[LevelBrowser] Error parsing level: " + ex);
                     }
                 }

                 // Sort by distance (closest first)
                 levelDataList.Sort((a, b) => a.distance.CompareTo(b.distance));

                 // Create level items in sorted order
                 foreach (var (levelData, distance) in levelDataList)
                 {
                     CreateLevelItem(levelData, distance);
                 }

                 ShowFeedback($"Loaded {levelCount} levels - sorted by proximity");
                 Debug.Log($"[LevelBrowser] Successfully loaded and sorted {levelCount} levels by distance");
             });
    }


    /// <summary>
    /// Open preview panel showing where the level starts
    /// </summary>
    private void ShowLevelPreview(LevelData levelData)
    {
        if (previewPanel == null)
        {
            Debug.LogError("[LevelBrowser] previewPanel not assigned!");
            return;
        }

        previewPanel.SetActive(true);

        // Update preview header
        if (previewLevelNameText != null)
            previewLevelNameText.text = levelData.name;

        if (previewTreasureCountText != null)
            previewTreasureCountText.text = $"{levelData.treasureCount} treasures • {levelData.difficulty}";

        // Clear old preview pins
        if (previewPinsLayer != null)
        {
            foreach (Transform child in previewPinsLayer)
                Destroy(child.gameObject);
        }

        ShowFeedback($"Loading preview for: {levelData.name}");
        Debug.Log($"[LevelBrowser] Opening preview for: {levelData.name}");

        dbRef.Child("levels").Child(levelData.levelId).Child("treasures")
             .GetValueAsync()
             .ContinueWithOnMainThread(task =>
             {
                 if (task.IsFaulted || !task.Result.Exists)
                 {
                     ShowFeedback("No treasures to preview");
                     Debug.LogWarning("[LevelBrowser] No treasures in preview");
                     return;
                 }

                 // Get first treasure as start location
                 double startLat = 0;
                 double startLon = 0;
                 bool foundFirst = false;
                 int treasureCount = 0;

                 foreach (var treasureSnapshot in task.Result.Children)
                 {
                     try
                     {
                         double lat = double.TryParse(treasureSnapshot.Child("lat").Value?.ToString(), out double l) ? l : 0;
                         double lon = double.TryParse(treasureSnapshot.Child("lon").Value?.ToString(), out double lo) ? lo : 0;
                         string name = treasureSnapshot.Child("name").Value?.ToString() ?? "Treasure";

                         if (!foundFirst)
                         {
                             startLat = lat;
                             startLon = lon;
                             foundFirst = true;
                         }

                         treasureCount++;
                     }
                     catch (System.Exception ex)
                     {
                         Debug.LogWarning("[LevelBrowser] Error parsing treasure in preview: " + ex);
                     }
                 }

                 if (foundFirst && previewTileLoader != null && previewMapContainer != null && previewPinsLayer != null)
                 {
                     // Center preview map on start location
                     previewTileLoader.CenterMapOn(startLat, startLon);
                     Debug.Log($"[LevelBrowser] Preview map centered on: ({startLat:F4}, {startLon:F4})");

                     // Spawn preview pins
                     float tileSize = previewMapContainer.rect.width / previewTileLoader.tileGridSize;
                     int pinCount = 0;

                     foreach (var treasureSnapshot in task.Result.Children)
                     {
                         try
                         {
                             double lat = double.TryParse(treasureSnapshot.Child("lat").Value?.ToString(), out double l) ? l : 0;
                             double lon = double.TryParse(treasureSnapshot.Child("lon").Value?.ToString(), out double lo) ? lo : 0;
                             string name = treasureSnapshot.Child("name").Value?.ToString() ?? "Treasure";

                             Vector2 offset = previewTileLoader.GpsToPixelOffset(lat, lon, tileSize);

                             GameObject pin = Instantiate(previewPinPrefab ?? pinPrefab, previewPinsLayer);
                             RectTransform pinRect = pin.GetComponent<RectTransform>();
                             pinRect.anchoredPosition = offset;
                             pinRect.sizeDelta = new Vector2(50, 50);

                             var label = pin.GetComponentInChildren<TMP_Text>();
                             if (label != null)
                             {
                                 label.text = name;
                                 label.color = Color.white;
                             }

                             // First treasure is GREEN (START), others are BLUE
                             var img = pin.GetComponent<Image>();
                             if (img != null)
                             {
                                 if (pinCount == 0)
                                     img.color = new Color(0f, 1f, 0f, 1f); // Green - START
                                 else
                                     img.color = new Color(0f, 0.5f, 1f, 1f); // Blue - Other treasures
                             }

                             pinCount++;
                             Debug.Log($"[LevelBrowser] Preview pin {pinCount}: {name} (Green=Start, Blue=Others)");
                         }
                         catch (System.Exception ex)
                         {
                             Debug.LogWarning("[LevelBrowser] Error spawning preview pin: " + ex);
                         }
                     }

                     ShowFeedback($"Preview ready: {treasureCount} treasures (Green = Start)");
                     Debug.Log($"[LevelBrowser] Preview loaded with {pinCount} pins");
                 }
             });
    }

    private void ClosePreview()
    {
        if (previewPanel != null)
            previewPanel.SetActive(false);

        Debug.Log("[LevelBrowser] Preview panel closed");
    }

    private LevelData ParseLevelData(DataSnapshot snapshot)
    {
        if (!snapshot.Exists) return null;

        try
        {
            // Get first treasure location for distance calculation
            double firstTreasureLat = 0;
            double firstTreasureLon = 0;

            if (snapshot.HasChild("treasures"))
            {
                var treasures = snapshot.Child("treasures").Children;
                var firstTreasure = treasures.FirstOrDefault();
                if (firstTreasure != null)
                {
                    firstTreasureLat = double.TryParse(firstTreasure.Child("lat").Value?.ToString(), out double lat) ? lat : 0;
                    firstTreasureLon = double.TryParse(firstTreasure.Child("lon").Value?.ToString(), out double lon) ? lon : 0;
                }
            }

            var level = new LevelData
            {
                levelId = snapshot.Key,
                name = snapshot.HasChild("name") ? snapshot.Child("name").Value?.ToString() ?? "Unknown" : "Unknown",
                description = snapshot.HasChild("description") ? snapshot.Child("description").Value?.ToString() ?? "" : "",
                creatorName = snapshot.HasChild("creatorName") ? snapshot.Child("creatorName").Value?.ToString() ?? "Anonymous" : "Anonymous",
                treasureCount = snapshot.HasChild("treasureCount") ? int.TryParse(snapshot.Child("treasureCount").Value?.ToString(), out int count) ? count : 0 : 0,
                plays = snapshot.HasChild("plays") ? int.TryParse(snapshot.Child("plays").Value?.ToString(), out int plays) ? plays : 0 : 0,
                rating = snapshot.HasChild("rating") ? int.TryParse(snapshot.Child("rating").Value?.ToString(), out int rating) ? rating : 0 : 0,
                difficulty = snapshot.HasChild("difficulty") ? snapshot.Child("difficulty").Value?.ToString() ?? "Medium" : "Medium",
                firstTreasureLat = firstTreasureLat,
                firstTreasureLon = firstTreasureLon
            };

            Debug.Log($"[LevelBrowser] Parsed level: {level.name} at ({firstTreasureLat:F4}, {firstTreasureLon:F4})");
            return level;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[LevelBrowser] Error parsing level data: " + ex);
            return null;
        }
    }

    private void CreateLevelItem(LevelData levelData, double distance = double.MaxValue)
    {
        if (levelItemPrefab == null)
        {
            Debug.LogError("[LevelBrowser] Level item prefab not assigned!");
            return;
        }

        if (levelListContent == null)
        {
            Debug.LogError("[LevelBrowser] Level list content not assigned!");
            return;
        }

        GameObject itemObj = Instantiate(levelItemPrefab, levelListContent);

        LayoutElement layoutElement = itemObj.GetComponent<LayoutElement>();
        if (layoutElement == null)
            layoutElement = itemObj.AddComponent<LayoutElement>();

        layoutElement.preferredHeight = 120f;
        layoutElement.flexibleWidth = 1f;

        LevelBrowserItem itemController = itemObj.GetComponent<LevelBrowserItem>();

        if (itemController != null)
        {
            // When item is clicked, show preview instead of playing directly
            itemController.Setup(levelData, (levelId) =>
            {
                selectedLevelId = levelId;
                ShowLevelPreview(levelData);
            }, distance);
            Debug.Log($"[LevelBrowser] Setup item for level: {levelData.name}");
        }
        else
        {
            Debug.LogError("[LevelBrowser] LevelBrowserItem script not found on prefab!");
        }
    }

    /// <summary>
    /// Calculate distance between two GPS coordinates using Haversine formula
    /// </summary>
    private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadius = 6371000; // meters

        double dLat = (lat2 - lat1) * Mathf.Deg2Rad;
        double dLon = (lon2 - lon1) * Mathf.Deg2Rad;

        double a = Mathf.Sin((float)(dLat / 2)) * Mathf.Sin((float)(dLat / 2)) +
                   Mathf.Cos((float)(lat1 * Mathf.Deg2Rad)) * Mathf.Cos((float)(lat2 * Mathf.Deg2Rad)) *
                   Mathf.Sin((float)(dLon / 2)) * Mathf.Sin((float)(dLon / 2));

        double c = 2 * Mathf.Atan2(Mathf.Sqrt((float)a), Mathf.Sqrt((float)(1 - a)));
        double distanceInMeters = earthRadius * c;

        return distanceInMeters;
    }

    private void OnPlayLevelClicked(string levelId)
    {
        Debug.Log($"[LevelBrowser] Play button clicked for level: {levelId}");

        if (availableLevels.ContainsKey(levelId))
        {
            var levelData = availableLevels[levelId];

            PlayerPrefs.SetString("SelectedLevelId", levelId);
            PlayerPrefs.SetString("SelectedLevelName", levelData.name);
            PlayerPrefs.Save();

            ShowFeedback($"Loading '{levelData.name}'...");
            Debug.Log($"[LevelBrowser] Level selected: {levelData.name}. Saved to PlayerPrefs.");

            StartCoroutine(LoadLevelTreasuresAndStart(levelId, levelData.name));
        }
        else
        {
            ShowFeedback("Error: Level not found");
            Debug.LogError("[LevelBrowser] Level not found in dictionary");
        }
    }

    private System.Collections.IEnumerator LoadLevelTreasuresAndStart(string levelId, string levelName)
    {
        yield return new WaitForSeconds(1f);

        dbRef.Child("levels").Child(levelId).Child("treasures")
             .GetValueAsync()
             .ContinueWithOnMainThread(task =>
             {
                 if (task.IsFaulted)
                 {
                     ShowFeedback("Failed to load level treasures.");
                     Debug.LogError("[LevelBrowser] Error loading treasures: " + task.Exception);
                     return;
                 }

                 if (!task.Result.Exists)
                 {
                     ShowFeedback("This level has no treasures!");
                     Debug.LogWarning("[LevelBrowser] No treasures found for level: " + levelId);
                     return;
                 }

                 var treasures = new List<TreasureManagerGPS_Multiplayer.TreasureData>();
                 var treasureKeys = new Dictionary<TreasureManagerGPS_Multiplayer.TreasureData, string>();

                 foreach (var treasureSnapshot in task.Result.Children)
                 {
                     try
                     {
                         var treasure = ParseTreasure(treasureSnapshot);
                         if (treasure != null)
                         {
                             treasures.Add(treasure);
                             treasureKeys[treasure] = treasureSnapshot.Key;
                             Debug.Log($"[LevelBrowser] ✓ Loaded treasure '{treasure.name}' with Firebase key: {treasureSnapshot.Key}");
                         }
                     }
                     catch (System.Exception ex)
                     {
                         Debug.LogWarning("[LevelBrowser] Error parsing treasure: " + ex);
                     }
                 }

                 Debug.Log($"[LevelBrowser] Total treasureKeys mapped: {treasureKeys.Count}");

                 var gameManager = GameManager.Instance;
                 if (gameManager != null)
                 {
                     gameManager.SetGameModeForLevel(levelId, levelName, treasures);
                     gameManager.TreasureKeys = treasureKeys;
                     Debug.Log($"[LevelBrowser] ✓ Passed {treasureKeys.Count} treasure key mappings to GameManager");
                 }
                 else
                 {
                     Debug.LogError("[LevelBrowser] GameManager not found!");
                 }

                 ShowFeedback($"Starting '{levelName}'...");
                 UnityEngine.SceneManagement.SceneManager.LoadScene("ARScene");
             });
    }

    private TreasureManagerGPS_Multiplayer.TreasureData ParseTreasure(Firebase.Database.DataSnapshot snapshot)
    {
        if (!snapshot.Exists) return null;

        try
        {
            var treasure = new TreasureManagerGPS_Multiplayer.TreasureData
            {
                name = snapshot.HasChild("name") ? snapshot.Child("name").Value?.ToString() ?? "Treasure" : "Treasure",
                lat = snapshot.HasChild("lat") ? double.Parse(snapshot.Child("lat").Value?.ToString() ?? "0") : 0,
                lon = snapshot.HasChild("lon") ? double.Parse(snapshot.Child("lon").Value?.ToString() ?? "0") : 0,
                points = snapshot.HasChild("points") ? int.Parse(snapshot.Child("points").Value?.ToString() ?? "100") : 100
            };

            if (snapshot.HasChild("challenge"))
            {
                var challengeSnapshot = snapshot.Child("challenge");

                var challengeType = (ChallengeType)int.Parse(challengeSnapshot.Child("type").Value?.ToString() ?? "0");

                var challenge = new ChallengeData
                {
                    type = challengeType,
                    bonusPoints = int.Parse(challengeSnapshot.Child("bonusPoints").Value?.ToString() ?? "0"),
                    maxAttempts = int.Parse(challengeSnapshot.Child("maxAttempts").Value?.ToString() ?? "1"),
                    timeLimitSeconds = int.Parse(challengeSnapshot.Child("timeLimitSeconds").Value?.ToString() ?? "60"),
                    minigameId = challengeSnapshot.Child("minigameId").Value?.ToString() ?? ""
                };

                if (challengeType == ChallengeType.MCQ && challengeSnapshot.HasChild("options"))
                {
                    challenge.question = challengeSnapshot.Child("question").Value?.ToString() ?? "";
                    challenge.options = new List<MCQOption>();

                    foreach (var optionSnapshot in challengeSnapshot.Child("options").Children)
                    {
                        challenge.options.Add(new MCQOption
                        {
                            text = optionSnapshot.Child("text").Value?.ToString() ?? "",
                            isCorrect = bool.Parse(optionSnapshot.Child("isCorrect").Value?.ToString() ?? "false")
                        });
                    }
                }

                treasure.challenge = challenge;
                Debug.Log($"[LevelBrowser] Parsed challenge for treasure: {treasure.name} (type: {challengeType})");
            }

            return treasure;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[LevelBrowser] Error parsing treasure: " + ex);
            return null;
        }
    }

    private void ShowFeedback(string message)
    {
        if (feedbackText != null)
        {
            feedbackText.text = message;
            Debug.Log("[LevelBrowser] Feedback: " + message);
        }
        else
        {
            Debug.LogWarning("[LevelBrowser] Feedback text not assigned!");
        }
    }
}