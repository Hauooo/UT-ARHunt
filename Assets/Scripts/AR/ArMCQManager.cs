using UnityEngine;
using UnityEngine.XR.ARFoundation;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Orchestrates the AR-based MCQ flow:
///  1. Shows the question on the 2D HUD.
///  2. Delegates plane-detection and option spawning to <see cref="ARMCQOptionSpawner"/>.
///  3. Performs raycasting to detect which AR option the player tapped.
///  4. Evaluates the answer, handles retries, and reports the final result.
///
/// Attach this to a persistent AR GameObject that also holds
/// <see cref="ARMCQOptionSpawner"/>, <see cref="ARPlaneManager"/> and
/// <see cref="ARRaycastManager"/>.
/// </summary>
public class ArMCQManager : MonoBehaviour
{
    // ── Inspector references ─────────────────────────────────────────────────

    [Header("AR Components")]
    [SerializeField] private ARRaycastManager arRaycastManager;
    [SerializeField] private ARMCQOptionSpawner optionSpawner;

    [Header("HUD – always on screen")]
    [SerializeField] private GameObject arMCQHUD;
    [SerializeField] private TMP_Text questionText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text attemptsText;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private ChallengeData currentChallenge;
    private int attemptsLeft;
    private System.Action<bool, int> onComplete;
    private bool waitingForTap;
    private bool arAvailable;

    private readonly List<ARRaycastHit> raycastHits = new List<ARRaycastHit>();

    // ─── Public entry point ───────────────────────────────────────────────────

    /// <summary>
    /// Start the AR MCQ experience.
    /// Call this from <see cref="ChallengeRunner"/> instead of the normal ShowMCQ path.
    /// </summary>
    public void StartARMCQ(ChallengeData challenge, System.Action<bool, int> completionCallback)
    {
        currentChallenge = challenge;
        onComplete = completionCallback;
        attemptsLeft = challenge.maxAttempts;

        arAvailable = arRaycastManager != null && optionSpawner != null;

        if (arMCQHUD != null) arMCQHUD.SetActive(true);
        if (questionText != null) questionText.text = challenge.question;

        UpdateAttemptsText();

        if (!arAvailable)
        {
            // AR components not assigned – skip straight to screen fallback
            NotifyFallback();
            return;
        }

        SetStatus("📷 Scan a flat surface to place the answers…");

        // Shuffle options so the correct answer is not always in the same slot
        var shuffled = currentChallenge.options.OrderBy(_ => Random.value).ToList();

        optionSpawner.OnOptionsSpawned = OnOptionsSpawned;
        optionSpawner.OnDetectionTimeout = OnDetectionTimeout;
        optionSpawner.BeginSpawning(shuffled);
    }

    /// <summary>
    /// Stop and clean up everything (called by ChallengeRunner when needed).
    /// </summary>
    public void StopARMCQ()
    {
        waitingForTap = false;

        if (optionSpawner != null)
        {
            optionSpawner.OnOptionsSpawned = null;
            optionSpawner.OnDetectionTimeout = null;
            optionSpawner.ClearOptions();
        }

        if (arMCQHUD != null) arMCQHUD.SetActive(false);
    }

    // ─── Callbacks from ARMCQOptionSpawner ───────────────────────────────────

    private void OnOptionsSpawned()
    {
        SetStatus("Tap an answer!");
        waitingForTap = true;

        // Register tap listener on every spawned option
        foreach (var opt in optionSpawner.SpawnedOptions)
        {
            if (opt != null)
                opt.OnSelected += HandleOptionSelected;
        }
    }

    private void OnDetectionTimeout()
    {
        SetStatus("No surface found. Falling back to screen mode…");
        StartCoroutine(DelayedFallback(1.5f));
    }

    // ─── Touch / click input ─────────────────────────────────────────────────

    private void Update()
    {
        if (!waitingForTap) return;

        // Mobile touch
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
                ProcessScreenPoint(touch.position);
            return;
        }

