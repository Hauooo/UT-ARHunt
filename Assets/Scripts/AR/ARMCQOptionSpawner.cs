using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Detects AR planes and spawns MCQ option prefabs on a single plane,
/// arranging them randomly across the surface.
/// </summary>
public class ARMCQOptionSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARPlaneManager planeManager;

    [Header("Prefab")]
    [SerializeField] private GameObject mcqOptionPrefab;

    [Header("Spawn Settings")]
    [SerializeField] private float heightAbovePlane = 0.05f;
    [SerializeField] private float minSpacing = 0.3f; // Minimum distance between options
    [SerializeField] private float edgePadding = 0.2f; // Distance from plane edge

    [Header("Detection Timeout")]
    [SerializeField] private float detectionTimeoutSeconds = 30f;

    private readonly List<MCQOption> pendingOptions = new List<MCQOption>();
    private readonly List<MCQOptionUI> spawnedOptions = new List<MCQOptionUI>();
    private bool hasSpawned;
    private Coroutine timeoutCoroutine;

    /// <summary>Fired once all options have been successfully spawned.</summary>
    public System.Action OnAllOptionsSpawned;

    /// <summary>Fired if no plane is detected within the timeout window.</summary>
    public System.Action OnDetectionTimeout;

    private void OnEnable()
    {
        if (planeManager != null)
        {
            planeManager.trackablesChanged.AddListener(OnPlanesChanged);
            Debug.Log("[ARMCQOptionSpawner] Listening to plane trackables");
        }
    }

    private void OnDisable()
    {
        if (planeManager != null)
            planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);

        StopAllCoroutines();
    }

    /// <summary>
    /// Begin watching for planes and attempt to spawn <paramref name="options"/>
    /// randomly on a single plane.
    /// </summary>
    public void BeginSpawning(List<MCQOption> options)
    {
        if (options == null || options.Count == 0)
        {
            Debug.LogError("[ARMCQOptionSpawner] No options provided!");
            return;
        }

        Debug.Log($"[ARMCQOptionSpawner] BeginSpawning with {options.Count} options randomly");

        hasSpawned = false;
        ClearOptions();

        pendingOptions.Clear();
        pendingOptions.AddRange(options);

        if (planeManager == null)
        {
            Debug.LogError("[ARMCQOptionSpawner] ARPlaneManager not assigned!");
            return;
        }

        if (!planeManager.enabled)
            planeManager.enabled = true;

        timeoutCoroutine = StartCoroutine(DetectionTimeoutCoroutine());

        // If a plane is already tracked, spawn immediately
        foreach (var plane in planeManager.trackables)
        {
            if (TrySpawnOnPlane(plane))
            {
                Debug.Log("[ARMCQOptionSpawner] ✓ Spawned on existing plane");
                return;
            }
        }

        Debug.Log("[ARMCQOptionSpawner] No suitable plane found yet, waiting...");
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
        Debug.Log("[ARMCQOptionSpawner] Cleared all options");
    }

    /// <summary>
    /// All currently spawned <see cref="MCQOptionUI"/> instances.
    /// </summary>
    public IReadOnlyList<MCQOptionUI> SpawnedOptions => spawnedOptions;

    // ─── Private helpers ─────────────────────────────────────────────────────

    private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        if (hasSpawned) return;

        // Try newly added planes
        foreach (var plane in args.added)
        {
            if (TrySpawnOnPlane(plane))
                return;
        }

        // Also try updated planes (they might have grown and become suitable)
        foreach (var plane in args.updated)
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

        // ← Check minimum plane size
        Vector3 extents = plane.extents;
        float minRequiredSize = 0.2f; // At least 20cm
        
        // Accept if at least one dimension is large enough
        bool hasMinSize = (extents.x >= minRequiredSize) || (extents.z >= minRequiredSize);
        
        if (!hasMinSize)
        {
            Debug.Log($"[ARMCQOptionSpawner] Plane too small: {extents.x:F3}x{extents.z:F3}m, waiting...");
            return false;
        }

        Debug.Log($"[ARMCQOptionSpawner] ✓ Suitable plane found: {extents.x:F3}x{extents.z:F3}m");

        hasSpawned = true;

        if (timeoutCoroutine != null)
        {
            StopCoroutine(timeoutCoroutine);
            timeoutCoroutine = null;
        }

        SpawnOptionsOnPlane(plane, pendingOptions);
        OnAllOptionsSpawned?.Invoke();
        Debug.Log($"[ARMCQOptionSpawner] ✓ Successfully spawned {pendingOptions.Count} options randomly on one plane");
        return true;
    }

    private void SpawnOptionsOnPlane(ARPlane plane, List<MCQOption> options)
    {
        Vector3 planeCenter = plane.center;
        Vector3 extents = plane.extents;

        // Calculate plane bounds with padding
        float minX = planeCenter.x - (extents.x / 2f) + edgePadding;
        float maxX = planeCenter.x + (extents.x / 2f) - edgePadding;
        float minZ = planeCenter.z - (extents.z / 2f) + edgePadding;
        float maxZ = planeCenter.z + (extents.z / 2f) - edgePadding;

        Debug.Log($"[ARMCQOptionSpawner] Plane bounds: X[{minX:F2}, {maxX:F2}], Z[{minZ:F2}, {maxZ:F2}]");

        List<Vector3> spawnPositions = new List<Vector3>();

        // Generate random positions for each option
        int maxAttempts = 100;
        int attempts = 0;

        for (int i = 0; i < options.Count; i++)
        {
            Vector3 randomPos = Vector3.zero;
            bool validPosition = false;
            attempts = 0;

            // Keep trying until we find a valid position (not too close to other options)
            while (!validPosition && attempts < maxAttempts)
            {
                randomPos = new Vector3(
                    Random.Range(minX, maxX),
                    planeCenter.y + heightAbovePlane,
                    Random.Range(minZ, maxZ)
                );

                // Check distance from all previously spawned options
                validPosition = true;
                foreach (var existingPos in spawnPositions)
                {
                    float distance = Vector3.Distance(randomPos, existingPos);
                    if (distance < minSpacing)
                    {
                        validPosition = false;
                        break;
                    }
                }

                attempts++;
            }

            if (!validPosition)
            {
                Debug.LogWarning($"[ARMCQOptionSpawner] Could not find valid position for option {i + 1} after {maxAttempts} attempts");
                continue;
            }

            spawnPositions.Add(randomPos);

            GameObject obj = Instantiate(mcqOptionPrefab, randomPos, Quaternion.identity);
            MCQOptionUI ui = obj.GetComponent<MCQOptionUI>();

            if (ui != null)
            {
                ui.Setup(options[i].text, options[i].isCorrect);
                spawnedOptions.Add(ui);
                Debug.Log($"[ARMCQOptionSpawner] Spawned option {i + 1}/{options.Count}: '{options[i].text}' at {randomPos}");
            }
            else
            {
                Debug.LogError($"[ARMCQOptionSpawner] MCQOptionUI not found on prefab instance!");
                Destroy(obj);
            }
        }
    }

    private IEnumerator DetectionTimeoutCoroutine()
    {
        Debug.Log($"[ARMCQOptionSpawner] Starting detection timeout ({detectionTimeoutSeconds}s)");
        float elapsed = 0f;

        while (elapsed < detectionTimeoutSeconds)
        {
            if (hasSpawned) yield break;

            // Retry against any currently tracked plane each frame
            if (planeManager != null && planeManager.trackables.count > 0)
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
        {
            Debug.LogError("[ARMCQOptionSpawner] ❌ Detection timeout! No suitable plane found.");
            OnDetectionTimeout?.Invoke();
        }
    }
}