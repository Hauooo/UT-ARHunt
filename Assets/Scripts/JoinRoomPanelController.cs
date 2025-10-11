using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class JoinRoomPanelController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_InputField roomCodeInput;
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_Text feedbackText;

    // We'll wire up the 'Back' button in the Inspector to call MenuManager.OnBackButton()

    private void Awake()
    {
        joinButton.onClick.AddListener(OnJoinButtonClicked);
    }

    private void OnEnable()
    {
        // Reset the panel every time it's shown
        roomCodeInput.text = "";
        feedbackText.text = "Enter a 4-digit room code.";
        joinButton.interactable = true;
    }

    private void OnJoinButtonClicked()
    {
        string roomCode = roomCodeInput.text.Trim().ToUpper();

        if (string.IsNullOrWhiteSpace(roomCode) || roomCode.Length != 4)
        {
            feedbackText.text = "Please enter a valid 4-digit code.";
            return;
        }

        // Give immediate feedback and prevent spam clicks
        joinButton.interactable = false;
        feedbackText.text = $"Joining room {roomCode}...";

        // Ask the GameManager to handle the rest.
        // The MenuManager will listen for success/failure and switch panels.
        GameManager.Instance.JoinRoomById(roomCode);
    }
}