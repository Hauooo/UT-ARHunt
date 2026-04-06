using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Auth;
using Firebase.Extensions;
using System;

public class UsernameSetupController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button cancelButton;   // optional (hide for first-time)
    [SerializeField] private TMP_Text feedbackText;

    [SerializeField] private int minLength = 3;
    [SerializeField] private int maxLength = 20;

    private bool isMandatory = false; // true on first app open if missing username
    public event Action<string> OnUsernameSaved; // Optional event for other scripts to react to username changes

    private void Awake()
    {
        if (saveButton != null)
        {
            saveButton.onClick.RemoveAllListeners();
            saveButton.onClick.AddListener(OnSaveClicked);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(CloseIfAllowed);
        }
    }

    public void Open(bool mandatory)
    {
        isMandatory = mandatory;

        var user = FirebaseAuth.DefaultInstance?.CurrentUser;
        string currentName = user?.DisplayName ?? "";

        if (usernameInput != null) usernameInput.text = currentName;
        if (feedbackText != null) feedbackText.text = mandatory ? "Please set your username." : "";

        if (cancelButton != null) cancelButton.gameObject.SetActive(!mandatory);
        if (panelRoot != null) panelRoot.SetActive(true);
    }

    public void CloseIfAllowed()
    {
        if (isMandatory) return;
        if (panelRoot != null) panelRoot.SetActive(false);

        var menuManager = FindObjectOfType<MenuManager>();
        if (menuManager != null)
        {
            menuManager.ShowPanel(menuManager.mainMenuPanel);
        };
    }

    private void OnSaveClicked()
    {
        var user = FirebaseAuth.DefaultInstance?.CurrentUser;
        if (user == null) { SetFeedback("Not signed in."); return; }

        string name = usernameInput != null ? usernameInput.text.Trim() : "";
        if (!IsValid(name))
        {
            SetFeedback($"Username must be {minLength}-{maxLength} chars.");
            return;
        }

        SetFeedback("Saving...");
        saveButton.interactable = false;
        if (cancelButton != null) cancelButton.interactable = false;

        var profile = new UserProfile { DisplayName = name };

        user.UpdateUserProfileAsync(profile).ContinueWithOnMainThread(task =>
        {
            saveButton.interactable = true;
            if (cancelButton != null) cancelButton.interactable = true;

            if (task.IsFaulted || task.IsCanceled)
            {
                SetFeedback("Failed to save username.");
                Debug.LogError("[UsernameSetup] Save failed: " + task.Exception);
                return;
            }

            // Optional but recommended: mirror username to RTDB for scoreboard consistency
            // FirebaseDatabase.GetInstance("https://...").RootReference
            //     .Child("users").Child(user.UserId).Child("displayName").SetValueAsync(name);

            SetFeedback("Saved!");
            Debug.Log($"[UsernameSetup] Username updated to: {name}");
            OnUsernameSaved?.Invoke(name);

            if (panelRoot != null) panelRoot.SetActive(false);
            isMandatory = false;

            var menuManager = FindObjectOfType<MenuManager>();
            if (menuManager != null)
                menuManager.ShowPanel(menuManager.mainMenuPanel);
        });
    }

    private bool IsValid(string n)
    {
        if (string.IsNullOrWhiteSpace(n)) return false;
        return n.Length >= minLength && n.Length <= maxLength;
    }

    private void SetFeedback(string msg)
    {
        if (feedbackText != null) feedbackText.text = msg;
    }
}