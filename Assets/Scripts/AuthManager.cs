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
            Debug.Log($"[Auth] AuthManager Awake() instance created. Scene={gameObject.scene.name} id={GetInstanceID()}");
        }
        else
        {
            Debug.LogWarning($"[Auth] Duplicate AuthManager destroyed. Scene={gameObject.scene.name} id={GetInstanceID()}");
            Destroy(gameObject);
        }
    }

    void Start()
    {
        Debug.Log($"[Auth] Start() begin dependency check. Scene={gameObject.scene.name} id={GetInstanceID()}");

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError("[Auth] Dependency check faulted/canceled: " + task.Exception);
                return;
            }

            Debug.Log($"[Auth] Dependency status={task.Result}");
            if (task.Result == DependencyStatus.Available)
            {
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
        Debug.Log("[Auth] Attempting to sign in anonymously...");
        FirebaseAuth.DefaultInstance.SignInAnonymouslyAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled || task.IsFaulted)
            {
                Debug.LogError("[Auth] Anonymous sign-in failed: " + task.Exception);
                return;
            }

            User = task.Result.User;
            Debug.Log($"[Auth] Sign-in successful. UserId={User.UserId} IsAnonymous={User.IsAnonymous}");

            Debug.Log("[Auth] Firing OnSignedIn event.");
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