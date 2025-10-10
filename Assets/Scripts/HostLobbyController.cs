using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;
using System.Collections.Generic;

public class HostLobbyController : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject setListItemPrefab; // Your prefab with a Button and a TMP_Text
    [SerializeField] private Transform contentParent;      // The "Content" object of your ScrollView
    [SerializeField] private Button hostButton;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text feedbackText;

    private DatabaseReference dbRef;
    private AuthManager authManager;

    // Store all loaded sets so we can easily find the selected one
    private Dictionary<string, TreasureSetData> availableSets = new Dictionary<string, TreasureSetData>();
    private TreasureSetData selectedSet;

    void Start()
    {
        dbRef = FirebaseDatabase.DefaultInstance.RootReference;
        authManager = AuthManager.Instance;

        hostButton.onClick.AddListener(OnHostButtonClicked);
        // Assuming your MenuManager has a public method to show the main menu
        // backButton.onClick.AddListener(() => MenuManager.Instance.ShowPanel(MenuManager.Instance.mainMenuPanel));

        hostButton.interactable = false; // Disable until a set is chosen
        feedbackText.text = "Loading your treasure sets...";
    }

    // This is called when the panel is set to active
    private void OnEnable()
    {
        FetchPlayerTreasureSets();
    }

    private void FetchPlayerTreasureSets()
    {
        // Clear previous list items
        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }
        availableSets.Clear();

        string myUserId = authManager.UserId;
        if (string.IsNullOrEmpty(myUserId)) return;

        // Query Firebase for all treasure sets created by the current user
        dbRef.Child("treasureSets").OrderByChild("createdBy").EqualTo(myUserId).GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                feedbackText.text = "Error loading sets.";
                Debug.LogError("Error fetching treasure sets: " + task.Exception);
                return;
            }

            DataSnapshot snapshot = task.Result;
            if (!snapshot.Exists)
            {
                feedbackText.text = "You haven't created any treasure sets yet!";
                return;
            }

            // Populate the list with the fetched sets
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

            // Store it for later
            availableSets[setData.setId] = setData;

            // Create a UI item for it
            GameObject listItem = Instantiate(setListItemPrefab, contentParent);
            listItem.GetComponentInChildren<TMP_Text>().text = setData.setName;

            // Add a listener to the button on the prefab
            listItem.GetComponent<Button>().onClick.AddListener(() => SelectSet(setData));
        }
    }

    private void SelectSet(TreasureSetData setData)
    {
        selectedSet = setData;
        hostButton.interactable = true;
        feedbackText.text = $"Selected: {setData.setName}";
        Debug.Log($"Selected Treasure Set: {setData.setName} ({setData.setId})");
        // You could also add a visual indicator (like changing the color) for the selected item
    }

    private void OnHostButtonClicked()
    {
        if (selectedSet == null)
        {
            feedbackText.text = "Please select a set first.";
            return;
        }

        // We have a set, now we tell the GameManager to create the room!
        GameManager.Instance.HostNewRoom(selectedSet);

        // The GameManager's OnLobbyReady event will then be handled by your MenuManager
        // to switch to the lobby panel.
        gameObject.SetActive(false); // Hide this panel
    }
}