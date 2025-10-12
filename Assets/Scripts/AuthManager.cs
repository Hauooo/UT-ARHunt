using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using System;

public class AuthManager : MonoBehaviour
{
    public static AuthManager Instance;

    public FirebaseUser User { get; private set; }
    public string UserId => User?.UserId ?? string.Empty;

    // Event to notify other parts of the game that sign-in is complete
    public event Action<FirebaseUser> OnSignedIn;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // First, check Firebase dependencies.
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                // If dependencies are fine, attempt to sign in.
                SignInAnonymously();
            }
            else
            {
                Debug.LogError("Could not resolve all Firebase dependencies: " + task.Result);
            }
        });
    }

    private void SignInAnonymously()
    {
        Debug.Log("Attempting to sign in anonymously...");
        FirebaseAuth.DefaultInstance.SignInAnonymouslyAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled || task.IsFaulted)
            {
                Debug.LogError("Anonymous sign-in failed: " + task.Exception);
                return;
            }

            // Sign-in successful
            User = task.Result.User;
            Debug.Log($"Sign-in successful! User ID: {User.UserId}");

            // Fire the event to let other managers (like TreasureManager) know we are ready.
            OnSignedIn?.Invoke(User);
        });
    }

    // Add this method to your AuthManager.cs script

    public void LinkAnonymousAccountWithEmail(string email, string password)
    {
        // Make sure we have an anonymous user to upgrade
        if (User == null || !User.IsAnonymous)
        {
            Debug.LogError("No anonymous user to link.");
            return;
        }

        Debug.Log($"Attempting to link anonymous account with email: {email}...");

        // Create the "credential" for the new login method
        Credential credential = EmailAuthProvider.GetCredential(email, password);

        // Link it to the CURRENT anonymous user
        User.LinkWithCredentialAsync(credential).ContinueWithOnMainThread(task => {
            if (task.IsCanceled || task.IsFaulted)
            {
                Debug.LogError("Account linking failed: " + task.Exception);
                return;
            }

            // IMPORTANT: The User object is updated, but the User ID STAYS THE SAME!
            User = task.Result.User;
            Debug.Log($"Anonymous account successfully upgraded! The User ID is still: {User.UserId}");

            // Now, this user is no longer anonymous and can sign in with their email on other devices.
        });
    }
}