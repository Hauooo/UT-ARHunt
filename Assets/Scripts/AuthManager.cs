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
}