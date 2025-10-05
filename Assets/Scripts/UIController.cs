using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI treasureText;
    [SerializeField] private Button collectButton;

    private GameObject currentTreasure;

    void Start()
    {
        treasureText.text = "No treasure yet!";
        collectButton.gameObject.SetActive(false);
        collectButton.onClick.AddListener(CollectTreasure);
    }

    public void ShowTreasure(GameObject treasure)
    {
        currentTreasure = treasure;
        treasureText.text = "Treasure found!";
        collectButton.gameObject.SetActive(true);
    }

    private void CollectTreasure()
    {
        if (currentTreasure != null)
        {
            Destroy(currentTreasure);
            treasureText.text = "Treasure collected!";
            collectButton.gameObject.SetActive(false);
            currentTreasure = null;
        }
    }
}
