using UnityEngine;
using TMPro;

/// <summary>
/// Detects taps on the treasure in AR and triggers collection
/// </summary>
public class TreasureARTapHandler : MonoBehaviour
{
    [SerializeField] private Collider treasureCollider;
    private TreasureManagerGPS_Multiplayer treasureManager;
    private TextMeshProUGUI tapPromptText;

    private void Start()
    {
        if (treasureCollider == null)
            treasureCollider = GetComponent<Collider>();

        // ← FIRST: Try parent
        treasureManager = GetComponentInParent<TreasureManagerGPS_Multiplayer>();

        // ← SECOND: If not found, search in scene (fallback)
        if (treasureManager == null)
        {
            treasureManager = FindObjectOfType<TreasureManagerGPS_Multiplayer>();
            Debug.Log("[TreasureARTap] Found TreasureManager via scene search: " + (treasureManager != null));
        }

        // Find the tap prompt text in children
        tapPromptText = GetComponentInChildren<TextMeshProUGUI>();

        if (treasureCollider == null)
            Debug.LogError("[TreasureARTap] Collider not found on treasure");

        if (treasureManager == null)
            Debug.LogError("[TreasureARTap] TreasureManager not found in parent or scene!");
        else
            Debug.Log("[TreasureARTap] TreasureManager found: " + treasureManager.gameObject.name);
    }

    private void Update()
    {
        // Detect touch input
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
                ProcessTap(touch.position);
        }

        // Editor testing with mouse
#if UNITY_EDITOR
        if (Input.GetMouseButtonDown(0))
            ProcessTap(Input.mousePosition);
#endif
    }

    private void ProcessTap(Vector2 screenPos)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(screenPos);

        // Raycast to detect if we hit this treasure
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            // Check if we hit this treasure or any of its children
            if (hit.collider.gameObject == gameObject ||
                hit.collider.transform.IsChildOf(transform))
            {
                Debug.Log("[TreasureARTap] ✓ Treasure tapped!");
                OnTreasureTapped();
            }
        }
    }

    private void OnTreasureTapped()
    {
        // Hide tap prompt
        if (tapPromptText != null)
            tapPromptText.gameObject.SetActive(false);

        if (treasureManager != null)
        {
            Debug.Log("[TreasureARTap] Calling CollectTargetTreasure()");
            treasureManager.CollectTargetTreasure();
        }
        else
        {
            Debug.LogError("[TreasureARTap] TreasureManager is NULL - cannot collect!");
        }
    }
}