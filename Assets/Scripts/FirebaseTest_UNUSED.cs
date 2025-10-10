using UnityEngine;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;


public class FirebaseTest : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available)
            {
                // Create and hold a reference to your FirebaseApp, i.e.
                //   app = Firebase.FirebaseApp.DefaultInstance;
                // Set a flag here to indicate whether Firebase is ready to use by your app.
                Debug.Log("Firebase is ready to use.");
                
                // Example of writing to the database
                var dbUrl = "https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/";
                var dbRef = FirebaseDatabase.GetInstance(dbUrl);

                DatabaseReference reference = dbRef.RootReference;
                if (reference != null) {
                    Debug.Log("Database reference obtained successfully.");
                } else {
                    Debug.LogError("Failed to obtain database reference.");
                }

                reference.Child("test").SetValueAsync("Hello, Firebase!");
            }
            else
            {
                UnityEngine.Debug.LogError(System.String.Format(
                  "Could not resolve all Firebase dependencies: {0}", dependencyStatus));
                // Firebase Unity SDK is not safe to use here.
            }
        });
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
