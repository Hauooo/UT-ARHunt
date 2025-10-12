using UnityEngine;
using UnityEngine.SceneManagement;

public class Bootstrapper : MonoBehaviour
{
    private static bool hasBootstrapped = false;

    void Start()
    {
        if (hasBootstrapped)
        {
            Debug.Log("Bootstrapper skipped — already ran once.");
            Destroy(gameObject);
            return;
        }

        hasBootstrapped = true;
        DontDestroyOnLoad(gameObject);

        Debug.Log("Bootstrapper starting game flow...");
        SceneManager.LoadScene("MenuScene");
    }
}
