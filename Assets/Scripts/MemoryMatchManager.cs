using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class MemoryMatchManager : MonoBehaviour
{
    [Header("Grid Settings")]
    [SerializeField] private GridLayoutGroup gridLayout;
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private int gridRows = 4;
    [SerializeField] private int gridCols = 4;

    [Header("Card Content")]
    [SerializeField] private Sprite[] cardSprites;      // Images for cards
    [SerializeField] private string[] cardTexts;        // Alternative: text labels

    [Header("UI")]
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text matchesText;
    [SerializeField] private GameObject gamePanel;
    [SerializeField] private Button skipButton;

    [Header("Game Settings")]
    [SerializeField] private int timeLimitSeconds = 60;

    // Game state
    private List<CardBehaviour> allCards = new List<CardBehaviour>();
    private CardBehaviour firstFlippedCard;
    private CardBehaviour secondFlippedCard;
    private int matchesFound = 0;
    private int totalPairs;
    private bool canFlip = true;
    private int timeRemaining;
    private Coroutine timerCoroutine;

    private System.Action<bool> onGameComplete;
    private ChallengeData currentChallenge;

    private void Awake()
    {
        if (gamePanel != null) gamePanel.SetActive(false);
    }

    private void Start()
    {
        if (skipButton != null)
            skipButton.onClick.AddListener(SkipGame);
    }

    /// <summary>
    /// Start the memory match game
    /// </summary>
    public void StartGame(ChallengeData challenge, System.Action<bool> onComplete)
    {
        currentChallenge = challenge;
        onGameComplete = onComplete;

        

        timeRemaining = challenge.timeLimitSeconds > 0 ? challenge.timeLimitSeconds : 60;

        CreateGrid();
        gamePanel.SetActive(true);

        timerCoroutine = StartCoroutine(CountdownCoroutine());
    }

    private void CreateGrid()
    {
        foreach (Transform child in gridLayout.transform)
            Destroy(child.gameObject);

        allCards.Clear();
        matchesFound = 0;
        firstFlippedCard = null;
        secondFlippedCard = null;
        canFlip = true;

        totalPairs = (gridRows * gridCols) / 2;

        // ← ADD THIS: Set GridLayoutGroup constraint based on gridCols
        if (gridLayout != null)
        {
            gridLayout.constraintCount = gridCols;  // This controls how many columns!
            Debug.Log($"[MemoryMatch] GridLayout constraint set to {gridCols} columns");
        }

        List<int> cardIds = new List<int>();
        for (int i = 0; i < totalPairs; i++)
        {
            cardIds.Add(i);
            cardIds.Add(i);
        }

        // Shuffle...
        for (int i = cardIds.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);
            int temp = cardIds[i];
            cardIds[i] = cardIds[randomIndex];
            cardIds[randomIndex] = temp;
        }

        for (int i = 0; i < gridRows * gridCols; i++)
        {
            GameObject cardObj = Instantiate(cardPrefab, gridLayout.transform);
            CardBehaviour card = cardObj.GetComponent<CardBehaviour>();

            int cardId = cardIds[i];

            Sprite sprite = cardSprites != null && cardId < cardSprites.Length
                ? cardSprites[cardId]
                : null;

            string text = cardTexts != null && cardId < cardTexts.Length
                ? cardTexts[cardId]
                : $"{cardId}";

            card.SetupCard(cardId, sprite, text);
            card.OnCardFlipped += OnCardFlipped;

            allCards.Add(card);
        }

        UpdateMatchesText();
    }

    private void OnCardFlipped(CardBehaviour card)
    {
        if (!canFlip) return;

        if (firstFlippedCard == null)
        {
            firstFlippedCard = card;
        }
        else if (secondFlippedCard == null && card != firstFlippedCard)
        {
            secondFlippedCard = card;
            canFlip = false;

            // Check if cards match
            StartCoroutine(CheckMatchCoroutine());
        }
    }

    private IEnumerator CheckMatchCoroutine()
    {
        yield return new WaitForSeconds(0.5f);

        if (firstFlippedCard.GetCardId() == secondFlippedCard.GetCardId())
        {
            // Match found!
            firstFlippedCard.MatchCard();
            secondFlippedCard.MatchCard();
            matchesFound++;

            UpdateMatchesText();

            // Check if won
            if (matchesFound >= totalPairs)
            {
                CompleteGame(true);
                yield break;
            }
        }
        else
        {
            // No match - flip back
            firstFlippedCard.UnflipCard();
            secondFlippedCard.UnflipCard();
        }

        firstFlippedCard = null;
        secondFlippedCard = null;
        canFlip = true;
    }

    private IEnumerator CountdownCoroutine()
    {
        while (timeRemaining > 0)
        {
            UpdateTimerText();
            yield return new WaitForSeconds(1f);
            timeRemaining--;
        }

        UpdateTimerText();
        CompleteGame(false);  // Time's up
    }

    private void UpdateTimerText()
    {
        if (timerText != null)
            timerText.text = $"Time: {timeRemaining}s";
    }

    private void UpdateMatchesText()
    {
        if (matchesText != null)
            matchesText.text = $"Matches: {matchesFound}/{totalPairs}";
    }

    private void CompleteGame(bool success)
    {
        canFlip = false;

        if (timerCoroutine != null)
            StopCoroutine(timerCoroutine);

        if (success)
        {
            if (matchesText != null)
                matchesText.text = "🎉 You Won!";
        }
        else
        {
            if (matchesText != null)
                matchesText.text = "⏰ Time's Up!";
        }

        Invoke(nameof(HideGame), 1.5f);
        onGameComplete?.Invoke(success);
    }

    private void HideGame()
    {
        gamePanel.SetActive(false);
    }

    private void SkipGame()
    {
        CompleteGame(false);
    }

    public void StopGame()
    {
        canFlip = false;
        if (timerCoroutine != null)
            StopCoroutine(timerCoroutine);
        gamePanel.SetActive(false);
    }
}