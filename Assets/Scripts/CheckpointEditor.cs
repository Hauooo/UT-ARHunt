using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Complete checkpoint editor with GPS placement
/// Handles: place, view, edit, delete checkpoints with challenges
/// </summary>
public class CheckpointEditorController : MonoBehaviour
{
    [Header("Checkpoint List")]
    [SerializeField] private Transform checkpointListContent;
    [SerializeField] private GameObject checkpointItemPrefab;
    [SerializeField] private Button addCheckpointButton;

    [Header("Checkpoint Editor Panel")]
    [SerializeField] private GameObject editCheckpointPanel;
    [SerializeField] private TMP_InputField checkpointNameInput;
    [SerializeField] private TMP_Text checkpointLocationText;
    [SerializeField] private Button editSaveButton;
    [SerializeField] private Button editCancelButton;
    [SerializeField] private Button deleteCheckpointButton;
    [SerializeField] private Button changeChallengeButton;

    [Header("Challenge Configuration Panel")]
    [SerializeField] private GameObject challengeConfigPanel;
    [SerializeField] private TMP_Dropdown challengeTypeDropdown;

    [Header("MCQ Sub-Panel")]
    [SerializeField] private GameObject mcqSubPanel;
    [SerializeField] private TMP_InputField questionInput;
    [SerializeField] private TMP_InputField[] optionInputs = new TMP_InputField[4];
    [SerializeField] private TMP_Dropdown correctAnswerDropdown;
    [SerializeField] private TMP_InputField bonusPointsInput;
    [SerializeField] private TMP_InputField maxAttemptsInput;

    [Header("Minigame Sub-Panel")]
    [SerializeField] private GameObject minigameSubPanel;
    [SerializeField] private TMP_Dropdown minigameSelectionDropdown;
    [SerializeField] private TMP_InputField timeLimitInput;

    [Header("Challenge Buttons")]
    [SerializeField] private Button saveChallengeButton;
    [SerializeField] private Button cancelChallengeButton;

    [Header("Save/Close")]
    [SerializeField] private Button saveAllButton;
    [SerializeField] private Button closeEditorButton;
    [SerializeField] private TMP_Text statusText;

    private LocationManager locationManager;
    private List<TreasureManagerGPS_Multiplayer.TreasureData> treasures = new();
    private int selectedCheckpointIndex = -1;
    private System.Action<List<TreasureManagerGPS_Multiplayer.TreasureData>> onSaveCallback;
    private System.Action onCloseCallback;

    private readonly List<string> minigameOptions = new()
    {
        "MemoryMatch",
        "OrderSequence"
    };

    public void SetOnCloseCallback(System.Action onClose)
    {
        onCloseCallback = onClose;
    }

    private void Start()
    {
        locationManager = LocationManager.Instance;
        SetupButtons();
        SetupDropdowns();
        challengeConfigPanel.SetActive(false);
        gameObject.SetActive(false);
    }

    private void SetupButtons()
    {
        if (addCheckpointButton != null)
        {
            addCheckpointButton.onClick.RemoveAllListeners();
            addCheckpointButton.onClick.AddListener(PlaceCheckpointAtCurrentLocation);
            addCheckpointButton.GetComponentInChildren<TMP_Text>().text = "Add";
        }

        if (editSaveButton != null)
        {
            editSaveButton.onClick.RemoveAllListeners();
            editSaveButton.onClick.AddListener(SaveCheckpointEdit);
        }

        if (editCancelButton != null)
        {
            editCancelButton.onClick.RemoveAllListeners();
            editCancelButton.onClick.AddListener(CancelEdit);
        }

        if (deleteCheckpointButton != null)
        {
            deleteCheckpointButton.onClick.RemoveAllListeners();
            deleteCheckpointButton.onClick.AddListener(DeleteSelectedCheckpoint);
        }

        if (changeChallengeButton != null)
        {
            changeChallengeButton.onClick.RemoveAllListeners();
            changeChallengeButton.onClick.AddListener(OpenChallengeConfiguration);
        }

        if (saveChallengeButton != null)
        {
            saveChallengeButton.onClick.RemoveAllListeners();
            saveChallengeButton.onClick.AddListener(OnSaveChallenge);
        }

        if (cancelChallengeButton != null)
        {
            cancelChallengeButton.onClick.RemoveAllListeners();
            cancelChallengeButton.onClick.AddListener(HideChallengeConfiguration);
        }

        if (saveAllButton != null)
        {
            saveAllButton.onClick.RemoveAllListeners();
            saveAllButton.onClick.AddListener(SaveAllChanges);
        }

        if (closeEditorButton != null)
        {
            closeEditorButton.onClick.RemoveAllListeners();
            closeEditorButton.onClick.AddListener(CloseEditor);
        }

        if (challengeTypeDropdown != null)
        {
            challengeTypeDropdown.onValueChanged.RemoveAllListeners();
            challengeTypeDropdown.onValueChanged.AddListener(OnChallengeTypeChanged);
        }
    }

