using UnityEngine;
using UnityEngine.SceneManagement;

public class Bootstrapper : MonoBehaviour
{
    void Start()
    {
        // This ensures your managers are initialized before loading the menu.
        SceneManager.LoadScene("MenuScene"); // Make sure your menu scene is named this
    }
}