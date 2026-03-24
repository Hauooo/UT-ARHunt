using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;
using System.Collections.Generic;

/// <summary>
/// Allows players to browse and play uploaded levels
/// </summary>
public class LevelBrowserManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject levelBrowserPanel;
    [SerializeField] private Transform levelListContent;  // ← The parent transform where items go
    [SerializeField] private GameObject levelItemPrefab;  // ← The prefab to instantiate
    [SerializeField] private Button refreshButton;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private Button backButton;

    [Header("Firebase")]
    [SerializeField]
    private string firebaseDatabaseUrl =
        "https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/";

    private DatabaseReference dbRef;
    private Dictionary<string, LevelData> availableLevels = new Dictionary<string, LevelData>();
    private bool isFirebaseReady = false;

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
    }

    private void Start()
    {
        SetupButtons();
        InitializeFirebaseWithDelay();
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

        // Clear old list
        foreach (Transform child in levelListContent)
        {
            Debug.Log($"[LevelBrowser] Destroying old item: {child.name}");
            Destroy(child.gameObject);
        }

        availableLevels.Clear();

        // Fetch all levels
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

                 int levelCount = 0;
                 foreach (var levelSnapshot in task.Result.Children)
                 {
                     try
                     {
                         var levelData = ParseLevelData(levelSnapshot);
                         if (levelData != null)
                         {
                             availableLevels[levelSnapshot.Key] = levelData;
                             CreateLevelItem(levelData);
                             levelCount++;
                             Debug.Log($"[LevelBrowser] Added level: {levelData.name}");
                         }
                     }
                     catch (System.Exception ex)
                     {
                         Debug.LogWarning("[LevelBrowser] Error parsing level: " + ex);
                     }
                 }

                 ShowFeedback($"Loaded {levelCount} levels");
                 Debug.Log($"[LevelBrowser] Successfully loaded {levelCount} levels");
             });
    }

    private LevelData ParseLevelData(DataSnapshot snapshot)
    {
        if (!snapshot.Exists) return null;

        try
        {
            var level = new LevelData
            {
                levelId = snapshot.Key,
                name = snapshot.HasChild("name") ? snapshot.Child("name").Value?.ToString() ?? "Unknown" : "Unknown",
                description = snapshot.HasChild("description") ? snapshot.Child("description").Value?.ToString() ?? "" : "",
                creatorName = snapshot.HasChild("creatorName") ? snapshot.Child("creatorName").Value?.ToString() ?? "Anonymous" : "Anonymous",
                treasureCount = snapshot.HasChild("treasureCount") ? int.TryParse(snapshot.Child("treasureCount").Value?.ToString(), out int count) ? count : 0 : 0,
                plays = snapshot.HasChild("plays") ? int.TryParse(snapshot.Child("plays").Value?.ToString(), out int plays) ? plays : 0 : 0,
                rating = snapshot.HasChild("rating") ? int.TryParse(snapshot.Child("rating").Value?.ToString(), out int rating) ? rating : 0 : 0,
                difficulty = snapshot.HasChild("difficulty") ? snapshot.Child("difficulty").Value?.ToString() ?? "Medium" : "Medium"
            };

            Debug.Log($"[LevelBrowser] Parsed level: {level.name} (id: {level.levelId})");
            return level;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[LevelBrowser] Error parsing level data: " + ex);
            return null;
        }
    }

    private void CreateLevelItem(LevelData levelData)
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

        Debug.Log($"[LevelBrowser] Creating item for level: {levelData.name}");
        Debug.Log($"[LevelBrowser] Parent transform: {levelListContent.name}, Child count before: {levelListContent.childCount}");

        GameObject itemObj = Instantiate(levelItemPrefab, levelListContent);

        Debug.Log($"[LevelBrowser] Instantiated item, Child count after: {levelListContent.childCount}");

        LevelBrowserItem itemController = itemObj.GetComponent<LevelBrowserItem>();

        if (itemController != null)
        {
            itemController.Setup(levelData, OnPlayLevelClicked);
            Debug.Log($"[LevelBrowser] Setup item controller for level: {levelData.name}");
        }
        else
        {
            Debug.LogError("[LevelBrowser] LevelBrowserItem script not found on prefab!");
        }
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

            // ← NEW: Load treasures and start the game
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

        // Load treasures from Firebase
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

                 // ← NEW: Store treasures in GameManager
                 var gameManager = GameManager.Instance;
                 if (gameManager == null)
                 {
                     Debug.LogError("[LevelBrowser] GameManager not found!");
                     return;
                 }

                 // Parse treasures from Firebase
                 var treasures = new List<TreasureManagerGPS_Multiplayer.TreasureData>();
                 foreach (var treasureSnapshot in task.Result.Children)
                 {
                     try
                     {
                         var treasure = ParseTreasure(treasureSnapshot);
                         if (treasure != null)
                             treasures.Add(treasure);
                     }
                     catch (System.Exception ex)
                     {
                         Debug.LogWarning("[LevelBrowser] Error parsing treasure: " + ex);
                     }
                 }

                 Debug.Log($"[LevelBrowser] Loaded {treasures.Count} treasures for level: {levelName}");

                 // ← NEW: Store treasures and set game mode
                 gameManager.SetGameModeForLevel(levelId, levelName, treasures);

                 ShowFeedback($"Starting '{levelName}'...");

                 // Load AR scene
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

            // Parse challenge if it exists
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

                // Parse MCQ options ← Changed from ChallengeOption to MCQOption
                if (challengeType == ChallengeType.MCQ && challengeSnapshot.HasChild("options"))
                {
                    challenge.question = challengeSnapshot.Child("question").Value?.ToString() ?? "";
                    challenge.options = new List<MCQOption>();  // ← Changed here

                    foreach (var optionSnapshot in challengeSnapshot.Child("options").Children)
                    {
                        challenge.options.Add(new MCQOption  // ← Changed here
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