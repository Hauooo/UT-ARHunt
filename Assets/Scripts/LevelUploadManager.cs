using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;
using Firebase.Auth;
using System.Collections.Generic;

/// <summary>
/// Allows creators to upload treasure levels to Firebase
/// </summary>
public class LevelUploadManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject uploadPanelRoot;
    [SerializeField] private TMP_InputField levelNameInput;
    [SerializeField] private TMP_InputField levelDescriptionInput;
    [SerializeField] private Button uploadButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private Scrollbar progressBar;
    [SerializeField] private TMP_Text treasureCountText;

    [Header("Firebase")]
    [SerializeField]
    private string firebaseDatabaseUrl =
        "https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/";

    private DatabaseReference dbRef;
    private List<TreasureManagerGPS_Multiplayer.TreasureData> treasuresToUpload;
    private bool isFirebaseReady = false;

    private void Start()
    {
        InitializeFirebaseWithDelay();
        SetupButtons();
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
            Debug.Log("[LevelUpload] Firebase initialized successfully");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[LevelUpload] Failed to init Firebase: " + ex);
            isFirebaseReady = false;
        }
    }

    private void SetupButtons()
    {
        if (uploadButton != null)
        {
            uploadButton.onClick.RemoveAllListeners();
            uploadButton.onClick.AddListener(OnUploadClicked);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(OnCancelClicked);
        }
    }

    /// <summary>
    /// Open upload panel with treasures to upload
    /// </summary>
    public void OpenUploadPanel(List<TreasureManagerGPS_Multiplayer.TreasureData> treasures)
    {
        if (treasures == null || treasures.Count == 0)
        {
            ShowFeedback("No treasures to upload!");
            return;
        }

        treasuresToUpload = new List<TreasureManagerGPS_Multiplayer.TreasureData>(treasures);
        levelNameInput.text = "";
        levelDescriptionInput.text = "";
        feedbackText.text = "";

        if (treasureCountText != null)
            treasureCountText.text = $"Treasures to upload: {treasures.Count}";

        if (uploadPanelRoot != null)
            uploadPanelRoot.SetActive(true);

        Debug.Log($"[LevelUpload] Upload panel opened with {treasures.Count} treasures");
    }

    public void CloseUploadPanel()
    {
        if (uploadPanelRoot != null)
            uploadPanelRoot.SetActive(false);

        treasuresToUpload = null;
        Debug.Log("[LevelUpload] Upload panel closed");
    }

    private void OnUploadClicked()
    {
        if (!isFirebaseReady || dbRef == null)
        {
            ShowFeedback("Firebase not ready. Please try again.");
            Debug.LogError("[LevelUpload] Firebase not initialized");
            return;
        }

        string levelName = levelNameInput != null ? levelNameInput.text.Trim() : "";
        string description = levelDescriptionInput != null ? levelDescriptionInput.text.Trim() : "";

        if (string.IsNullOrEmpty(levelName))
        {
            ShowFeedback("Please enter a level name");
            return;
        }

        if (treasuresToUpload == null || treasuresToUpload.Count == 0)
        {
            ShowFeedback("No treasures to upload");
            return;
        }

        uploadButton.interactable = false;
        ShowFeedback("Uploading level...");
        Debug.Log($"[LevelUpload] Starting upload: {levelName}");

        UploadLevelToFirebase(levelName, description);
    }

    private void UploadLevelToFirebase(string levelName, string description)
    {
        var user = FirebaseAuth.DefaultInstance?.CurrentUser;
        if (user == null)
        {
            ShowFeedback("Not signed in");
            uploadButton.interactable = true;
            return;
        }

        // Create new level entry
        string levelId = dbRef.Child("levels").Push().Key;

        var levelData = new Dictionary<string, object>
        {
            { "levelId", levelId },
            { "name", levelName },
            { "description", description },
            { "createdBy", user.UserId },
            { "creatorName", user.DisplayName ?? "Anonymous" },
            { "createdAt", ServerValue.Timestamp },
            { "treasureCount", treasuresToUpload.Count },
            { "plays", 0 },
            { "rating", 0 },
            { "difficulty", "Medium" }
        };

        // Upload level metadata
        dbRef.Child("levels").Child(levelId)
             .SetValueAsync(levelData)
             .ContinueWithOnMainThread(task =>
             {
                 if (task.IsFaulted || task.IsCanceled)
                 {
                     ShowFeedback("Failed to upload level");
                     Debug.LogError("[LevelUpload] Upload failed: " + task.Exception);
                     uploadButton.interactable = true;
                     return;
                 }

                 Debug.Log($"[LevelUpload] Level metadata uploaded: {levelId}");
                 // Upload treasures for this level
                 UploadTreasuresToLevel(levelId);
             });
    }

    private void UploadTreasuresToLevel(string levelId)
    {
        int uploadedCount = 0;
        int totalCount = treasuresToUpload.Count;

        Debug.Log($"[LevelUpload] Starting to upload {totalCount} treasures");

        foreach (var treasure in treasuresToUpload)
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
                         Debug.LogError("[LevelUpload] Treasure upload failed: " + task.Exception);
                     }
                     else
                     {
                         Debug.Log($"[LevelUpload] Uploaded treasure {uploadedCount}/{totalCount}");
                     }

                     // Update progress
                     if (progressBar != null)
                         progressBar.value = (float)uploadedCount / totalCount;

                     // When all treasures uploaded, show success
                     if (uploadedCount >= totalCount)
                     {
                         string message = $"Level '{treasuresToUpload[0].name}' uploaded successfully!";
                         ShowFeedback(message);
                         Debug.Log($"[LevelUpload] {message}");
                         Invoke(nameof(CloseUploadPanel), 2f);
                         uploadButton.interactable = true;
                     }
                 });
        }
    }

    private void OnCancelClicked()
    {
        CloseUploadPanel();
    }

    private void ShowFeedback(string message)
    {
        if (feedbackText != null)
        {
            feedbackText.text = message;
            feedbackText.gameObject.SetActive(true);
        }

        Debug.Log("[LevelUpload] " + message);
    }
}