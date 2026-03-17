using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class CardBehaviour : MonoBehaviour
{
    [SerializeField] private Image cardImage;
    [SerializeField] private TextMeshProUGUI cardText;
    [SerializeField] private Button cardButton;
    [SerializeField] private Image cardBack;  // The back of the card

    private int cardId;  // Which pair this belongs to
    private bool isFlipped = false;
    private bool isMatched = false;

    public event System.Action<CardBehaviour> OnCardFlipped;

    private void Start()
    {
        if (cardButton != null)
            cardButton.onClick.AddListener(FlipCard);
    }

    public void SetupCard(int id, Sprite cardSprite, string displayText)
    {
        cardId = id;

        if (cardImage != null)
            cardImage.sprite = cardSprite;

        if (cardText != null)
            cardText.text = displayText;

        // Start with back showing
        cardBack.gameObject.SetActive(true);
        cardImage.gameObject.SetActive(false);
    }

    public void FlipCard()
    {
        if (isFlipped || isMatched) return;

        isFlipped = true;
        cardBack.gameObject.SetActive(false);
        cardImage.gameObject.SetActive(true);

        OnCardFlipped?.Invoke(this);
    }

    public void UnflipCard()
    {
        StartCoroutine(UnflipCoroutine());
    }

    private IEnumerator UnflipCoroutine()
    {
        yield return new WaitForSeconds(0.3f);

        isFlipped = false;
        cardBack.gameObject.SetActive(true);
        cardImage.gameObject.SetActive(false);
    }

    public void MatchCard()
    {
        isMatched = true;
        cardButton.interactable = false;
    }

    public int GetCardId()
    {
        return cardId;
    }

    public bool IsMatched()
    {
        return isMatched;
    }

    public bool IsFlipped()
    {
        return isFlipped;
    }
}