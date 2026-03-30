using UnityEngine;
using UnityEngine.UI;

public class MapClickHandler : MonoBehaviour
{
    public System.Action<Vector2> onMapClicked;

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            RectTransform rectTransform = GetComponent<RectTransform>();

            // Check if click is within the rect
            if (RectTransformUtility.RectangleContainsScreenPoint(
                rectTransform,
                Input.mousePosition,
                null))  // null = use main camera
            {
                onMapClicked?.Invoke(Input.mousePosition);
                Debug.Log("[MapClickHandler] Map clicked at: " + Input.mousePosition);
            }
        }
    }
}