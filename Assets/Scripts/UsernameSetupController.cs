using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Auth;
using Firebase.Extensions;

public class UsernameSetupController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject panelRott;
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private Button saveButton;
    [SerializeField] private TMP_Text feedbackText;

    [Header("Rules")]
    [SerializeField] private int minLength = 3;
    [SerializeField] private int maxLength = 20;

    private void Awake()
    {
        if (saveButton != null)
        {
            saveButton.onClick.RemoveAllListeners();
            saveButton.onClick.AddListener(OnSaveClicked);
        }

        ShowIfNeeded();
    }

    public void ShowIfNeeded()
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            Debug.LogError("[UsernameSetup] No authenticated user found.");
            panelRott.SetActive(false);
            return;
        }
        if (string.IsNullOrEmpty(user.DisplayName))
        {
            panelRott.SetActive(true);
            feedbackText.text = $"Please choose a username ({minLength}-{maxLength} chars).";
        }
        else
        {
            panelRott.SetActive(false);
        }
    }


    private void OnSaveClicked()
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            Debug.LogError("[UsernameSetup] No authenticated user found.");
            feedbackText.text = "Error: No authenticated user.";
            return;
        }
        string newName = usernameInput.text.Trim();
        if (newName.Length < minLength || newName.Length > maxLength)
        {
            feedbackText.text = $"Username must be {minLength}-{maxLength} characters.";
            return;
        }
        saveButton.interactable = false;
        feedbackText.text = "Saving username...";
        UserProfile profile = new UserProfile { DisplayName = newName };
        user.UpdateUserProfileAsync(profile).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError("[UsernameSetup] Failed to update username: " + task.Exception);
                feedbackText.text = "Failed to save username. Try again.";
                saveButton.interactable = true;
            }
            else
            {
                feedbackText.text = "Username saved!";
                panelRott.SetActive(false);
            }
        });
    }

    private bool IsValidUsername(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length < minLength || name.Length > maxLength) return false;
        // Additional checks (e.g. profanity filter) can be added here
        return true;
    }

    private void SetFeedback(string message)
    {
        if (feedbackText != null) feedbackText.text = message;
    }
}