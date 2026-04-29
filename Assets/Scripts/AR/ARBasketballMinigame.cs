using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.UI;

public class ARBasketballMinigame : MonoBehaviour
{
    [Header("Basketball Setup")]
    [SerializeField] private GameObject basketballPrefab;
    [SerializeField] private GameObject hoopPrefab;
    [SerializeField] private float throwForce = 20f;
    [SerializeField] private float timeLimit = 30f;
    [SerializeField] private int targetScore = 5; // Make 5 baskets to win

    [Header("UI")]
    [SerializeField] private GameObject gamePanel;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text instructionText;
    [SerializeField] private Button skipButton;

    [Header("AR")]
    [SerializeField] private UnityEngine.XR.ARFoundation.ARRaycastManager arRaycastManager;

    private GameObject currentBall;
    private GameObject hoop;
    private int score = 0;
    private float timeRemaining;
    private bool gameActive = false;
    private Coroutine gameCoroutine;
    private System.Action<bool, int> onComplete;

    private void Awake()
    {
        if (gamePanel != null) gamePanel.SetActive(false);
    }

    private void Start()
    {
        if (skipButton != null)
        {
            skipButton.onClick.RemoveAllListeners();
            skipButton.onClick.AddListener(SkipGame);
        }
    }

    public void StartGame(System.Action<bool, int> onCompleteCallback)
    {
        onComplete = onCompleteCallback;
        gamePanel.SetActive(true);
        score = 0;
        timeRemaining = timeLimit;
        gameActive = true;

        instructionText.text = "Tap and drag to aim, release to shoot!";
        UpdateUI();

        SpawnHoop();
        SpawnBall();

        gameCoroutine = StartCoroutine(GameLoop());
    }

    private void SpawnHoop()
    {
        if (hoopPrefab == null)
        {
            Debug.LogError("[Basketball] hoopPrefab not assigned");
            return;
        }

        // Spawn hoop 3 meters in front of camera
        Vector3 spawnPos = Camera.main.transform.position + Camera.main.transform.forward * 3f;
        spawnPos.y = Camera.main.transform.position.y + 1.5f; // Head height

        hoop = Instantiate(hoopPrefab, spawnPos, Quaternion.identity);
        hoop.name = "Hoop";
        Debug.Log("[Basketball] Hoop spawned at: " + spawnPos);
    }

    private void SpawnBall()
    {
        if (basketballPrefab == null)
        {
            Debug.LogError("[Basketball] basketballPrefab not assigned");
            return;
        }

        // Spawn ball in front of player
        Vector3 spawnPos = Camera.main.transform.position + Camera.main.transform.forward * 1.5f;
        spawnPos.y = Camera.main.transform.position.y - 0.3f; // Waist height

        currentBall = Instantiate(basketballPrefab, spawnPos, Quaternion.identity);
        currentBall.name = "Basketball";

        // Add physics
        Rigidbody rb = currentBall.GetComponent<Rigidbody>();
        if (rb == null)
            rb = currentBall.AddComponent<Rigidbody>();

        rb.useGravity = true;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;

        Debug.Log("[Basketball] Ball spawned at: " + spawnPos);
    }

    private IEnumerator GameLoop()
    {
        while (gameActive && timeRemaining > 0)
        {
            HandleBallInput();
            timeRemaining -= Time.deltaTime;
            UpdateUI();

            // Check if score reached
            if (score >= targetScore)
            {
                gameActive = false;
                instructionText.text = "You won!";
                CompleteGame(true);
                yield break;
            }

            yield return null;
        }

        if (gameActive)
        {
            gameActive = false;
            instructionText.text = "Time's up!";
            bool success = score >= targetScore;
            CompleteGame(success);
        }
    }

    private void HandleBallInput()
    {
        if (!gameActive || currentBall == null) return;

        // Detect touch/mouse input
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Began)
            {
                OnBallDragStart();
            }
            else if (touch.phase == TouchPhase.Moved)
            {
                OnBallDragMove(touch.deltaPosition);
            }
            else if (touch.phase == TouchPhase.Ended)
            {
                OnBallDragEnd();
            }
        }

#if UNITY_EDITOR
        if (Input.GetMouseButtonDown(0))
        {
            OnBallDragStart();
        }
        if (Input.GetMouseButton(0))
        {
            Vector2 mouseDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 100f;
            OnBallDragMove(mouseDelta);
        }
        if (Input.GetMouseButtonUp(0))
        {
            OnBallDragEnd();
        }
#endif
    }

    private Vector3 dragDirection = Vector3.zero;
    private float dragMagnitude = 0f;

    private void OnBallDragStart()
    {
        dragDirection = Vector3.zero;
        dragMagnitude = 0f;
    }

    private void OnBallDragMove(Vector2 delta)
    {
        // Accumulate drag direction
        dragDirection += new Vector3(delta.x, delta.y, 0) * 0.01f;
        dragMagnitude = dragDirection.magnitude;
    }

    private void OnBallDragEnd()
    {
        if (dragMagnitude < 0.1f)
        {
            dragDirection = Vector3.zero;
            return; // Tap too small
        }

        ThrowBall();
        dragDirection = Vector3.zero;
        dragMagnitude = 0f;
    }

    private void ThrowBall()
    {
        if (currentBall == null) return;

        Rigidbody rb = currentBall.GetComponent<Rigidbody>();
        if (rb == null) return;

        // Calculate throw direction from drag
        Vector3 throwDir = Camera.main.transform.forward + dragDirection.normalized * 0.5f;
        throwDir = throwDir.normalized;

        // Apply force
        rb.linearVelocity = throwDir * throwForce;

        Debug.Log($"[Basketball] Ball thrown with force: {throwForce}, direction: {throwDir}");

        // Check for basket after delay
        StartCoroutine(CheckBasketAfterThrow());
    }

    private IEnumerator CheckBasketAfterThrow()
    {
        yield return new WaitForSeconds(2f); // Wait 2 seconds for ball to reach hoop

        if (currentBall == null || hoop == null) yield break;

        // Check distance from ball to hoop center
        float distanceToHoop = Vector3.Distance(currentBall.transform.position, hoop.transform.position);

        if (distanceToHoop < 0.5f) // Within 0.5m of hoop = basket!
        {
            score++;
            instructionText.text = $"Basket! Score: {score}/{targetScore}";
            Debug.Log($"[Basketball] Basket scored! Total: {score}");

            // Destroy ball and spawn new one
            Destroy(currentBall);
            SpawnBall();
        }
        else
        {
            instructionText.text = "Missed! Try again.";
            Debug.Log("[Basketball] Missed");

            // Respawn ball
            Destroy(currentBall);
            SpawnBall();
        }
    }

    private void UpdateUI()
    {
        if (scoreText != null)
            scoreText.text = $"Score: {score}/{targetScore}";

        if (timerText != null)
            timerText.text = $"Time: {Mathf.Max(0, timeRemaining):F1}s";
    }

    private void CompleteGame(bool success)
    {
        gameActive = false;

        if (gameCoroutine != null)
            StopCoroutine(gameCoroutine);

        Invoke(nameof(HideGame), 2f);
        onComplete?.Invoke(success, success ? 100 : 0);
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
        gameActive = false;
        if (gameCoroutine != null)
            StopCoroutine(gameCoroutine);
        gamePanel.SetActive(false);

        if (currentBall != null) Destroy(currentBall);
        if (hoop != null) Destroy(hoop);
    }
}