        // Editor / PC mouse click
        if (Input.GetMouseButtonDown(0))
            ProcessScreenPoint(Input.mousePosition);
    }

    private void ProcessScreenPoint(Vector2 screenPos)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // Try AR raycast first (hits AR tracked objects)
        if (arRaycastManager != null &&
            arRaycastManager.Raycast(screenPos, raycastHits,
                UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinBounds))
        {
            // AR raycast found a plane hit – but we still need to check if a
            // spawned option collider was hit via the regular Physics raycast.
        }

        // Physics raycast to detect taps on 3D option colliders
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            MCQOptionUI option = hit.collider.GetComponent<MCQOptionUI>();
            if (option != null)
                option.OnTapped();
        }
    }

    // ─── Answer handling ──────────────────────────────────────────────────────

    private void HandleOptionSelected(MCQOptionUI selected)
    {
        if (!waitingForTap) return;
        waitingForTap = false;

        // Show result on all options
        foreach (var opt in optionSpawner.SpawnedOptions)
        {
            if (opt != null)
            {
                opt.SetInteractionEnabled(false);
                opt.ShowResult(opt.IsCorrect);
            }
        }

        if (selected.IsCorrect)
        {
            SetStatus("✅ Correct!");
            StartCoroutine(FinishAfterDelay(true, currentChallenge.bonusPoints, 1.5f));
            return;
        }

        attemptsLeft--;
        UpdateAttemptsText();

        if (attemptsLeft <= 0)
        {
            SetStatus("❌ Failed.");
            StartCoroutine(FinishAfterDelay(false, 0, 1.5f));
        }
        else
        {
            SetStatus($"❌ Wrong! {attemptsLeft} attempt(s) left. Scanning for new surface…");
            StartCoroutine(RetryAfterDelay(1.5f));
        }
    }

    // ─── Retry / complete helpers ─────────────────────────────────────────────

    private IEnumerator RetryAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        optionSpawner.ClearOptions();

        // Shuffle options again for the retry
        var shuffled = currentChallenge.options.OrderBy(_ => Random.value).ToList();

        optionSpawner.BeginSpawning(shuffled);
        SetStatus("📷 Scan a flat surface to place the answers…");
    }

    private IEnumerator FinishAfterDelay(bool success, int bonus, float delay)
    {
        yield return new WaitForSeconds(delay);
        Finish(success, bonus);
    }

    private IEnumerator DelayedFallback(float delay)
    {
        yield return new WaitForSeconds(delay);
        NotifyFallback();
    }

    private void Finish(bool success, int bonus)
    {
        StopARMCQ();
        onComplete?.Invoke(success, bonus);
    }

    /// <summary>
    /// Signals ChallengeRunner to fall back to screen-based MCQ.
    /// Re-uses the same completion callback with special sentinel values
    /// (success=false, bonus=-1) that ChallengeRunner interprets as a
    /// fallback request.
    /// </summary>
    private void NotifyFallback()
    {
        StopARMCQ();
        onComplete?.Invoke(false, ArMCQManager.FallbackBonusSentinel);
    }

    // ─── UI helpers ───────────────────────────────────────────────────────────

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private void UpdateAttemptsText()
    {
        if (attemptsText != null)
            attemptsText.text = $"Attempts left: {attemptsLeft}";
    }

    // ─── Constants ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sentinel bonus value returned when AR is unavailable and
    /// <see cref="ChallengeRunner"/> should fall back to screen-based MCQ.
    /// </summary>
    public const int FallbackBonusSentinel = -1;

    /// <summary>
    /// Returns true when the completion result signals that AR was unavailable
    /// and the caller should fall back to screen-based MCQ.
    /// </summary>
    public static bool IsARFallbackResult(bool success, int bonus)
        => !success && bonus == FallbackBonusSentinel;
}
