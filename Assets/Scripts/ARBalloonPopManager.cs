using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;

/// <summary>
/// Self-contained AR minigame manager for "Pop the Balloon".
/// Spawns balloons on detected AR planes, waits for taps, runs a countdown timer.
/// Wire up via the Inspector or from <see cref="ChallengeRunner"/>.
/// </summary>
public class ARBalloonPopManager : MonoBehaviour
{
    [Header("AR Components")]
    [SerializeField] private ARRaycastManager arRaycastManager;
    [SerializeField] private ARPlaneManager   arPlaneManager;

    [Header("Prefab")]
    [SerializeField] private GameObject balloonPrefab;

    [Header("UI")]
    [SerializeField] private GameObject gamePanel;
    [SerializeField] private TMP_Text   timerText;
    [SerializeField] private TMP_Text   balloonsLeftText;
    [SerializeField] private TMP_Text   resultText;

    // ── Runtime state ─────────────────────────────────────────────────────────
    private int _totalBalloons;
    private int _balloonsRemaining;
    private readonly List<GameObject> _spawnedBalloons = new();
    private System.Action<bool> _onResult;
    private ChallengeData _challenge;
    private bool _gameActive;
    private Coroutine _timerCoroutine;

    // Layer mask cached once
    private int _balloonLayerMask;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        int layer = LayerMask.NameToLayer("Balloon");
        _balloonLayerMask = layer >= 0 ? (1 << layer) : ~0;

        if (gamePanel != null) gamePanel.SetActive(false);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a balloon-pop minigame for the given challenge.
    /// </summary>
    /// <param name="challenge">Challenge data; reads <c>minigameId</c> for difficulty.</param>
    /// <param name="onResult">Callback: true = win, false = fail.</param>
    public void StartGame(ChallengeData challenge, System.Action<bool> onResult)
    {
        _challenge = challenge;
        _onResult  = onResult;

        // Determine difficulty from minigameId
        bool isHard = challenge.minigameId == "BalloonPop_Hard";
        _totalBalloons     = isHard ? 10 : 5;

        // Use the configured time limit; fall back to difficulty defaults if not set
        int defaultTime   = isHard ? 20 : 30;
        int timeLimitSecs = challenge.timeLimitSeconds > 0 ? challenge.timeLimitSeconds : defaultTime;

        _balloonsRemaining = _totalBalloons;
        _gameActive        = true;
        _spawnedBalloons.Clear();

        if (gamePanel != null) gamePanel.SetActive(true);
        if (resultText != null) resultText.text = "";

        UpdateBalloonsLeftText();
        UpdateTimerText(timeLimitSecs);

        // Enable plane detection and start the spawn flow
        if (arPlaneManager != null) arPlaneManager.enabled = true;

        StartCoroutine(SpawnFlow(timeLimitSecs));
    }

    /// <summary>
    /// Safe to call at any time — destroys all spawned balloons, stops coroutines, hides UI.
    /// </summary>
    public void StopGame()
    {
        _gameActive = false;
        StopAllCoroutines();
        DestroyAllBalloons();

        if (gamePanel != null) gamePanel.SetActive(false);
        if (arPlaneManager != null) arPlaneManager.enabled = false;
    }

    // ── Update — tap detection ─────────────────────────────────────────────────

    private void Update()
    {
        if (!_gameActive) return;
        if (Camera.main == null) return;

        // 1) Get a tap/click position
        bool tapped = false;
        Vector2 tapPosition = default;

#if UNITY_EDITOR
    if (Input.GetMouseButtonDown(0))
    {
        tapped = true;
        tapPosition = Input.mousePosition;
    }
#else
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            tapped = true;
            tapPosition = Input.GetTouch(0).position;
        }
#endif

        if (!tapped) return;

