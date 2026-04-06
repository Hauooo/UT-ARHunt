using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Handles the Challenge Configuration panel in CreatorScene.
/// The creator can attach an MCQ or minigame to any treasure checkpoint.
/// </summary>
public class ChallengeConfigController : MonoBehaviour
{
    [Header("Panel Root")]
    [SerializeField] private GameObject challengeConfigPanel;

    [Header("Type Selection")]
    [SerializeField] private TMP_Dropdown challengeTypeDropdown;

    [Header("MCQ Sub-Panel")]
    [SerializeField] private GameObject mcqSubPanel;
    [SerializeField] private TMP_InputField questionInput;
    [SerializeField] private TMP_InputField[] optionInputs;      // 4 elements
    [SerializeField] private TMP_Dropdown correctAnswerDropdown; // 0-3
    [SerializeField] private TMP_InputField bonusPointsInput;
    [SerializeField] private TMP_InputField maxAttemptsInput;

    [Header("Minigame Sub-Panel")]
    [SerializeField] private GameObject minigameSubPanel;
    [SerializeField] private TMP_Dropdown minigameDropdown;
    [SerializeField] private TMP_InputField timeLimitInput;

    [Header("Sub-Panel Cancel Buttons")]
    [SerializeField] private Button cancelMcqSubpanelButton;
    [SerializeField] private Button cancelMinigameSubpanelButton;

    [Header("Shared Action Buttons")]
    [SerializeField] private Button saveChallengeButton;
    [SerializeField] private Button cancelChallengeButton; // cancel = back to type selection

    // ── State ────────────────────────────────────────────────────────────────
    private int editingPinIndex = -1;
    private System.Action<int, ChallengeData> onSaveCallback;

    private readonly List<string> minigameOptions = new()
    {
        "MemoryMatch",
        "OrderSequence"
    };

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        challengeTypeDropdown.ClearOptions();
        challengeTypeDropdown.AddOptions(new List<string> { "None", "MCQ", "AR MCQ", "Minigame" });

        minigameDropdown.ClearOptions();
        minigameDropdown.AddOptions(minigameOptions);

        // Type change
        challengeTypeDropdown.onValueChanged.RemoveAllListeners();
        challengeTypeDropdown.onValueChanged.AddListener(OnTypeChanged);

        // Save / Cancel
        if (saveChallengeButton != null)
        {
            saveChallengeButton.onClick.RemoveAllListeners();
            saveChallengeButton.onClick.AddListener(OnSaveChallenge);
        }

        if (cancelChallengeButton != null)
        {
            cancelChallengeButton.onClick.RemoveAllListeners();
            cancelChallengeButton.onClick.AddListener(BackToTypeSelection);
        }

        // Sub-panel cancel buttons
        if (cancelMcqSubpanelButton != null)
        {
            cancelMcqSubpanelButton.onClick.RemoveAllListeners();
            cancelMcqSubpanelButton.onClick.AddListener(BackToTypeSelection);
        }

        if (cancelMinigameSubpanelButton != null)
        {
            cancelMinigameSubpanelButton.onClick.RemoveAllListeners();
            cancelMinigameSubpanelButton.onClick.AddListener(BackToTypeSelection);
        }

