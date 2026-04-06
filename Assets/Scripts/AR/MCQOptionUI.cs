using UnityEngine;
using TMPro;

/// <summary>
/// Attached to each 3D MCQ option object in AR space.
/// Handles visual feedback on hover/select and fires the selection event.
/// </summary>
public class MCQOptionUI : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private TMP_Text optionText;

    [Header("Materials")]
    [SerializeField] private Material normalMaterial;
    [SerializeField] private Material highlightMaterial;
    [SerializeField] private Material selectedMaterial;

    private MeshRenderer meshRenderer;
    private string optionLabel;
    private bool isCorrect;
    private bool interactionEnabled = true;

    /// <summary>Fired when the player taps/clicks this option.</summary>
    public System.Action<MCQOptionUI> OnSelected;

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
    }

    /// <summary>
    /// Initialise this option with its display text and correctness flag.
    /// </summary>
    public void Setup(string text, bool correct)
    {
        optionLabel = text;
        isCorrect = correct;

        if (optionText != null)
            optionText.text = text;

        if (meshRenderer != null && normalMaterial != null)
            meshRenderer.material = normalMaterial;
    }

    /// <summary>Whether this option is the correct answer.</summary>
    public bool IsCorrect => isCorrect;

    /// <summary>The display text of this option.</summary>
    public string OptionText => optionLabel;

    /// <summary>Enable or disable tap interaction on this option.</summary>
    public void SetInteractionEnabled(bool enabled)
    {
        interactionEnabled = enabled;
    }

    /// <summary>
    /// Called by <see cref="ArMCQManager"/> when a raycast hits this object.
    /// </summary>
    public void OnTapped()
    {
        if (!interactionEnabled) return;

        interactionEnabled = false;

        if (meshRenderer != null && selectedMaterial != null)
            meshRenderer.material = selectedMaterial;

        OnSelected?.Invoke(this);
    }

    /// <summary>
    /// Highlight this option (e.g. on pointer-over via gaze or hover).
    /// </summary>
    public void SetHighlight(bool highlighted)
    {
        if (!interactionEnabled || meshRenderer == null) return;

        meshRenderer.material = highlighted
            ? (highlightMaterial != null ? highlightMaterial : normalMaterial)
            : (normalMaterial != null ? normalMaterial : meshRenderer.material);
    }

    /// <summary>
    /// Show a result indicator ("✅" / "❌") as a prefix on the option text.
    /// </summary>
    public void ShowResult(bool correct)
    {
        if (optionText != null)
            optionText.text = (correct ? "✅ " : "❌ ") + optionLabel;
    }
}
