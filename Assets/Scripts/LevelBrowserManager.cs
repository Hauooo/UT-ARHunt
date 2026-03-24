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
    [SerializeField] private Transform levelListContent;
    [SerializeField] private GameObject levelItemPrefab;
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

    /// <summary>
    /// Initialize Firebase after a small delay to ensure dependencies are ready
    /// </summary>
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

        // Check if Firebase is ready before loading
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
            Destroy(child.gameObject);

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

        GameObject itemObj = Instantiate(levelItemPrefab, levelListContent);
        LevelBrowserItem itemController = itemObj.GetComponent<LevelBrowserItem>();

        if (itemController != null)
        {
            itemController.Setup(levelData, OnPlayLevelClicked);
            Debug.Log($"[LevelBrowser] Created UI item for level: {levelData.name}");
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

            // Save levelId to PlayerPrefs for the game to load
            PlayerPrefs.SetString("SelectedLevelId", levelId);
            PlayerPrefs.SetString("SelectedLevelName", levelData.name);
            PlayerPrefs.Save();

            ShowFeedback($"Loading '{levelData.name}'...");
            Debug.Log($"[LevelBrowser] Level selected: {levelData.name}. Saved to PlayerPrefs.");

            // TODO: Load game scene with this level
            // SceneManager.LoadScene("ARScene");
        }
        else
        {
            ShowFeedback("Error: Level not found");
            Debug.LogError("[LevelBrowser] Level not found in dictionary");
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