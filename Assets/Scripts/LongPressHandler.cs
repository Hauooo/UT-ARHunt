using UnityEngine;
using UnityEngine.EventSystems;
using System;

public class LongPressHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public event Action OnLongPress;

    private float holdTime = 0f;
    private float requiredHoldTime = 0.5f;
    private bool isHolding = false;

    public void OnPointerDown(PointerEventData eventData)
    {
        holdTime = 0f;
        isHolding = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isHolding = false;
    }

    private void Update()
    {
        if (isHolding)
        {
            holdTime += Time.deltaTime;
            if (holdTime >= requiredHoldTime)
            {
                OnLongPress?.Invoke();
                isHolding = false;
            }
        }
    }
}