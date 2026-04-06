using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class ChallengeRunner : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject challengePanel;

    [Header("MCQ UI")]
    [SerializeField] private GameObject mcqPanel;
    [SerializeField] private TMP_Text questionText;
    [SerializeField] private Button[] answerButtons;
    [SerializeField] private TMP_Text[] answerButtonLabels;
    [SerializeField] private TMP_Text attemptsText;
    [SerializeField] private TMP_Text resultText;

    [Header("Minigame UI")]
    [SerializeField] private GameObject minigamePanel;
    [SerializeField] private TMP_Text minigameNameText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private Button launchMinigameButton;

    [Header("AR Minigames")]
    [SerializeField] private MemoryMatchManager memoryMatchManager;

    [Header("AR MCQ")]
    [SerializeField] private ArMCQManager arMCQManager;

    [Header("Shared")]
    [SerializeField] private Button skipButton;
    [SerializeField] private Button retryButton; // NEW

    private ChallengeData currentChallenge;
    private int attemptsLeft;
    private System.Action<bool, int> onComplete;
    private bool awaitingRetryChoice;

    private void Awake()
    {
        challengePanel.SetActive(false);

        if (skipButton != null)
        {
            skipButton.onClick.RemoveAllListeners();
            skipButton.onClick.AddListener(OnSkipClicked);
        }

        if (retryButton != null)
        {
            retryButton.onClick.RemoveAllListeners();
            retryButton.onClick.AddListener(OnRetryClicked);
            retryButton.gameObject.SetActive(false);
        }

        if (launchMinigameButton != null)
        {
            launchMinigameButton.onClick.RemoveAllListeners();
            launchMinigameButton.onClick.AddListener(OnLaunchMinigameClicked);
        }
    }

    public void RunChallenge(ChallengeData challenge, System.Action<bool, int> onComplete)
    {
        if (challenge == null || challenge.type == ChallengeType.None)
        {
            onComplete?.Invoke(true, 0);
            return;
        }

        currentChallenge = challenge;
        this.onComplete = onComplete;
        attemptsLeft = challenge.maxAttempts;
        awaitingRetryChoice = false;

        challengePanel.SetActive(true);
        resultText.text = "";

        if (retryButton != null) retryButton.gameObject.SetActive(false);
        if (skipButton != null) skipButton.gameObject.SetActive(true);

        switch (challenge.type)
        {
            case ChallengeType.MCQ:
                if (challenge.useARMode && arMCQManager != null)
                    ShowARMCQ(challenge);
                else
                    ShowMCQ(challenge);
                break;
            case ChallengeType.MemoryMatch:
            case ChallengeType.OrderSequence:
                ShowMinigameLauncher(challenge);
                break;
        }
    }

    private void ShowMCQ(ChallengeData challenge)
    {
        mcqPanel.SetActive(true);
        minigamePanel.SetActive(false);
        questionText.text = challenge.question;
        UpdateAttemptsText();

        var shuffled = new List<MCQOption>(challenge.options);
        shuffled.Sort((a, b) => Random.Range(-1, 2));

        for (int i = 0; i < answerButtons.Length; i++)
        {
            bool hasOption = i < shuffled.Count;
            answerButtons[i].gameObject.SetActive(hasOption);
            if (!hasOption) continue;

            answerButtonLabels[i].text = shuffled[i].text;
            bool isCorrect = shuffled[i].isCorrect;

            answerButtons[i].onClick.RemoveAllListeners();
            answerButtons[i].onClick.AddListener(() => OnAnswerSelected(isCorrect));
        }
    }

    private void ShowARMCQ(ChallengeData challenge)
    {
        mcqPanel.SetActive(false);
        minigamePanel.SetActive(false);

        // Show the question in the HUD while AR options load
        questionText.text = challenge.question;
        UpdateAttemptsText();

        arMCQManager.StartARMCQ(challenge, OnARMCQComplete);
    }

    private void OnARMCQComplete(bool success, int bonus)
    {
        // Fallback: AR unavailable – retry as screen-based MCQ
        if (ArMCQManager.IsARFallbackResult(success, bonus))
        {
            ShowMCQ(currentChallenge);
            return;
        }

        if (success)
        {
            resultText.text = "✅ Correct!";
        }
        else
        {
            resultText.text = "❌ Failed. You can skip.";
        }

        StartCoroutine(DelayedComplete(success, bonus));
    }

    private void OnAnswerSelected(bool isCorrect)
    {
        if (isCorrect)
        {
            resultText.text = "✅ Correct!";
            StartCoroutine(DelayedComplete(true, currentChallenge.bonusPoints));
            return;
        }

        attemptsLeft--;
        UpdateAttemptsText();

        if (attemptsLeft <= 0)
        {
            resultText.text = "❌ Failed. You can skip.";
            StartCoroutine(DelayedComplete(false, 0));
        }
        else
        {
            resultText.text = $"❌ Wrong! {attemptsLeft} left.";
        }
    }

    private void UpdateAttemptsText() => attemptsText.text = $"Attempts left: {attemptsLeft}";

    private void ShowMinigameLauncher(ChallengeData challenge)
    {
        mcqPanel.SetActive(false);
        minigamePanel.SetActive(true);

        minigameNameText.text = $"Challenge: {challenge.minigameId}\nTime limit: {challenge.timeLimitSeconds}s";

        launchMinigameButton.onClick.RemoveAllListeners();
        launchMinigameButton.onClick.AddListener(() => StartCoroutine(LaunchMinigame(challenge)));
    }

    private void OnLaunchMinigameClicked()
    {
        if (currentChallenge != null)
            StartCoroutine(LaunchMinigame(currentChallenge));
    }

    private IEnumerator LaunchMinigame(ChallengeData challenge)
    {
        awaitingRetryChoice = false;
        minigamePanel.SetActive(false);
        if (retryButton != null) retryButton.gameObject.SetActive(false);

        switch (challenge.minigameId)
        {
            case "MemoryMatch_Easy":
            case "MemoryMatch_Hard":
                if (memoryMatchManager != null)
                {
                    challengePanel.SetActive(false);
                    memoryMatchManager.StartGame(challenge, OnMinigameResult);
                    yield break;
                }
                OnMinigameResult(false);
                yield break;
        }

        yield return new WaitForSeconds(3f);
        OnMinigameResult(false);
    }

    private void OnMinigameResult(bool success)
    {
        if (memoryMatchManager != null) memoryMatchManager.StopGame();

        challengePanel.SetActive(true);

        if (success)
        {
            resultText.text = "✅ Minigame completed!";
            StartCoroutine(DelayedComplete(true, currentChallenge.bonusPoints));
        }
        else
        {
            // KEY FIX: don't auto-complete fail; wait for user action
            awaitingRetryChoice = true;
            resultText.text = "❌ Minigame failed. Retry or Skip?";
            minigamePanel.SetActive(true);

            if (retryButton != null) retryButton.gameObject.SetActive(true);
            if (skipButton != null) skipButton.gameObject.SetActive(true);
        }
    }

    private IEnumerator DelayedComplete(bool success, int bonus)
    {
        yield return new WaitForSeconds(1.0f);
        challengePanel.SetActive(false);
        onComplete?.Invoke(success, bonus);
    }

    private void OnRetryClicked()
    {
        if (!awaitingRetryChoice || currentChallenge == null) return;
        resultText.text = "";
        StartCoroutine(LaunchMinigame(currentChallenge));
    }

    private void OnSkipClicked()
    {
        challengePanel.SetActive(false);
        onComplete?.Invoke(false, 0);
    }
}