using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;  // ← ADDED

/// <summary>
/// Order Sequence Minigame
/// Player must tap buttons in the correct order to match the sequence shown
/// </summary>
public class OrderSequenceMinigame : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject minigamePanel;
    [SerializeField] private TextMeshProUGUI instructionText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private GameObject buttonPrefab;
    [SerializeField] private Button submitButton;

    [Header("Minigame Settings")]
    [SerializeField] private int startSequenceLength = 3;
    [SerializeField] private int maxSequenceLength = 5;
    [SerializeField] private float sequenceDisplayTime = 2f;
    [SerializeField] private float delayBetweenButtons = 0.5f;
    [SerializeField] private Color activeButtonColor = Color.green;
    [SerializeField] private Color normalButtonColor = Color.white;
    [SerializeField] private Color errorButtonColor = Color.red;

    private List<int> sequence = new List<int>();
    private List<int> playerInput = new List<int>();
    private List<Button> buttons = new List<Button>();
    private bool isSequenceDisplaying = false;
    private bool acceptingInput = false;
    private int currentSequenceLength = 0;

    // Callback
    private System.Action<bool, int> onComplete;

    private void Start()
    {
        if (minigamePanel == null || statusText == null || instructionText == null ||
            buttonContainer == null || buttonPrefab == null || submitButton == null)
        {
            Debug.LogError("[OrderSequence] Missing inspector references.");
            enabled = false;
            return;
        }

        SetupButtons();
        submitButton.onClick.AddListener(SubmitSequence);
    }

    /// <summary>
    /// Start the minigame
    /// </summary>
    public void StartMinigame(System.Action<bool, int> onCompleteCallback)
    {
        onComplete = onCompleteCallback;
        minigamePanel.SetActive(true);

        sequence.Clear();
        playerInput.Clear();
        currentSequenceLength = startSequenceLength;

        instructionText.text = "Watch the sequence and tap the buttons in order!";
        StartCoroutine(PlayRound());
    }

    private IEnumerator PlayRound()
    {
        // Generate next sequence
        for (int i = sequence.Count; i < currentSequenceLength; i++)
        {
            sequence.Add(Random.Range(0, buttons.Count));
        }

        Debug.Log($"[OrderSequence] Sequence: {string.Join(", ", sequence)}");

        // Show sequence
        yield return StartCoroutine(DisplaySequence());

        // Wait a moment before accepting input
        yield return new WaitForSeconds(0.5f);

        // Accept player input
        acceptingInput = true;
        playerInput.Clear();
        statusText.text = "Your turn! Tap the buttons.";
    }

    private IEnumerator DisplaySequence()
    {
        isSequenceDisplaying = true;
        acceptingInput = false;

        if (statusText == null)
        {
            Debug.LogError("[OrderSequence] statusText is NULL");
            yield break;
        }

        statusText.text = "Watch carefully...";

        yield return new WaitForSeconds(sequenceDisplayTime);

        if (buttons == null || buttons.Count == 0)
        {
            Debug.LogError("[OrderSequence] buttons list empty");
            yield break;
        }

        foreach (int buttonIndex in sequence)
        {
            if (buttonIndex < 0 || buttonIndex >= buttons.Count)
            {
                Debug.LogError($"[OrderSequence] invalid buttonIndex={buttonIndex}, count={buttons.Count}");
                continue;
            }

            yield return FlashButton(buttonIndex);
            yield return new WaitForSeconds(delayBetweenButtons);
        }

        isSequenceDisplaying = false;
    }

    private IEnumerator FlashButton(int buttonIndex)
    {
        var btn = buttons[buttonIndex];
        if (btn == null)
        {
            Debug.LogError($"[OrderSequence] buttons[{buttonIndex}] is NULL");
            yield break;
        }

        var img = btn.GetComponent<Image>();
        if (img == null)
        {
            Debug.LogError($"[OrderSequence] Button {buttonIndex} missing Image on prefab.");
            yield break;
        }

        Color original = img.color;
        img.color = activeButtonColor;
        yield return new WaitForSeconds(0.4f);
        img.color = original;
        yield return new WaitForSeconds(0.2f);
    }

    private void SetupButtons()
    {
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);

        buttons.Clear();

        int buttonCount = 4;
        for (int i = 0; i < buttonCount; i++)
        {
            var go = Instantiate(buttonPrefab, buttonContainer);
            var btn = go.GetComponent<Button>();
            var img = go.GetComponent<Image>();
            var txt = go.GetComponentInChildren<TextMeshProUGUI>();

            if (btn == null || img == null || txt == null) { Debug.LogError("Bad buttonPrefab"); Destroy(go); continue; }

            img.color = normalButtonColor;
            txt.text = (i + 1).ToString();

            int buttonIndex = i;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnButtonTapped(buttonIndex));

            buttons.Add(btn);
        }

        Debug.Log($"[OrderSequence] Setup {buttons.Count} buttons");
    }

    private void OnButtonTapped(int buttonIndex)
    {
        if (!acceptingInput || isSequenceDisplaying)
        {
            Debug.Log("[OrderSequence] Input not accepted right now");
            return;
        }

        Debug.Log($"[OrderSequence] Button {buttonIndex} tapped");

        StartCoroutine(FlashButton(buttonIndex));
        playerInput.Add(buttonIndex);

        // Check if player made a mistake
        if (playerInput[playerInput.Count - 1] != sequence[playerInput.Count - 1])
        {
            Debug.Log("[OrderSequence] WRONG! Game Over");
            OnSequenceFailed();
            return;
        }

        // Check if player completed the sequence
        if (playerInput.Count == sequence.Count)
        {
            Debug.Log("[OrderSequence] Sequence complete!");
            OnSequenceComplete();
        }
    }

    private void OnSequenceComplete()
    {
        acceptingInput = false;

        // Check if we've reached max difficulty
        if (currentSequenceLength >= maxSequenceLength)
        {
            Debug.Log("[OrderSequence] Max difficulty reached - VICTORY!");
            statusText.text = "Perfect! You've mastered it!";
            CompleteMinigame(true, 100);
        }
        else
        {
            // Next round with longer sequence
            currentSequenceLength++;
            statusText.text = $"Great! Next round with {currentSequenceLength} buttons...";
            StartCoroutine(PlayRound());
        }
    }

    private void OnSequenceFailed()
    {
        acceptingInput = false;
        statusText.text = "Wrong! Game Over.";

        // Flash all buttons red
        foreach (var btn in buttons)
        {
            btn.GetComponent<Image>().color = errorButtonColor;
        }

        CompleteMinigame(false, CalculateScore());
    }

    private void SubmitSequence()
    {
        // Optional: allow player to submit incomplete sequence
        if (playerInput.Count > 0)
        {
            bool success = playerInput.Count == sequence.Count &&
                          playerInput.SequenceEqual(sequence);  // ← Now works with System.Linq

            if (success)
            {
                OnSequenceComplete();
            }
            else
            {
                OnSequenceFailed();
            }
        }
    }

    private int CalculateScore()
    {
        // Score based on how many correct buttons tapped
        int correctCount = 0;
        for (int i = 0; i < playerInput.Count && i < sequence.Count; i++)
        {
            if (playerInput[i] == sequence[i])
                correctCount++;
            else
                break;
        }

        // Scale score: 0-100
        return Mathf.RoundToInt((float)correctCount / currentSequenceLength * 100f);
    }

    private void CompleteMinigame(bool success, int score)
    {
        minigamePanel.SetActive(false);
        onComplete?.Invoke(success, score);
        Debug.Log($"[OrderSequence] Minigame complete - Success: {success}, Score: {score}");
    }

    /// <summary>
    /// Stop the minigame (cleanup)
    /// </summary>
    public void StopMinigame()
    {
        StopAllCoroutines();
        minigamePanel.SetActive(false);
        acceptingInput = false;
        Debug.Log("[OrderSequence] Minigame stopped");
    }

    private void CloseMinigame()
    {
        CompleteMinigame(false, 0);
    }
}