using UnityEngine;
using UnityEngine.Events;

public class ARMCQOptionBehaviour : MonoBehaviour
{
    [SerializeField] private string optionText;
    [SerializeField] private bool isCorrect;
    private UnityEvent<bool> onOptionSelected;

    public void Setup(string text, bool correct, UnityEvent<bool> onSelected)
    {
        optionText = text;
        isCorrect = correct;
        onOptionSelected = onSelected;
    }

    public void OnTapped()
    {
        Debug.Log($"[AROption] Selected: '{optionText}' (correct={isCorrect})");
        onOptionSelected?.Invoke(isCorrect);
    }
}