        // 2) Raycast into the 3D world for balloons
        Ray tapRay = Camera.main.ScreenPointToRay(tapPosition);
        if (Physics.Raycast(tapRay, out RaycastHit tapHit, Mathf.Infinity, _balloonLayerMask))
        {
            // Prefer BalloonBehaviour on parent (common if collider is on child)
            BalloonBehaviour balloon =
                tapHit.collider.GetComponentInParent<BalloonBehaviour>() ??
                tapHit.collider.GetComponent<BalloonBehaviour>();

            balloon?.Pop();
        }
    }

    // ── Coroutines ────────────────────────────────────────────────────────────

    private IEnumerator SpawnFlow(int timeLimitSecs)
    {
        // Wait up to 5 s for at least one AR plane to be detected
        float waitStart = Time.time;
        while (Time.time - waitStart < 5f)
        {
            if (arPlaneManager != null && arPlaneManager.trackables.count > 0) break;
            yield return null;
        }

        // Spawn all balloons
        for (int i = 0; i < _totalBalloons; i++)
            SpawnBalloon();

        // Disable plane visualisations after spawning
        if (arPlaneManager != null) arPlaneManager.enabled = false;

        // Start the countdown
        _timerCoroutine = StartCoroutine(CountdownCoroutine(timeLimitSecs));
    }

    private IEnumerator CountdownCoroutine(int seconds)
    {
        int remaining = seconds;
        while (remaining > 0)
        {
            UpdateTimerText(remaining);
            yield return new WaitForSeconds(1f);
            remaining--;
        }

        UpdateTimerText(0);
        if (_gameActive) CompleteGame(false);
    }

    // ── Spawn helpers ─────────────────────────────────────────────────────────

    private void SpawnBalloon()
    {
        if (balloonPrefab == null)
        {
            Debug.LogWarning("[ARBalloonPopManager] balloonPrefab is not assigned.");
            return;
        }

        Vector3 spawnPos = TryGetARPlanePosition() ?? FallbackPosition();

        GameObject go = Instantiate(balloonPrefab, spawnPos, Quaternion.identity);
        _spawnedBalloons.Add(go);

        BalloonBehaviour balloon = go.GetComponent<BalloonBehaviour>();
        if (balloon != null)
            balloon.OnPopped += OnBalloonPopped;
    }

    /// <summary>
    /// Tries to find a surface position via AR plane raycast from a random screen point.
    /// Returns null if no plane is hit.
    /// </summary>
    private Vector3? TryGetARPlanePosition()
    {
        
        if (arRaycastManager == null)
        {
            Debug.LogWarning("[BalloonPop] arRaycastManager is null.");
            return null;
        }

        // Try a few random screen positions
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Vector2 screenPoint = new Vector2(
                Random.Range(Screen.width  * 0.1f, Screen.width  * 0.9f),
                Random.Range(Screen.height * 0.1f, Screen.height * 0.9f));

            var hits = new List<ARRaycastHit>();
            if (arRaycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinPolygon))
            {
                Pose hitPose = hits[0].pose;
                return hitPose.position + Vector3.up * 0.3f;
            }
        }
        Debug.Log("[BalloonPop] No plane hit found (try scanning environment).");
        return null;
    }

    /// <summary>Camera-forward fallback with a small random offset.</summary>
    private Vector3 FallbackPosition()
    {
        if (Camera.main == null) return Vector3.zero;

        Transform cam = Camera.main.transform;
        Vector3 offset = new Vector3(
            Random.Range(-0.5f, 0.5f),
            Random.Range(-0.2f, 0.4f),
            0f);
        return cam.position + cam.forward * 2f + offset;
    }

    // ── Game logic ────────────────────────────────────────────────────────────

    private void OnBalloonPopped()
    {
        if (!_gameActive) return;

        _balloonsRemaining--;
        UpdateBalloonsLeftText();

        if (_balloonsRemaining <= 0)
            CompleteGame(true);
    }

    private void CompleteGame(bool success)
    {
        if (!_gameActive) return;
        _gameActive = false;

        if (_timerCoroutine != null)
        {
            StopCoroutine(_timerCoroutine);
            _timerCoroutine = null;
        }

        DestroyAllBalloons();

        if (resultText != null)
            resultText.text = success ? "🎈 All popped! Great job!" : "⏰ Time's up!";

        StartCoroutine(EndSequence(success));
    }

    private IEnumerator EndSequence(bool success)
    {
        yield return new WaitForSeconds(2f);

        if (gamePanel != null) gamePanel.SetActive(false);
        if (arPlaneManager != null) arPlaneManager.enabled = false;

        _onResult?.Invoke(success);
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private void UpdateTimerText(int seconds)
    {
        if (timerText != null)
            timerText.text = $"Time: {seconds}s";
    }

    private void UpdateBalloonsLeftText()
    {
        if (balloonsLeftText != null)
            balloonsLeftText.text = $"Balloons: {_balloonsRemaining}/{_totalBalloons}";
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void DestroyAllBalloons()
    {
        foreach (GameObject go in _spawnedBalloons)
        {
            if (go != null) Destroy(go);
        }
        _spawnedBalloons.Clear();
    }
}
