using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Detects AR planes and spawns MCQ option prefabs on the first suitable plane,
/// arranging them in a grid layout above the surface.
/// </summary>
public class ARMCQOptionSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARPlaneManager planeManager;

    [Header("Prefab")]
    [SerializeField] private GameObject mcqOptionPrefab;

    [Header("Grid Layout")]
    [SerializeField] private float spacingX = 0.35f;
    [SerializeField] private float spacingZ = 0.25f;
    [SerializeField] private float heightAbovePlane = 0.05f;
    [SerializeField] private int columnsPerRow = 2;

    [Header("Detection Timeout")]
    [SerializeField] private float detectionTimeoutSeconds = 30f;

    private readonly List<MCQOption> pendingOptions = new List<MCQOption>();
    private readonly List<MCQOptionUI> spawnedOptions = new List<MCQOptionUI>();
    private bool hasSpawned;
    private Coroutine timeoutCoroutine;

    /// <summary>Fired once the options have been successfully spawned.</summary>
    public System.Action OnOptionsSpawned;

    /// <summary>Fired if no plane is detected within the timeout window.</summary>
    public System.Action OnDetectionTimeout;

    private void OnEnable()
    {
        if (planeManager != null)
            planeManager.trackablesChanged.AddListener(OnPlanesChanged);
    }

    private void OnDisable()
    {
        if (planeManager != null)
            planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);

        StopAllCoroutines();
    }

    /// <summary>
    /// Begin watching for planes and attempt to spawn <paramref name="options"/>
    /// once a plane is available.  Starts the detection timeout countdown.
    /// </summary>
    public void BeginSpawning(List<MCQOption> options)
    {
        if (options == null || options.Count == 0) return;

        hasSpawned = false;
        ClearOptions();

        pendingOptions.Clear();
        pendingOptions.AddRange(options);

        if (planeManager != null)
            planeManager.enabled = true;

        timeoutCoroutine = StartCoroutine(DetectionTimeoutCoroutine());

        // If a plane is already tracked, spawn immediately
        if (planeManager != null)
        {
            foreach (var plane in planeManager.trackables)
            {
                if (TrySpawnOnPlane(plane))
                    return;
            }
        }
    }

    /// <summary>
    /// Remove all previously spawned option objects from the scene.
    /// </summary>
    public void ClearOptions()
    {
        foreach (var opt in spawnedOptions)
        {
            if (opt != null)
                Destroy(opt.gameObject);
        }
        spawnedOptions.Clear();
    }

    /// <summary>
    /// All currently spawned <see cref="MCQOptionUI"/> instances.
    /// </summary>
    public IReadOnlyList<MCQOptionUI> SpawnedOptions => spawnedOptions;

    // ─── Private helpers ─────────────────────────────────────────────────────

    private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        if (hasSpawned) return;

        foreach (var plane in args.added)
        {
            if (TrySpawnOnPlane(plane))
                return;
        }
    }

    private bool TrySpawnOnPlane(ARPlane plane)
    {
        if (hasSpawned || mcqOptionPrefab == null) return false;
        if (plane == null) return false;
        if (pendingOptions == null || pendingOptions.Count == 0) return false;

        // Only horizontal planes are suitable for placing options
        if (plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp &&
            plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalDown)
            return false;

        hasSpawned = true;

        if (timeoutCoroutine != null)
        {
            StopCoroutine(timeoutCoroutine);
            timeoutCoroutine = null;
        }

        SpawnOptionsOnPlane(plane, pendingOptions);
        OnOptionsSpawned?.Invoke();
        return true;
    }

    private void SpawnOptionsOnPlane(ARPlane plane, List<MCQOption> options)
    {
        int count = options.Count;
        int cols = Mathf.Min(columnsPerRow, count);
        int rows = Mathf.CeilToInt((float)count / cols);

        float totalWidth = (cols - 1) * spacingX;
        float totalDepth = (rows - 1) * spacingZ;

        Vector3 planeCenter = plane.center;
        Vector3 basePosition = new Vector3(
            planeCenter.x - totalWidth / 2f,
            planeCenter.y + heightAbovePlane,
            planeCenter.z - totalDepth / 2f
        );

        for (int i = 0; i < count; i++)
        {
            int col = i % cols;
            int row = i / cols;

            Vector3 pos = basePosition + new Vector3(col * spacingX, 0f, row * spacingZ);

            GameObject obj = Instantiate(mcqOptionPrefab, pos, Quaternion.identity);
            MCQOptionUI ui = obj.GetComponent<MCQOptionUI>();

            if (ui != null)
            {
                ui.Setup(options[i].text, options[i].isCorrect);
                spawnedOptions.Add(ui);
            }
        }
    }

    private IEnumerator DetectionTimeoutCoroutine()
    {
        float elapsed = 0f;
        while (elapsed < detectionTimeoutSeconds)
        {
            if (hasSpawned) yield break;

            // Retry against any currently tracked plane each frame
            if (planeManager != null)
            {
                foreach (var plane in planeManager.trackables)
                {
                    if (TrySpawnOnPlane(plane))
                        yield break;
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!hasSpawned)
            OnDetectionTimeout?.Invoke();
    }
}
