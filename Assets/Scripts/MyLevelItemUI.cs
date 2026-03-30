using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Individual level item in the My Levels list
/// </summary>
public class MyLevelItemUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI levelNameText;
    [SerializeField] private TextMeshProUGUI treasureCountText;
    [SerializeField] private TextMeshProUGUI difficultyText;
    [SerializeField] private Button selectButton;
    [SerializeField] private Button editButton;
    [SerializeField] private Button deleteButton;

    private System.Action onSelectClicked;
    private System.Action onEditClicked;
    private System.Action onDeleteClicked;

    public void Setup(string levelName, int treasureCount, string difficulty,
                      System.Action selectCallback,
                      System.Action editCallback,
                      System.Action deleteCallback)
    {
        onSelectClicked = selectCallback;
        onEditClicked = editCallback;
        onDeleteClicked = deleteCallback;

        levelNameText.text = levelName;
        treasureCountText.text = $"{treasureCount} treasures";
        difficultyText.text = difficulty;

        selectButton.onClick.AddListener(OnSelectClicked);
        editButton.onClick.AddListener(OnEditClicked);
        deleteButton.onClick.AddListener(OnDeleteClicked);

        Debug.Log($"[MyLevelItemUI] Setup for level: {levelName}");
    }

    private void OnSelectClicked()
    {
        onSelectClicked?.Invoke();
    }

    private void OnEditClicked()
    {
        onEditClicked?.Invoke();
    }

    private void OnDeleteClicked()
    {
        onDeleteClicked?.Invoke();
    }
}