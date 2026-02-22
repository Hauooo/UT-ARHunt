using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;
using System.Collections.Generic;

public class HostLobbyController : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject setListItemPrefab;
    [SerializeField] private Transform contentParent;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text feedbackText;

    [Header("Firebase")]
    [SerializeField]
    private string firebaseDatabaseUrl =
        "https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/";

    private DatabaseReference dbRef;
    private AuthManager authManager;

    private Dictionary<string, TreasureSetData> availableSets = new Dictionary<string, TreasureSetData>();
    private TreasureSetData selectedSet;

    private void Awake()
    {
        authManager = AuthManager.Instance;

        try
        {
            dbRef = FirebaseDatabase.GetInstance(firebaseDatabaseUrl).RootReference;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[HostLobbyController] Failed to init FirebaseDatabase: " + ex);
            dbRef = null;
        }
    }

    void Start()
    {
        hostButton.onClick.AddListener(OnHostButtonClicked);
        hostButton.interactable = false;
        feedbackText.text = "Loading your treasure sets...";
    }

    private void OnEnable()
    {
        FetchPlayerTreasureSets();
    }

    private void FetchPlayerTreasureSets()
    {
        if (feedbackText != null) feedbackText.text = "Loading your treasure sets...";

        if (authManager == null)
        {
            Debug.LogError("[HostLobbyController] AuthManager is null.");
            if (feedbackText != null) feedbackText.text = "Auth not ready.";
            return;
        }

        if (dbRef == null)
        {
            Debug.LogError("[HostLobbyController] dbRef is null (Firebase DB not configured).");
            if (feedbackText != null) feedbackText.text = "Database not ready.";
            return;
        }

        // Clear previous list items
        foreach (Transform child in contentParent) Destroy(child.gameObject);
        availableSets.Clear();
        selectedSet = null;
        hostButton.interactable = false;

        string myUserId = authManager.UserId;
        if (string.IsNullOrEmpty(myUserId))
        {
            Debug.LogWarning("[HostLobbyController] UserId empty; waiting for sign-in.");
            if (feedbackText != null) feedbackText.text = "Signing in... please wait.";
            return;
        }

        dbRef.Child("treasureSets")
            .OrderByChild("createdBy")
            .EqualTo(myUserId)
            .GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    if (feedbackText != null) feedbackText.text = "Error loading sets.";
                    Debug.LogError("Error fetching treasure sets: " + task.Exception);
                    return;
                }

                DataSnapshot snapshot = task.Result;
                if (!snapshot.Exists)
                {
                    if (feedbackText != null) feedbackText.text = "You haven't created any treasure sets yet!";
                    return;
                }

                PopulateSetList(snapshot);
            });
    }

    private void PopulateSetList(DataSnapshot snapshot)
    {
        feedbackText.text = "Choose a set to host:";

        foreach (var childSnapshot in snapshot.Children)
        {
            string json = childSnapshot.GetRawJsonValue();
            TreasureSetData setData = JsonUtility.FromJson<TreasureSetData>(json);

            availableSets[setData.setId] = setData;

            GameObject listItem = Instantiate(setListItemPrefab, contentParent);
            listItem.GetComponentInChildren<TMP_Text>().text = setData.setName;

            listItem.GetComponent<Button>().onClick.AddListener(() => SelectSet(setData));
        }
    }

    private void SelectSet(TreasureSetData setData)
    {
        selectedSet = setData;
        hostButton.interactable = true;
        feedbackText.text = $"Selected: {setData.setName}";
    }

    private void OnHostButtonClicked()
    {
        if (selectedSet == null)
        {
            feedbackText.text = "Please select a set first.";
            return;
        }

        GameManager.Instance.HostNewRoom(selectedSet);

        // Don't force-disable the panel here unless you really want to.
        // MenuManager will switch panels on OnLobbyReady anyway.
        // gameObject.SetActive(false);
    }
}