    private void SetupDropdowns()
    {
        // Challenge Type Dropdown
        if (challengeTypeDropdown != null)
        {
            challengeTypeDropdown.ClearOptions();
            challengeTypeDropdown.AddOptions(new List<string> { "None", "MCQ","ARMCQ", "Minigame" });
        }

        // Correct Answer Dropdown
        if (correctAnswerDropdown != null)
        {
            correctAnswerDropdown.ClearOptions();
            correctAnswerDropdown.AddOptions(new List<string> { "Option 1", "Option 2", "Option 3", "Option 4" });
        }

        // Minigame Selection Dropdown
        if (minigameSelectionDropdown != null)
        {
            minigameSelectionDropdown.ClearOptions();
            minigameSelectionDropdown.AddOptions(minigameOptions);
        }
    }

    /// <summary>
    /// Load treasures into the checkpoint editor
    /// </summary>
    public void LoadCheckpoints(List<TreasureManagerGPS_Multiplayer.TreasureData> treasureList,
                                System.Action<List<TreasureManagerGPS_Multiplayer.TreasureData>> onSave)
    {
        treasures = new List<TreasureManagerGPS_Multiplayer.TreasureData>(treasureList);
        onSaveCallback = onSave;
        RefreshCheckpointList();

        if (treasures.Count > 0)
        {
            SelectCheckpointForEdit(0);
        }

        UpdateStatus($"Loaded {treasures.Count} checkpoint(s)");
        Debug.Log($"[CheckpointEditor] Loaded {treasures.Count} treasures");
    }

    /// <summary>
    /// Place a checkpoint at user's current GPS location
    /// </summary>
    private void PlaceCheckpointAtCurrentLocation()
    {
        if (locationManager == null || locationManager.Status != LocationManager.LocationStatus.Ready)
        {
            UpdateStatus("GPS not ready. Wait a moment...");
            Debug.LogWarning("[CheckpointEditor] GPS not ready");
            return;
        }

        double lat = locationManager.Latitude;
        double lon = locationManager.Longitude;

        var newCheckpoint = new TreasureManagerGPS_Multiplayer.TreasureData
        {
            name = $"Treasure #{treasures.Count + 1}",
            lat = lat,
            lon = lon,
            points = 0,
            challenge = new ChallengeData { type = ChallengeType.None }
        };

        treasures.Add(newCheckpoint);
        RefreshCheckpointList();

        UpdateStatus($"Placed checkpoint at ({lat:F4}, {lon:F4})");
        Debug.Log($"[CheckpointEditor] Placed checkpoint {treasures.Count} at GPS: ({lat}, {lon})");
    }

    /// <summary>
    /// Refresh the checkpoint list display
    /// </summary>
    private void RefreshCheckpointList()
    {
        foreach (Transform child in checkpointListContent)
            Destroy(child.gameObject);

        for (int i = 0; i < treasures.Count; i++)
        {
            CreateCheckpointItem(i, treasures[i]);
        }

        UpdateStatus($"{treasures.Count} checkpoint(s)");
    }

