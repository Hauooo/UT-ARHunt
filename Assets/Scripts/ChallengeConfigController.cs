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
    [SerializeField] private TMP_Dropdown minigameDropdown;      // MemoryMatch_Easy etc.
    [SerializeField] private TMP_InputField timeLimitInput;

    [Header("Buttons")]
    [SerializeField] private Button saveChallengeButton;
    [SerializeField] private Button cancelChallengeButton;

    [Header("Pin Label (feedback)")]
    [SerializeField] private TMP_Text pinChallengeStatusText; // shows "✓ MCQ attached" on pin

    // ── State ─────────────────────────────────────────────────────────────────
    private int editingPinIndex = -1;
    private System.Action<int, ChallengeData> onSaveCallback;

    private readonly List<string> minigameOptions = new()
    {
        "MemoryMatch_Easy",
        "MemoryMatch_Hard",
        "OrderSequence",
        "BalloonPop_Easy",
        "BalloonPop_Hard"
    };

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Populate dropdowns
        challengeTypeDropdown.ClearOptions();
        challengeTypeDropdown.AddOptions(new List<string> { "None", "MCQ", "Minigame" });

        minigameDropdown.ClearOptions();
        minigameDropdown.AddOptions(minigameOptions);

        // Wire listeners
        challengeTypeDropdown.onValueChanged.AddListener(OnTypeChanged);
        saveChallengeButton.onClick.AddListener(OnSaveChallenge);
        cancelChallengeButton.onClick.AddListener(Hide);

        challengeConfigPanel.SetActive(false);
    }

    /// <summary>
    /// Call this when the creator taps a placed pin to configure its challenge.
    /// </summary>
    /// <param name="pinIndex">Index in the working pins list</param>
    /// <param name="existing">Existing challenge data (or null if none)</param>
    /// <param name="onSave">Callback: (pinIndex, challengeData) → called on save</param>
    public void Show(int pinIndex, ChallengeData existing,
                     System.Action<int, ChallengeData> onSave)
    {
        editingPinIndex = pinIndex;
        onSaveCallback = onSave;

        // Pre-fill with existing data
        if (existing != null)
            LoadExistingChallenge(existing);
        else
            ResetToDefaults();

        challengeConfigPanel.SetActive(true);
        OnTypeChanged(challengeTypeDropdown.value); // show correct sub-panel
    }

    public void Hide()
    {
        challengeConfigPanel.SetActive(false);
        editingPinIndex = -1;
    }

    // ── Type Toggle ───────────────────────────────────────────────────────────

    private void OnTypeChanged(int index)
    {
        // index: 0=None, 1=MCQ, 2=Minigame
        mcqSubPanel.SetActive(index == 1);
        minigameSubPanel.SetActive(index == 2);
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
        }
        else if (typeIndex == 2) // Minigame
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
            "MemoryMatch_Easy" or "MemoryMatch_Hard" => ChallengeType.MemoryMatch,
            "OrderSequence"                          => ChallengeType.OrderSequence,
            "BalloonPop_Easy"  or "BalloonPop_Hard"  => ChallengeType.BalloonPop,
            _                                        => ChallengeType.MemoryMatch
        };

        return new ChallengeData
        {
            type             = resolvedType,
            minigameId       = id,
            timeLimitSeconds = timeLimit > 0 ? timeLimit : 60
        };
    }

    // ── Pre-fill ──────────────────────────────────────────────────────────────

    private void LoadExistingChallenge(ChallengeData data)
    {
        switch (data.type)
        {
            case ChallengeType.None:
                challengeTypeDropdown.value = 0;
                break;

            case ChallengeType.MCQ:
                challengeTypeDropdown.value = 1;
                questionInput.text = data.question;
                bonusPointsInput.text = data.bonusPoints.ToString();
                maxAttemptsInput.text = data.maxAttempts.ToString();
                for (int i = 0; i < optionInputs.Length && i < data.options.Count; i++)
                {
                    optionInputs[i].text = data.options[i].text;
                    if (data.options[i].isCorrect) correctAnswerDropdown.value = i;
                }
                break;

            case ChallengeType.MemoryMatch:
            case ChallengeType.OrderSequence:
            case ChallengeType.BalloonPop:
                challengeTypeDropdown.value = 2;
                int idx = minigameOptions.IndexOf(data.minigameId);
                minigameDropdown.value = idx >= 0 ? idx : 0;
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
        bonusPointsInput.text = "50";
        maxAttemptsInput.text = "3";
        minigameDropdown.value = 0;
        timeLimitInput.text = "60";
    }
}