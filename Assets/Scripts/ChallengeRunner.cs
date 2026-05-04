using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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

    [Header("AR Minigames")]
    [SerializeField] private MemoryMatchManager memoryMatchManager;
    [SerializeField] private OrderSequenceMinigame orderSequenceMinigame;
    [SerializeField] private ArMCQManager arMcqManager;

    [Header("Minigame UI")]
    [SerializeField] private GameObject minigamePanel;
    [SerializeField] private TMP_Text minigameNameText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private Button launchMinigameButton;




    [Header("Shared")]
    [SerializeField] private Button skipButton;
    [SerializeField] private Button retryButton;

    private ChallengeData currentChallenge;
    private int attemptsLeft;
    private System.Action<bool, int> onComplete;
    private bool awaitingRetryChoice;
    private bool challengeInProgress = false;

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

    public void RunChallenge(ChallengeData challenge, System.Action<bool, int> onCompleteCallback)
    {
        if (challenge == null || challenge.type == ChallengeType.None)
        {
            onCompleteCallback?.Invoke(true, 0);
            return;
        }

        currentChallenge = challenge;
        onComplete = onCompleteCallback;
        attemptsLeft = Mathf.Max(1, challenge.maxAttempts);
        awaitingRetryChoice = false;
        challengeInProgress = true;

        challengePanel.SetActive(true);
        resultText.text = "";

        if (retryButton != null) retryButton.gameObject.SetActive(false);
        if (skipButton != null) skipButton.gameObject.SetActive(true);

        switch (challenge.type)
        {
            case ChallengeType.MCQ:
                ShowMCQ(challenge); // pure 2D MCQ
                break;

            case ChallengeType.ARMCQ:  // ← ADD THIS
                ShowARMCQ(challenge); // AR-based MCQ
                break;

            case ChallengeType.MemoryMatch:
            case ChallengeType.OrderSequence:
                ShowMinigameLauncher(challenge);
                break;

            default:
                Debug.LogWarning($"[ChallengeRunner] Unknown challenge type: {challenge.type}");
                OnChallengeComplete(false, 0);
                break;
        }
    }

    // ---------------- MCQ (2D) ----------------

    private void ShowMCQ(ChallengeData challenge)
    {
        // Validate challenge data
        if (challenge == null || challenge.type != ChallengeType.MCQ)
        {
            resultText.text = "Invalid MCQ challenge data";
            Debug.LogError("[ChallengeRunner] Invalid MCQ challenge");
            StartCoroutine(DelayedComplete(false, 0));
            return;
        }


        if (string.IsNullOrEmpty(challenge.question))
        {
            resultText.text = "Question missing";
            Debug.LogError("[ChallengeRunner] MCQ question is missing");
            StartCoroutine(DelayedComplete(false, 0));
            return;
        }

        if (challenge.options == null || challenge.options.Count == 0)
        {
            resultText.text = "Options missing";
            Debug.LogError("[ChallengeRunner] MCQ options missing. Data invalid: options missing.");
            StartCoroutine(DelayedComplete(false, 0));
            return;
        }

        mcqPanel.SetActive(true);
        minigamePanel.SetActive(false);
        questionText.text = challenge.question;
        UpdateAttemptsText();



    var shuffled = new List<MCQOption>(challenge.options);
        shuffled = shuffled.OrderBy(_ => Random.value).ToList();

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

    private void OnAnswerSelected(bool isCorrect)
    {
        if (!challengeInProgress) return;

        if (isCorrect)
        {
            resultText.text = "Correct!";
            OnChallengeComplete(true, currentChallenge.bonusPoints);
            return;
        }

        attemptsLeft--;
        UpdateAttemptsText();

        if (attemptsLeft <= 0)
        {
            resultText.text = "Challenge failed. Try again!";
            OnChallengeComplete(false, 0);
        }
        else
        {
            resultText.text = $"Wrong! {attemptsLeft} attempt(s) left.";
        }
    }

    // ---------------- AR MCQ ----------------

    private void ShowARMCQ(ChallengeData challenge)
    {
        if (challenge == null || challenge.type != ChallengeType.ARMCQ)
        {
            resultText.text = "Invalid AR MCQ challenge data";
            Debug.LogError("[ChallengeRunner] Invalid AR MCQ challenge");
            StartCoroutine(DelayedComplete(false, 0));
            return;
        }

        if (string.IsNullOrEmpty(challenge.question))
        {
            resultText.text = "❌ Question missing";
            Debug.LogError("[ChallengeRunner] AR MCQ question is missing");
            StartCoroutine(DelayedComplete(false, 0));
            return;
        }

        if (challenge.options == null || challenge.options.Count == 0)
        {
            resultText.text = "❌ Options missing";
            Debug.LogError("[ChallengeRunner] AR MCQ options missing. Need at least 2 options.");
            StartCoroutine(DelayedComplete(false, 0));
            return;
        }

        if (challenge.bonusPoints <= 0)
        {
            Debug.LogWarning("[ChallengeRunner] AR MCQ bonus points not set, defaulting to 0");
            challenge.bonusPoints = 0;
        }

        if (arMcqManager == null)
        {
            Debug.LogError("[ChallengeRunner] ArMCQManager not assigned. Falling back to 2D MCQ.");
            challenge.type = ChallengeType.MCQ;
            ShowMCQ(challenge);
            return;
        }

        // Hide 2D panels
        mcqPanel.SetActive(false);
        minigamePanel.SetActive(false);
        challengePanel.SetActive(false);

        Debug.Log($"[ChallengeRunner] Starting AR MCQ: '{challenge.question}' with {challenge.options.Count} options");

        // Start AR MCQ
        arMcqManager.StartARMCQ(challenge, OnARMCQComplete);
    }

    private void OnARMCQComplete(bool success, int bonus)
    {
        // Handle fallback from AR to 2D MCQ
        if (ArMCQManager.IsARFallbackResult(success, bonus))
        {
            Debug.Log("[ChallengeRunner] AR unavailable, falling back to 2D MCQ");
            challengePanel.SetActive(true);
            mcqPanel.SetActive(true);
            currentChallenge.type = ChallengeType.MCQ;
            ShowMCQ(currentChallenge);
            return;
        }

        // Normal completion
        OnChallengeComplete(success, bonus);
    }



    // ---------------- Minigames ----------------

    private void ShowMinigameLauncher(ChallengeData challenge)
    {

        mcqPanel.SetActive(false);
        minigamePanel.SetActive(true);
        minigameNameText.text = $"{challenge.minigameId}";
        if (timerText != null) timerText.text = $"Time: {challenge.timeLimitSeconds}s";
    }

    private void OnLaunchMinigameClicked()
    {
        if (currentChallenge != null) StartCoroutine(LaunchMinigame(currentChallenge));
    }

    private IEnumerator LaunchMinigame(ChallengeData challenge)
    {
        awaitingRetryChoice = false;
        minigamePanel.SetActive(false);
        if (retryButton != null) retryButton.gameObject.SetActive(false);

        switch (challenge.minigameId)
        {
            case "MemoryMatch":
                if (memoryMatchManager != null)
                {
                    Debug.Log("[ChallengeRunner] Starting Memory Match game");
                    memoryMatchManager.StartGame(challenge, (success) =>
                    {
                        Debug.Log($"[ChallengeRunner] Memory Match result: {success}");
                        OnMinigameResult(success, success ? currentChallenge.bonusPoints : 0);
                    });
                    yield break;
                }
                Debug.LogError("[ChallengeRunner] memoryMatchManager is NULL");
                OnMinigameResult(false, 0);
                yield break;
        
                OnMinigameResult(false, 0);
                yield break;

            case "OrderSequence":
                if (orderSequenceMinigame != null)
                {
                    challengePanel.SetActive(false);
                    orderSequenceMinigame.StartMinigame((success, score) => OnMinigameResult(success, score));
                    yield break;
                }
                OnMinigameResult(false, 0);
                yield break;

            default:
                OnMinigameResult(false, 0);
                yield break;
        }
    }

    private void OnMinigameResult(bool success, int score)
    {
        if (memoryMatchManager != null) memoryMatchManager.StopGame();
        if (orderSequenceMinigame != null) orderSequenceMinigame.StopMinigame();

        challengePanel.SetActive(true);

        if (success)
        {
            resultText.text = "Minigame completed!";
            OnChallengeComplete(true, currentChallenge.bonusPoints);
        }
        else
        {
            awaitingRetryChoice = true;
            resultText.text = "Failed. Retry or Skip?";
            minigamePanel.SetActive(true);
            if (retryButton != null) retryButton.gameObject.SetActive(true);
            if (skipButton != null) skipButton.gameObject.SetActive(true);
        }
    }

    // ---------------- Shared ----------------

    private void OnRetryClicked()
    {
        if (!awaitingRetryChoice || currentChallenge == null) return;
        resultText.text = "";
        StartCoroutine(LaunchMinigame(currentChallenge));
    }

    private void OnSkipClicked()
    {
        if (!challengeInProgress) return;


        OnChallengeComplete(false, 0);
    }

    private void UpdateAttemptsText()
    {
        if (attemptsText != null) attemptsText.text = $"Attempts: {attemptsLeft}";
    }

    private void OnChallengeComplete(bool success, int bonusPoints)
    {
        challengeInProgress = false;
        StartCoroutine(DelayedComplete(success, bonusPoints));
    }

    private IEnumerator DelayedComplete(bool success, int bonus)
    {
        yield return new WaitForSeconds(1.5f);

        

        challengePanel.SetActive(false);
        mcqPanel.SetActive(false);
        minigamePanel.SetActive(false);

        if (retryButton != null) retryButton.gameObject.SetActive(false);
        if (skipButton != null) skipButton.gameObject.SetActive(false);

        onComplete?.Invoke(success, bonus);
    }
}