        challengeConfigPanel.SetActive(false);
    }

    /// <summary>
    /// Call this when the creator taps a placed pin to configure its challenge.
    /// </summary>
    public void Show(int pinIndex, ChallengeData existing, System.Action<int, ChallengeData> onSave)
    {
        editingPinIndex = pinIndex;
        onSaveCallback = onSave;

        if (existing != null) LoadExistingChallenge(existing);
        else ResetToDefaults();

        challengeConfigPanel.SetActive(true);
        OnTypeChanged(challengeTypeDropdown.value);
    }

    public void Hide()
    {
        challengeConfigPanel.SetActive(false);
        editingPinIndex = -1;
    }

    // ── Type Toggle ───────────────────────────────────────────────────────────

    private void OnTypeChanged(int index)
    {
        // 0=None, 1=MCQ, 2=AR MCQ, 3=Minigame
        bool isMCQFamily = (index == 1 || index == 2);
        bool isMini = (index == 3);
        bool showActionButtons = isMCQFamily || isMini;

        mcqSubPanel.SetActive(isMCQFamily);
        minigameSubPanel.SetActive(isMini);

        if (saveChallengeButton != null) saveChallengeButton.gameObject.SetActive(showActionButtons);
        if (cancelChallengeButton != null) cancelChallengeButton.gameObject.SetActive(showActionButtons);

        var mcqCanvas = mcqSubPanel != null ? mcqSubPanel.GetComponent<CanvasGroup>() : null;
        if (mcqCanvas != null)
        {
            mcqCanvas.blocksRaycasts = isMCQFamily;   
            mcqCanvas.interactable = isMCQFamily;     
        }

        var miniCanvas = minigameSubPanel != null ? minigameSubPanel.GetComponent<CanvasGroup>() : null;
        if (miniCanvas != null)
        {
            miniCanvas.blocksRaycasts = isMini;
            miniCanvas.interactable = isMini;
        }

        challengeTypeDropdown.RefreshShownValue();
    }

    private void BackToTypeSelection()
    {
        challengeTypeDropdown.value = 0;
        OnTypeChanged(0);
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void OnSaveChallenge()
    {
        int typeIndex = challengeTypeDropdown.value;
        ChallengeData data = null;

        if (typeIndex == 0) // None
        {
            data = new ChallengeData { type = ChallengeType.None };
        }
        else if (typeIndex == 1) // MCQ
        {
            if (!ValidateMCQ()) return;
            data = BuildMCQData();
            data.type = ChallengeType.MCQ;
        }
        else if (typeIndex == 2) // AR MCQ
        {
            if (!ValidateMCQ()) return;
            data = BuildMCQData();
            data.type = ChallengeType.ARMCQ;
        }
        else if (typeIndex == 3) // Minigame
        {
            data = BuildMinigameData();
        }

        onSaveCallback?.Invoke(editingPinIndex, data);
        Hide();
    }

    // ── MCQ Helpers ───────────────────────────────────────────────────────────

    private bool ValidateMCQ()
    {
        if (string.IsNullOrWhiteSpace(questionInput.text))
        {
            Debug.LogWarning("[ChallengeConfig] Question cannot be empty.");
            return false;
        }

        int filledOptions = 0;
        foreach (var opt in optionInputs)
            if (!string.IsNullOrWhiteSpace(opt.text)) filledOptions++;

        if (filledOptions < 2)
        {
            Debug.LogWarning("[ChallengeConfig] At least 2 options required.");
            return false;
        }

        return true;
    }

    private ChallengeData BuildMCQData()
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
            type = ChallengeType.MCQ,
            question = questionInput.text.Trim(),
            options = options,
            bonusPoints = bonus > 0 ? bonus : 50,
            maxAttempts = attempts > 0 ? attempts : 3
        };
    }

    // ── Minigame Helpers ──────────────────────────────────────────────────────

    private ChallengeData BuildMinigameData()
    {
        int.TryParse(timeLimitInput.text, out int timeLimit);

        var id = minigameOptions[minigameDropdown.value];
        ChallengeType resolvedType = id switch
        {
            "MemoryMatch" => ChallengeType.MemoryMatch,
            "OrderSequence" => ChallengeType.OrderSequence,
            _ => ChallengeType.None
        };

        return new ChallengeData
        {
            type = resolvedType,
            minigameId = id,
            timeLimitSeconds = timeLimit > 0 ? timeLimit : 60
        };
    }

    // ── Pre-fill ──────────────────────────────────────────────────────────────

    private void LoadExistingChallenge(ChallengeData data)
    {
        // clean defaults first
        ResetToDefaults();

        switch (data.type)
        {
            case ChallengeType.None:
                challengeTypeDropdown.value = 0;
                break;

            case ChallengeType.MCQ:
                challengeTypeDropdown.value = 1;
                questionInput.text = data.question ?? "";
                bonusPointsInput.text = data.bonusPoints.ToString();
                maxAttemptsInput.text = data.maxAttempts.ToString();

                if (data.options != null)
                {
                    for (int i = 0; i < optionInputs.Length && i < data.options.Count; i++)
                    {
                        optionInputs[i].text = data.options[i].text ?? "";
                        if (data.options[i].isCorrect) correctAnswerDropdown.value = i;
                    }
                }
                break;
            case ChallengeType.ARMCQ:
                challengeTypeDropdown.value = 2;
                questionInput.text = data.question ?? "";
                bonusPointsInput.text = data.bonusPoints.ToString();
                maxAttemptsInput.text = data.maxAttempts.ToString();

                if (data.options != null)
                {
                    for (int i = 0; i < optionInputs.Length && i < data.options.Count; i++)
                    {
                        optionInputs[i].text = data.options[i].text ?? "";
                        if (data.options[i].isCorrect) correctAnswerDropdown.value = i;
                    }
                }
                break;

            case ChallengeType.MemoryMatch:
            case ChallengeType.OrderSequence:
                challengeTypeDropdown.value = 3;
                if (!string.IsNullOrEmpty(data.minigameId))
                {
                    int idx = minigameOptions.IndexOf(data.minigameId);
                    minigameDropdown.value = idx >= 0 ? idx : 0;
                }
                else
                {
                    minigameDropdown.value = data.type == ChallengeType.OrderSequence ? 1 : 0;
                }

                timeLimitInput.text = (data.timeLimitSeconds > 0 ? data.timeLimitSeconds : 60).ToString();
                break;
        }
    }

    private void ResetToDefaults()
    {
        challengeTypeDropdown.value = 0;

        questionInput.text = "";
        foreach (var opt in optionInputs) opt.text = "";
        correctAnswerDropdown.value = 0;
        bonusPointsInput.text = "50";
        maxAttemptsInput.text = "3";

        minigameDropdown.value = 0;
        timeLimitInput.text = "60";
    }
}