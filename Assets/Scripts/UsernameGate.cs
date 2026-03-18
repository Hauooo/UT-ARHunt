using UnityEngine;
using Firebase.Auth;
using System.Collections;

public class UsernameGate : MonoBehaviour
{
    [SerializeField] private UsernameSetupController usernameSetup;

    private IEnumerator Start()
    {
        while (FirebaseAuth.DefaultInstance == null || FirebaseAuth.DefaultInstance.CurrentUser == null)
            yield return null;

        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        bool missing = string.IsNullOrWhiteSpace(user.DisplayName);

        if (missing)
            usernameSetup.Open(mandatory: true);
    }
}