using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class MapPanZoomController : MonoBehaviour, IDragHandler, IEndDragHandler, IBeginDragHandler
{
    [SerializeField] private RectTransform mapContainer;
    private Canvas parentCanvas;
    private RectTransform parentRect; // The viewport/screen area

    [Header("Pan Settings")]
    [SerializeField] private float dragSensitivity = 1.0f;
    [SerializeField] private bool enableInertia = true;
    [SerializeField] private float inertiaDamping = 0.9f;

    [Header("Zoom Settings")]
    [SerializeField] private bool enableZoom = true;
    [SerializeField] private float pinchSensitivity = 0.005f;
    [SerializeField] private float minZoom = 0.5f;
    [SerializeField] private float maxZoom = 3.0f;

    [Header("Bounds")]
    [SerializeField] private bool restrictToBounds = true;
    // We no longer need mapSizeLimit because we calculate it dynamically!

    private Vector2 velocity = Vector2.zero;
    private bool isDragging = false;
    private float lastPinchDistance = 0f;

    private void Start()
    {
        if (mapContainer == null) mapContainer = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();

        // Get the parent RectTransform (usually the screen/mask) to calculate bounds
        if (mapContainer.parent != null)
        {
            parentRect = mapContainer.parent.GetComponent<RectTransform>();
        }
    }

    private void Update()
    {
        // Handle Inertia (Swiping hard)
        if (enableInertia && !isDragging && velocity.magnitude > 0.01f)
        {
            mapContainer.anchoredPosition += velocity * Time.deltaTime * 60f;
            velocity *= inertiaDamping;

            if (restrictToBounds) ClampMapToBounds();
        }

        if (!enableZoom) return;

        // Mobile Pinch Zoom
        if (Input.touchCount == 2)
        {
            HandlePinchZoom();
        }
        // PC / Unity Editor Mouse Scroll Zoom (For easier testing)
        else if (Input.mouseScrollDelta.y != 0)
        {
            HandleMouseZoom(Input.mouseScrollDelta.y);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        isDragging = true;
        velocity = Vector2.zero;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (Input.touchCount > 1) return; // Don't pan while pinching

        Vector2 normalizedDelta = eventData.delta / parentCanvas.scaleFactor;
        mapContainer.anchoredPosition += normalizedDelta * dragSensitivity;

        // Track velocity for inertia swipe
        velocity = normalizedDelta * dragSensitivity;

        if (restrictToBounds) ClampMapToBounds();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;
    }

    private void HandlePinchZoom()
    {
        Touch touch0 = Input.GetTouch(0);
        Touch touch1 = Input.GetTouch(1);

        if (touch0.phase == TouchPhase.Ended || touch1.phase == TouchPhase.Ended)
        {
            lastPinchDistance = 0f;
            return;
        }

        float currentDistance = Vector2.Distance(touch0.position, touch1.position);
        Vector2 pinchCenter = (touch0.position + touch1.position) * 0.5f;

        if (lastPinchDistance == 0f)
        {
            lastPinchDistance = currentDistance;
            return;
        }

        float distanceDelta = currentDistance - lastPinchDistance;
        float zoomDelta = distanceDelta * pinchSensitivity;

        ApplyZoom(zoomDelta, pinchCenter);

        lastPinchDistance = currentDistance;
    }

    private void HandleMouseZoom(float scrollDelta)
    {
        float zoomDelta = scrollDelta * 0.1f; // Mouse sensitivity
        ApplyZoom(zoomDelta, Input.mousePosition);
    }

    private void ApplyZoom(float zoomDelta, Vector2 zoomCenterScreen)
    {
        Vector3 oldScale = mapContainer.localScale;
        float targetZoom = Mathf.Clamp(oldScale.x + zoomDelta, minZoom, maxZoom);

        mapContainer.localScale = new Vector3(targetZoom, targetZoom, 1);

        if (parentCanvas.worldCamera != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                mapContainer,
                zoomCenterScreen,
                parentCanvas.worldCamera,
                out Vector2 localPinchBeforeScale);

            float scaleRatio = targetZoom / oldScale.x;
            Vector2 moveOffset = localPinchBeforeScale * (scaleRatio - 1f);
            mapContainer.anchoredPosition -= (Vector2)(mapContainer.localRotation * moveOffset);
        }

        if (restrictToBounds) ClampMapToBounds();
    }

    private void ClampMapToBounds()
    {
        if (parentRect == null) return;

        // Calculate the actual scaled size of the map
        float mapScaledWidth = mapContainer.rect.width * mapContainer.localScale.x;
        float mapScaledHeight = mapContainer.rect.height * mapContainer.localScale.y;

        // Calculate limits so the edge of the map stops exactly at the edge of the screen/parent
        float limitX = Mathf.Max(0, (mapScaledWidth - parentRect.rect.width) / 2f);
        float limitY = Mathf.Max(0, (mapScaledHeight - parentRect.rect.height) / 2f);

        Vector2 currentPos = mapContainer.anchoredPosition;

        // Stop the map from flying away!
        currentPos.x = Mathf.Clamp(currentPos.x, -limitX, limitX);
        currentPos.y = Mathf.Clamp(currentPos.y, -limitY, limitY);

        mapContainer.anchoredPosition = currentPos;
    }
}