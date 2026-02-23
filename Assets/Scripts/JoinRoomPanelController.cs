using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class JoinRoomPanelController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_InputField roomCodeInput;
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_Text feedbackText;

    private void Awake()
    {
        if (joinButton == null) Debug.LogError("[JoinRoomPanel] joinButton is not assigned.");
        if (roomCodeInput == null) Debug.LogError("[JoinRoomPanel] roomCodeInput is not assigned.");
        if (feedbackText == null) Debug.LogError("[JoinRoomPanel] feedbackText is not assigned.");

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(OnJoinButtonClicked);
        }
    }

    private void OnEnable()
    {
        if (roomCodeInput == null || feedbackText == null || joinButton == null)
            return;

        roomCodeInput.text = "";
        feedbackText.text = "Enter a 4-digit room code.";
        joinButton.interactable = true;
    }

    private void OnJoinButtonClicked()
    {
        if (roomCodeInput == null || feedbackText == null || joinButton == null)
            return;

        string roomCode = roomCodeInput.text.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(roomCode) || roomCode.Length != 4)
        {
            feedbackText.text = "Please enter a valid 4-digit code.";
            return;
        }

        joinButton.interactable = false;
        feedbackText.text = $"Joining room {roomCode}...";

        if (GameManager.Instance == null)
        {
            feedbackText.text = "GameManager not ready.";
            joinButton.interactable = true;
            return;
        }

        GameManager.Instance.JoinRoomById(roomCode);
    }
}