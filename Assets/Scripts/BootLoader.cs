using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sits in the Boot scene on the Managers GameObject.
/// Waits one frame for all managers to initialize, then loads MainMenu.
/// </summary>
public class BootLoader : MonoBehaviour
{
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    private void Start()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }
}
