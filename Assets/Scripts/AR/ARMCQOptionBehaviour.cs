using UnityEngine;
using UnityEngine.Events;
using TMPro;

public class ARMCQOptionBehaviour : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshPro labelText;
    [SerializeField] private Renderer cubeRenderer;
    [SerializeField] private Collider cubeCollider;

    [Header("Sizing")]
    [SerializeField] private float minWidth = 0.3f;
    [SerializeField] private float maxWidth = 2.0f;
    [SerializeField] private float minHeight = 0.2f;
    [SerializeField] private float maxHeight = 2.0f;
    [SerializeField] private float characterWidthFactor = 0.05f; // Width per character
    [SerializeField] private float lineHeightFactor = 0.15f;     // Height per line
    [SerializeField] private int charsPerLine = 15;              // Wrap text at this many chars

    [Header("Colors")]
    [SerializeField] private Color correctColor = Color.green;
    [SerializeField] private Color incorrectColor = Color.red;
    [SerializeField] private Color defaultColor = new Color(0.2f, 0.7f, 1f);

    [Header("Camera Facing")]
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private float rotationSpeed = 5f;

    private string optionText;
    private bool isCorrect;
    private bool isHit = false;
    private bool interactionEnabled = true;
    private UnityEvent<bool> onOptionSelected;
    private Camera mainCamera;

    public bool IsCorrect => isCorrect;

    private void Start()
    {
        if (cubeRenderer == null)
            cubeRenderer = GetComponent<Renderer>();

        if (cubeCollider == null)
            cubeCollider = GetComponent<Collider>();

        mainCamera = Camera.main;

        if (mainCamera == null)
            Debug.LogWarning("[AROption] Main camera not found!");

        if (labelText == null)
        {
            labelText = GetComponentInChildren<TextMeshPro>();
            if (labelText == null)
                Debug.LogWarning("[AROption] TextMeshPro (3D) not found in children!");
        }

        if (cubeRenderer != null)
        {
            cubeRenderer.enabled = true;
            cubeRenderer.material.color = defaultColor;
        }

        if (cubeCollider != null)
        {
            cubeCollider.enabled = true;
        }

        Debug.Log($"[AROption] Start complete. Position: {transform.position}, Scale: {transform.localScale}");
    }

    private void Update()
    {
        if (faceCamera && mainCamera != null && !isHit)
        {
            FaceCameraSmooth();
        }
    }

    private void FaceCameraSmooth()
    {
        Vector3 directionToCamera = (mainCamera.transform.position - transform.position).normalized;
        Quaternion targetRotation = Quaternion.LookRotation(directionToCamera);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
    }

    public void Setup(string text, bool correct, UnityEvent<bool> onSelected)
    {
        optionText = text;
        isCorrect = correct;
        onOptionSelected = onSelected;

        // ← Set and wrap text
        if (labelText != null)
        {
            labelText.text = WrapText(text, charsPerLine);
            Debug.Log($"[AROption] Text set: '{text}'");
        }

        // ← Resize cube based on text length AND line count
        ResizeCubeForText(text);

        if (cubeRenderer != null)
        {
            cubeRenderer.material.color = defaultColor;
            cubeRenderer.enabled = true;
        }

        if (cubeCollider != null)
        {
            cubeCollider.enabled = true;
        }

        gameObject.SetActive(true);

        Debug.Log($"[AROption] Setup complete: '{text}' (correct={isCorrect}), scale={transform.localScale}");
    }

    /// <summary>
    /// Wrap text to fit within a certain character limit per line
    /// </summary>
    private string WrapText(string text, int charsPerLine)
    {
        if (text.Length <= charsPerLine)
            return text;

        string[] words = text.Split(' ');
        string wrappedText = "";
        string currentLine = "";

        foreach (string word in words)
        {
            if ((currentLine + word).Length > charsPerLine)
            {
                if (!string.IsNullOrEmpty(currentLine))
                {
                    wrappedText += currentLine + "\n";
                    currentLine = word + " ";
                }
                else
                {
                    wrappedText += word + "\n";
                }
            }
            else
            {
                currentLine += word + " ";
            }
        }

        if (!string.IsNullOrEmpty(currentLine))
            wrappedText += currentLine;

        return wrappedText.Trim();
    }

    /// <summary>
    /// Calculate cube size based on text length and line count
    /// </summary>
    private void ResizeCubeForText(string text)
    {
        int lineCount = Mathf.Max(1, Mathf.CeilToInt((float)text.Length / charsPerLine));

        int longestLineLength = 0;
        string[] lines = text.Split('\n');
        foreach (string line in lines)
        {
            longestLineLength = Mathf.Max(longestLineLength, line.Length);
        }

        float calculatedWidth = Mathf.Clamp(
            longestLineLength * characterWidthFactor,
            minWidth,
            maxWidth
        );

        float calculatedHeight = Mathf.Clamp(
            lineCount * lineHeightFactor,
            minHeight,
            maxHeight
        );

        float depth = calculatedWidth * 0.5f;

        Vector3 newScale = new Vector3(calculatedWidth, calculatedHeight, depth);
        transform.localScale = newScale;

        Debug.Log($"[AROption] Resized cube for '{text}' ({text.Length} chars, {lineCount} lines): scale {newScale}");
    }

    public void OnTapped()
    {
        if (!interactionEnabled || isHit)
        {
            Debug.LogWarning($"[AROption] Tap ignored - interactionEnabled: {interactionEnabled}, isHit: {isHit}");
            return;
        }

        Debug.Log($"[AROption] Selected: '{optionText}' (correct={isCorrect})");
        onOptionSelected?.Invoke(isCorrect);

        ShowResult(isCorrect);
        isHit = true;
        faceCamera = false;
    }

    public void SetInteractionEnabled(bool enabled)
    {
        interactionEnabled = enabled;
        Debug.Log($"[AROption] Interaction enabled: {enabled}");
    }

    public void ShowResult(bool correct)
    {
        if (cubeRenderer == null) return;

        isHit = true;
        cubeRenderer.material.color = correct ? correctColor : incorrectColor;

        Debug.Log($"[AROption] Showing result: {(correct ? "Correct (Green)" : "Incorrect (Red)")}");
    }
}