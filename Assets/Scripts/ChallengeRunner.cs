using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Shown to the player when they reach a checkpoint that has a challenge.
/// Handles MCQ display, answer checking, minigame launching, and result callbacks.
/// </summary>
public class ChallengeRunner : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject challengePanel;

    [Header("MCQ UI")]
    [SerializeField] private GameObject mcqPanel;
    [SerializeField] private TMP_Text questionText;
    [SerializeField] private Button[] answerButtons;        // 4 buttons
    [SerializeField] private TMP_Text[] answerButtonLabels; // TMP_Text on each button
    [SerializeField] private TMP_Text attemptsText;
    [SerializeField] private TMP_Text resultText;

    [Header("Minigame UI")]
    [SerializeField] private GameObject minigamePanel;
    [SerializeField] private TMP_Text minigameNameText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private Button launchMinigameButton;

    [Header("AR Minigames")]
    [SerializeField] private ARBalloonPopManager balloonPopManager;

    [Header("Shared")]
    [SerializeField] private Button skipButton;   // optional — creator can disable

    // ── State ─────────────────────────────────────────────────────────────────
    private ChallengeData currentChallenge;
    private int attemptsLeft;
    private System.Action<bool, int> onComplete; // (success, bonusPoints)

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        challengePanel.SetActive(false);
    }

    /// <summary>
    /// Call this when a player reaches a checkpoint with a challenge.
    /// </summary>
    /// <param name="challenge">The challenge data from TreasureData</param>
    /// <param name="onComplete">Callback: (wasSuccessful, bonusPointsEarned)</param>
    public void RunChallenge(ChallengeData challenge, System.Action<bool, int> onComplete)
    {
        if (challenge == null || challenge.type == ChallengeType.None)
        {
            // No challenge — instant success
            onComplete?.Invoke(true, 0);
            return;
        }

        currentChallenge = challenge;
        this.onComplete = onComplete;
        attemptsLeft = challenge.maxAttempts;

        challengePanel.SetActive(true);

        switch (challenge.type)
        {
            case ChallengeType.MCQ:
                ShowMCQ(challenge);
                break;
            case ChallengeType.MemoryMatch:
            case ChallengeType.OrderSequence:
            case ChallengeType.BalloonPop:
                ShowMinigameLauncher(challenge);
                break;
        }
    }

    // ── MCQ ───────────────────────────────────────────────────────────────────

    private void ShowMCQ(ChallengeData challenge)
    {
        mcqPanel.SetActive(true);
        minigamePanel.SetActive(false);

        questionText.text = challenge.question;
        resultText.text = "";
        UpdateAttemptsText();

        // Shuffle options for fairness
        var shuffled = new List<MCQOption>(challenge.options);
        shuffled.Sort((a, b) => Random.Range(-1, 2));

        for (int i = 0; i < answerButtons.Length; i++)
        {
            bool hasOption = i < shuffled.Count;
            answerButtons[i].gameObject.SetActive(hasOption);

            if (!hasOption) continue;

            answerButtonLabels[i].text = shuffled[i].text;
            bool isCorrect = shuffled[i].isCorrect;

            // Capture for closure
            int capturedI = i;
            answerButtons[i].onClick.RemoveAllListeners();
            answerButtons[i].onClick.AddListener(() => OnAnswerSelected(isCorrect));
        }
    }

    private void OnAnswerSelected(bool isCorrect)
    {
        if (isCorrect)
        {
            resultText.text = "✅ Correct! Checkpoint unlocked!";
            resultText.color = Color.green;
            StartCoroutine(DelayedComplete(true, currentChallenge.bonusPoints));
        }
        else
        {
            attemptsLeft--;
            UpdateAttemptsText();

            if (attemptsLeft <= 0)
            {
                resultText.text = "❌ No attempts left. Checkpoint failed.";
                resultText.color = Color.red;
                StartCoroutine(DelayedComplete(false, 0));
            }
            else
            {
                resultText.text = $"❌ Wrong! {attemptsLeft} attempt(s) remaining.";
                resultText.color = Color.yellow;
            }
        }
    }

    private void UpdateAttemptsText()
    {
        attemptsText.text = $"Attempts left: {attemptsLeft}";
    }

    // ── Minigame Launcher ─────────────────────────────────────────────────────

    private void ShowMinigameLauncher(ChallengeData challenge)
    {
        mcqPanel.SetActive(false);
        minigamePanel.SetActive(true);

        string displayName = challenge.minigameId switch
        {
            "MemoryMatch_Easy" => "🃏 Memory Match (Easy)",
            "MemoryMatch_Hard" => "🃏 Memory Match (Hard)",
            "OrderSequence"    => "🔢 Order Sequence",
            "BalloonPop_Easy"  => "🎈 Pop the Balloons! (Easy)",
            "BalloonPop_Hard"  => "🎈 Pop the Balloons! (Hard)",
            _                  => challenge.minigameId
        };

        minigameNameText.text = $"Challenge: {displayName}\nTime limit: {challenge.timeLimitSeconds}s";

        launchMinigameButton.onClick.RemoveAllListeners();
        launchMinigameButton.onClick.AddListener(() =>
            StartCoroutine(LaunchMinigame(challenge)));
    }

    private IEnumerator LaunchMinigame(ChallengeData challenge)
    {
        minigamePanel.SetActive(false);

        // ── Dispatch to the correct minigame manager ──────────────────────
        // Replace these with your actual minigame scene/panel calls:
        switch (challenge.minigameId)
        {
            case "MemoryMatch_Easy":
            case "MemoryMatch_Hard":
                // TODO: MemoryMatchManager.Instance.StartGame(challenge, OnMinigameResult);
                break;
            case "OrderSequence":
                // TODO: OrderSequenceManager.Instance.StartGame(challenge, OnMinigameResult);
                break;
            case "BalloonPop_Easy":
            case "BalloonPop_Hard":
                if (balloonPopManager != null)
                {
                    balloonPopManager.StartGame(challenge, OnMinigameResult);
                    yield break;   // don't fall through to the fake timer
                }
                break;
        }

        // Placeholder: simulate minigame result after 3 seconds (MemoryMatch / OrderSequence TODO)
        yield return new WaitForSeconds(3f);
        OnMinigameResult(true); // replace with real callback
    }

    private void OnMinigameResult(bool success)
    {
        int bonus = success ? currentChallenge.bonusPoints : 0;
        StartCoroutine(DelayedComplete(success, bonus));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IEnumerator DelayedComplete(bool success, int bonus)
    {
        yield return new WaitForSeconds(1.5f);
        challengePanel.SetActive(false);
        onComplete?.Invoke(success, bonus);
    }
}