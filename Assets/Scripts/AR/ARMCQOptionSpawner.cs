using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;

public class ARMCQOptionSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARPlaneManager planeManager;

    [Header("Prefab")]
    [SerializeField] private GameObject mcqOptionPrefab;

    [Header("Spawn Settings")]
    [SerializeField] private float heightAbovePlane = 1.0f;
    [SerializeField] private float gridSpacing = 0.6f;  // ← Distance between options in grid
    [SerializeField] private float edgePadding = 0.2f;

    [Header("Detection Timeout")]
    [SerializeField] private float detectionTimeoutSeconds = 30f;

    private readonly List<MCQOption> pendingOptions = new List<MCQOption>();
    private readonly List<ARMCQOptionBehaviour> spawnedOptions = new List<ARMCQOptionBehaviour>();
    private bool hasSpawned;
    private Coroutine timeoutCoroutine;
    private System.Action<bool> optionSelectionCallback;

    public System.Action OnAllOptionsSpawned;
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

    public void BeginSpawning(List<MCQOption> options, System.Action<bool> onOptionSelected = null)
    {
        if (options == null || options.Count == 0)
        {
            Debug.LogError("[ARMCQOptionSpawner] No options provided!");
            return;
        }

        Debug.Log($"[ARMCQOptionSpawner] BeginSpawning with {options.Count} options in 2x2 grid");

        hasSpawned = false;
        ClearOptions();

        pendingOptions.Clear();
        pendingOptions.AddRange(options);

        optionSelectionCallback = onOptionSelected;

        if (planeManager == null)
        {
            Debug.LogError("[ARMCQOptionSpawner] ARPlaneManager not assigned!");
            return;
        }

        if (!planeManager.enabled)
            planeManager.enabled = true;


        timeoutCoroutine = StartCoroutine(DetectionTimeoutCoroutine());

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

    public IReadOnlyList<ARMCQOptionBehaviour> SpawnedOptions => spawnedOptions;

    private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        if (hasSpawned) return;

        foreach (var plane in args.added)
        {
            if (TrySpawnOnPlane(plane))
                return;
        }

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

        if (plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp &&
            plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalDown)
            return false;

        Vector3 extents = plane.extents;
        float minRequiredSize = 0.2f;
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
        Debug.Log($"[ARMCQOptionSpawner] ✓ Successfully spawned {pendingOptions.Count} options in 2x2 grid");
        return true;
    }

    private void SpawnOptionsOnPlane(ARPlane plane, List<MCQOption> options)
    {
        Vector3 planeCenter = plane.center;

        Camera mainCamera = Camera.main;
        float cameraHeight = mainCamera != null ? mainCamera.transform.position.y : 0f;
        float spawnHeight = cameraHeight + heightAbovePlane;

        Debug.Log($"[ARMCQOptionSpawner] Camera height: {cameraHeight}, spawn height: {spawnHeight}");

        // ← 2x2 GRID LAYOUT
        Vector3 cameraForward = mainCamera != null ? mainCamera.transform.forward : Vector3.forward;
        Vector3 cameraRight = mainCamera != null ? mainCamera.transform.right : Vector3.right;

        // Position grid 2m in front of camera
        float distanceFromCamera = 2.0f;
        Vector3 gridCenter = mainCamera.transform.position + cameraForward * distanceFromCamera;
        gridCenter.y = spawnHeight;

        // Grid positions (2x2)
        Vector3[] gridPositions = new Vector3[4];
        gridPositions[0] = gridCenter + cameraRight * (-gridSpacing / 2f) + Vector3.up * (gridSpacing / 2f);  // Top-left
        gridPositions[1] = gridCenter + cameraRight * (gridSpacing / 2f) + Vector3.up * (gridSpacing / 2f);   // Top-right
        gridPositions[2] = gridCenter + cameraRight * (-gridSpacing / 2f) + Vector3.up * (-gridSpacing / 2f); // Bottom-left
        gridPositions[3] = gridCenter + cameraRight * (gridSpacing / 2f) + Vector3.up * (-gridSpacing / 2f);  // Bottom-right

        for (int i = 0; i < options.Count && i < 4; i++)
        {
            Vector3 worldPos = gridPositions[i];

            Debug.Log($"[ARMCQOptionSpawner] Option {i + 1}: worldPos={worldPos}");

            GameObject obj = Instantiate(mcqOptionPrefab, worldPos, Quaternion.identity);
            ARMCQOptionBehaviour option = obj.GetComponent<ARMCQOptionBehaviour>();

            if (option != null)
            {
                UnityEvent<bool> unityCallback = new UnityEvent<bool>();
                if (optionSelectionCallback != null)
                {
                    unityCallback.AddListener(new UnityAction<bool>(optionSelectionCallback));
                }

                option.Setup(options[i].text, options[i].isCorrect, unityCallback);
                spawnedOptions.Add(option);
                Debug.Log($"[ARMCQOptionSpawner] Spawned option {i + 1}/{options.Count}: '{options[i].text}' at {worldPos}");
            }
            else
            {
                Debug.LogError($"[ARMCQOptionSpawner] ARMCQOptionBehaviour not found on prefab instance!");
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