    /// <summary>
    /// Create a checkpoint item in the list
    /// </summary>
    private void CreateCheckpointItem(int index, TreasureManagerGPS_Multiplayer.TreasureData treasure)
    {
        if (checkpointItemPrefab == null)
        {
            Debug.LogError("[CheckpointEditor] Checkpoint item prefab not assigned!");
            return;
        }

        GameObject itemObj = Instantiate(checkpointItemPrefab, checkpointListContent);

        var textComponent = itemObj.GetComponentInChildren<TMP_Text>();
        if (textComponent != null)
        {
            string challengeInfo = treasure.challenge?.type != ChallengeType.None
                ? $" ({treasure.challenge.type})"
                : " (No challenge)";
            textComponent.text = $"{index + 1}. {treasure.name}{challengeInfo}";
        }

        var button = itemObj.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => SelectCheckpointForEdit(index));
        }

        //configure button size
        var layoutElement = itemObj.GetComponent<LayoutElement>();
        if (layoutElement != null)
        {
            layoutElement.minHeight = 50;
            layoutElement.preferredHeight = 50;
            layoutElement.preferredWidth = 100;
        }

        Debug.Log($"[CheckpointEditor] Created item for checkpoint {index + 1}: {treasure.name}");
    }

    /// <summary>
    /// Select a checkpoint to edit
    /// </summary>
    private void SelectCheckpointForEdit(int index)
    {
        if (index < 0 || index >= treasures.Count)
            return;

        selectedCheckpointIndex = index;
        var checkpoint = treasures[index];

        if (editCheckpointPanel != null)
            editCheckpointPanel.SetActive(true);

        if (checkpointNameInput != null)
            checkpointNameInput.text = checkpoint.name;

        if (checkpointLocationText != null)
            checkpointLocationText.text = $"Location: ({checkpoint.lat:F6}, {checkpoint.lon:F6})\nPoints: {checkpoint.points}";

        UpdateStatus($"Editing: {checkpoint.name}");
        Debug.Log($"[CheckpointEditor] Selected checkpoint {index + 1} for editing");
    }

    /// <summary>
    /// Save checkpoint name edit
    /// </summary>
    private void SaveCheckpointEdit()
    {
        if (selectedCheckpointIndex < 0 || selectedCheckpointIndex >= treasures.Count)
            return;

        var checkpoint = treasures[selectedCheckpointIndex];

        if (checkpointNameInput != null)
            checkpoint.name = checkpointNameInput.text.Trim();

        RefreshCheckpointList();

        UpdateStatus($"Saved: {checkpoint.name}");
        Debug.Log($"[CheckpointEditor] Saved checkpoint {selectedCheckpointIndex + 1}");
    }

    /// <summary>
    /// Delete the selected checkpoint
    /// </summary>
    private void DeleteSelectedCheckpoint()
    {
        if (selectedCheckpointIndex < 0 || selectedCheckpointIndex >= treasures.Count)
        {
            UpdateStatus("Select a checkpoint first");
            return;
        }

        string deletedName = treasures[selectedCheckpointIndex].name;
        treasures.RemoveAt(selectedCheckpointIndex);

        RefreshCheckpointList();
        CancelEdit();

        UpdateStatus($"Deleted: {deletedName}");
        Debug.Log($"[CheckpointEditor] Deleted checkpoint");
    }

    /// <summary>
    /// Cancel editing checkpoint
    /// </summary>
    private void CancelEdit()
    {
        if (editCheckpointPanel != null)
            editCheckpointPanel.SetActive(false);

        selectedCheckpointIndex = -1;
    }

    /// <summary>
    /// Open challenge configuration panel
    /// </summary>
    private void OpenChallengeConfiguration()
    {
        if (selectedCheckpointIndex < 0 || selectedCheckpointIndex >= treasures.Count)
        {
            UpdateStatus("Select a checkpoint first");
            return;
        }

        // Ensure edit panel is hidden so it doesn't block interaction
        if (editCheckpointPanel != null)
            editCheckpointPanel.SetActive(false);

        // Pre-fill with existing challenge data
        var existing = treasures[selectedCheckpointIndex].challenge;
        if (existing != null)
            LoadExistingChallenge(existing);
        else
            ResetToDefaults();

        // Show challenge config panel
        if (challengeConfigPanel != null)
        {
            challengeConfigPanel.SetActive(true);

            // Ensure CanvasGroup is set correctly
            var canvasGroup = challengeConfigPanel.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = challengeConfigPanel.AddComponent<CanvasGroup>();

            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.alpha = 1f;
        }

        // Ensure dropdown is interactable
        if (challengeTypeDropdown != null)
        {
            challengeTypeDropdown.interactable = true;

            // Wait a frame to ensure UI has updated, then trigger change event
            OnChallengeTypeChanged(challengeTypeDropdown.value);
        }

        UpdateStatus($"Configuring challenge for '{treasures[selectedCheckpointIndex].name}'");
        Debug.Log($"[CheckpointEditor] Challenge config opened for checkpoint {selectedCheckpointIndex + 1}");
    }

    private System.Collections.IEnumerator TriggerDropdownChange()
    {
        yield return null; // Wait one frame
        if (challengeTypeDropdown != null)
            OnChallengeTypeChanged(challengeTypeDropdown.value);
    }



    /// <summary>
    /// Handle challenge type dropdown change
    /// </summary>
    private void OnChallengeTypeChanged(int index)
    {
        // Hide all sub-panels
        if (mcqSubPanel != null) mcqSubPanel.SetActive(false);
        if (minigameSubPanel != null) minigameSubPanel.SetActive(false);

        // Show the selected sub-panel
        switch (index)
        {
            case 0: // None
                Debug.Log("[CheckpointEditor] Challenge type: None");
                break;

            case 1: // MCQ
                if (mcqSubPanel != null) mcqSubPanel.SetActive(true);
                Debug.Log("[CheckpointEditor] Challenge type: MCQ");
                break;
            case 2: // ARMCQ
                if (mcqSubPanel != null) mcqSubPanel.SetActive(true);
                Debug.Log("[CheckpointEditor] Challenge type: ARMCQ");
                break;

            case 3: // Minigame
                if (minigameSubPanel != null) minigameSubPanel.SetActive(true);
                Debug.Log("[CheckpointEditor] Challenge type: Minigame");
                break;
        }
    }

    private void OnSaveChallenge()
    {
        if (selectedCheckpointIndex < 0 || selectedCheckpointIndex >= treasures.Count)
        {
            UpdateStatus("Select a checkpoint first");
            return;
        }

        int typeIndex = challengeTypeDropdown.value;
        ChallengeData data = null;

        if (typeIndex == 0) // None
        {
            data = new ChallengeData { type = ChallengeType.None };
        }
        else if (typeIndex == 1) // MCQ
        {
            if (!ValidateMCQ()) return;
            data = BuildMCQData(ChallengeType.MCQ); // Pass MCQ
        }
        else if (typeIndex == 2) // ARMCQ
        {
            if (!ValidateMCQ()) return;
            data = BuildMCQData(ChallengeType.ARMCQ); // Pass ARMCQ
        }
        else if (typeIndex == 3) // Minigame
        {
            data = BuildMinigameData();
        }

        treasures[selectedCheckpointIndex].challenge = data;
        RefreshCheckpointList();
        HideChallengeConfiguration();

        UpdateStatus($"Challenge saved for '{treasures[selectedCheckpointIndex].name}'");
        Debug.Log($"[CheckpointEditor] Challenge saved for checkpoint {selectedCheckpointIndex + 1}: {data?.type}");
    }

    /// <summary>
    /// Hide challenge configuration panel
    /// </summary>
    private void HideChallengeConfiguration()
    {
        if (challengeConfigPanel != null)
        {
            challengeConfigPanel.SetActive(false);

            // Ensure it's still interactable when reopened
            var canvasGroup = challengeConfigPanel.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
        }
    }

    // ── MCQ Validation & Building ─────────────────────────────────────────────

    private bool ValidateMCQ()
    {
        if (string.IsNullOrWhiteSpace(questionInput.text))
        {
            UpdateStatus("Question cannot be empty");
            return false;
        }

        int filledOptions = 0;
        foreach (var opt in optionInputs)
            if (!string.IsNullOrWhiteSpace(opt.text)) filledOptions++;

        if (filledOptions < 2)
        {
            UpdateStatus("At least 2 options required");
            return false;
        }

        return true;
    }

    private ChallengeData BuildMCQData(ChallengeType challengeType)
    {
        var options = new List<MCQOption>();
        int correctIndex = correctAnswerDropdown.value;

        for (int i = 0; i < optionInputs.Length; i++)
        {
            string text = optionInputs[i].text.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            options.Add(new MCQOption
            {
                text = text,
                isCorrect = (i == correctIndex)
            });
        }

        int.TryParse(bonusPointsInput.text, out int bonus);
        int.TryParse(maxAttemptsInput.text, out int attempts);

        return new ChallengeData
        {
            type = challengeType, // Use the type passed in (MCQ or ARMCQ)
            question = questionInput.text.Trim(),
            options = options,
            bonusPoints = bonus > 0 ? bonus : 50,
            maxAttempts = attempts > 0 ? attempts : 3
        };
    }



    // ── Minigame Validation & Building ────────────────────────────────────────

    private ChallengeData BuildMinigameData()
    {
        int.TryParse(timeLimitInput.text, out int timeLimit);

        var id = minigameSelectionDropdown.value < minigameOptions.Count
            ? minigameOptions[minigameSelectionDropdown.value]
            : "OrderSequence";

        ChallengeType resolvedType = id switch
        {
            "MemoryMatch" => ChallengeType.MemoryMatch,
            "OrderSequence" => ChallengeType.OrderSequence,
            _ => ChallengeType.MemoryMatch
        };

        return new ChallengeData
        {
            type = resolvedType,
            minigameId = id,
            timeLimitSeconds = timeLimit > 0 ? timeLimit : 60,
            bonusPoints = 50
        };
    }

    // ── Pre-fill Existing Challenge ───────────────────────────────────────────

    private void LoadExistingChallenge(ChallengeData data)
    {
        ResetToDefaults();

        if (data == null || data.type == ChallengeType.None)
        {
            challengeTypeDropdown.value = 0;
            return;
        }

        switch (data.type)
        {
            case ChallengeType.MCQ:
            case ChallengeType.ARMCQ:
                // 1 is MCQ, 2 is ARMCQ based on your SetupDropdowns order
                challengeTypeDropdown.value = (data.type == ChallengeType.MCQ) ? 1 : 2;

                questionInput.text = data.question ?? "";
                bonusPointsInput.text = data.bonusPoints.ToString();
                maxAttemptsInput.text = data.maxAttempts.ToString();
                if (data.options != null)
                {
                    for (int i = 0; i < optionInputs.Length && i < data.options.Count; i++)
                    {
                        optionInputs[i].text = data.options[i].text;
                        if (data.options[i].isCorrect)
                            correctAnswerDropdown.value = i;
                    }
                }
                break;

            case ChallengeType.MemoryMatch:
            case ChallengeType.OrderSequence:
                // Minigame is now index 3 in the dropdown!
                challengeTypeDropdown.value = 3;

                int idx = minigameOptions.IndexOf(data.minigameId ?? "");
                minigameSelectionDropdown.value = idx >= 0 ? idx : 0;
                timeLimitInput.text = data.timeLimitSeconds.ToString();
                break;
        }
    }

    private void ResetToDefaults()
    {
        challengeTypeDropdown.value = 0;
        questionInput.text = "";
        foreach (var opt in optionInputs) opt.text = "";
        correctAnswerDropdown.value = 0;
        bonusPointsInput.text = "150";
        maxAttemptsInput.text = "3";
        minigameSelectionDropdown.value = 0;
        timeLimitInput.text = "60";
    }

    /// <summary>
    /// Save all changes and close editor
    /// </summary>
    private void SaveAllChanges()
    {
        if (treasures == null || treasures.Count == 0)
        {
            UpdateStatus("Cannot save empty checkpoint list");
            return;
        }

        Debug.Log($"[CheckpointEditor] Saving {treasures.Count} checkpoints with changes");
        UpdateStatus($"Saved {treasures.Count} checkpoints");

        onSaveCallback?.Invoke(treasures);
        CloseEditor();
    }

    /// <summary>
    /// Save all checkpoints to Firebase in the currently editing set
    /// </summary>
    private void SaveCheckpointsToFirebase()
    {
        if (treasures == null || treasures.Count == 0)
        {
            UpdateStatus("No checkpoints to save");
            return;
        }

        // This will be called by CreatorMapController which has Firebase access
        // We just prepare the data here
        onSaveCallback?.Invoke(treasures);
    }

    /// <summary>
    /// Close the editor and return to map
    /// </summary>
    private void CloseEditor()
    {
        if (editCheckpointPanel != null)
            editCheckpointPanel.SetActive(false);

        if (challengeConfigPanel != null)
            challengeConfigPanel.SetActive(false);

        gameObject.SetActive(false);
        selectedCheckpointIndex = -1;

        onCloseCallback?.Invoke();

        Debug.Log("[CheckpointEditor] Editor closed and returned to map");
    }

    /// <summary>
    /// Get the edited treasures list
    /// </summary>
    public List<TreasureManagerGPS_Multiplayer.TreasureData> GetEditedTreasures()
    {
        return treasures;
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
        Debug.Log("[CheckpointEditor] " + message);
    }
}