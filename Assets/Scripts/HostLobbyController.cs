using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;
using Firebase.Auth;
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
    private Coroutine fetchRoutine;

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
        fetchRoutine = StartCoroutine(FetchWhenSignedIn());
    }

    private void OnDisable()
    {
        if (fetchRoutine != null) StopCoroutine(fetchRoutine);
        fetchRoutine = null;
    }

    private IEnumerator FetchWhenSignedIn()
    {
        // wait up to 10 seconds for auth
        float timeout = 10f;
        while (timeout > 0f && FirebaseAuth.DefaultInstance.CurrentUser == null)
        {
            if (feedbackText != null) feedbackText.text = "Signing in... please wait.";
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        FetchPlayerTreasureSets();
    }

    private void FetchPlayerTreasureSets()
    {
        if (feedbackText != null) feedbackText.text = "Loading your treasure sets...";

        if (dbRef == null)
        {
            Debug.LogError("[HostLobbyController] dbRef is null (Firebase DB not configured).");
            if (feedbackText != null) feedbackText.text = "Database not ready.";
            return;
        }

        foreach (Transform child in contentParent) Destroy(child.gameObject);
        availableSets.Clear();
        selectedSet = null;
        hostButton.interactable = false;

        string myUserId = FirebaseAuth.DefaultInstance.CurrentUser?.UserId;

        Debug.Log($"[HostLobbyController] Firebase UID={myUserId}, AuthManager.UserId={(authManager != null ? authManager.UserId : "null")}");

        if (string.IsNullOrEmpty(myUserId))
        {
            Debug.LogWarning("[HostLobbyController] Firebase user not signed in yet.");
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
                    Debug.LogError("[HostLobbyController] Error fetching treasure sets: " + task.Exception);
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
            string setId = childSnapshot.Key;
            TreasureSetData setData = JsonUtility.FromJson<TreasureSetData>(json);
            setData.setId = setId;
            availableSets[setId] = setData;

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