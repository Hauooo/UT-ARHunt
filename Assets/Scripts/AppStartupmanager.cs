using UnityEngine;

/// <summary>
/// Runs on app startup to check if username is set.
/// Uses AuthManager to detect when user is signed in.
/// If username not set, forces the player to set one before continuing.
/// </summary>
public class AppStartupManager : MonoBehaviour
{
    [SerializeField] private MenuManager menuManager;
    [SerializeField] private UsernameSetupController usernameSetupController;

    private void Start()
    {
        // Subscribe to AuthManager's signed-in event
        if (AuthManager.Instance != null)
        {
            AuthManager.Instance.OnSignedIn += HandleUserSignedIn;

            // If user is already signed in (e.g., scene reload)
            if (AuthManager.Instance.User != null)
            {
                HandleUserSignedIn(AuthManager.Instance.User);
            }
        }
        else
        {
            Debug.LogError("[AppStartup] AuthManager.Instance is null!");
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe to prevent memory leaks
        if (AuthManager.Instance != null)
        {
            AuthManager.Instance.OnSignedIn -= HandleUserSignedIn;
        }
    }

    /// <summary>
    /// Called when user is signed in (by AuthManager)
    /// </summary>
    private void HandleUserSignedIn(Firebase.Auth.FirebaseUser user)
    {
        Debug.Log($"[AppStartup] User signed in: {user.UserId}");

        if (string.IsNullOrEmpty(user.DisplayName))
        {
            Debug.Log("[AppStartup] Username not set. Opening setup dialog...");

            if (usernameSetupController != null)
            {
                // Subscribe to username saved event
                usernameSetupController.OnUsernameSaved += HandleUsernameSaved;

                // Open setup as mandatory
                usernameSetupController.Open(mandatory: true);
            }
            else
            {
                Debug.LogError("[AppStartup] UsernameSetupController not assigned!");
            }
        }
        else
        {
            Debug.Log($"[AppStartup] Username already set: {user.DisplayName}");
            ShowMainMenu();
        }
    }

    /// <summary>
    /// Called when username is successfully saved
    /// </summary>
    private void HandleUsernameSaved(string username)
    {
        Debug.Log($"[AppStartup] Username saved: {username}. Showing main menu...");

        // Unsubscribe to prevent duplicate calls
        if (usernameSetupController != null)
            usernameSetupController.OnUsernameSaved -= HandleUsernameSaved;

        ShowMainMenu();
    }

    private void ShowMainMenu()
    {
        if (menuManager != null)
        {
            menuManager.ShowPanel(menuManager.mainMenuPanel);
        }
        else
        {
            Debug.LogError("[AppStartup] MenuManager not assigned!");
        }
    }
}