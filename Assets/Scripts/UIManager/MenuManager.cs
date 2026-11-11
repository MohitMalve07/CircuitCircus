using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    private const string GAME_SCENE = "Game";
    private const string SETTINGS_SCENE = "Settings";
    private const string SHOP_SCENE = "Shop";

    private void Start()
    {
        GameSettings.ApplySceneAudio();
    }

    /// <summary>
    /// Loads the main game scene.
    /// </summary>
    public void Play()
    {
        SceneManager.LoadScene(GAME_SCENE);
    }

    /// <summary>
    /// Loads the settings scene additively so it overlays the current scene.
    /// Prevents duplicate loading if already active.
    /// </summary>
    public void OpenSettings()
    {
        if (!IsSceneLoaded(SETTINGS_SCENE))
        {
            SceneManager.LoadScene(SETTINGS_SCENE, LoadSceneMode.Additive);
        }
        else
        {
            Debug.LogWarning($"Scene '{SETTINGS_SCENE}' is already loaded.");
        }
    }

    /// <summary>
    /// Loads the shop scene.
    /// </summary>
    public void OpenShop()
    {
        SceneManager.LoadScene(SHOP_SCENE);
    }

    /// <summary>
    /// Quits the application.
    /// </summary>
    public void ExitGame()
    {
        Debug.Log("Exiting Application...");
        Application.Quit();
    }

    /// <summary>
    /// Helper method to check if a specific scene is currently loaded.
    /// </summary>
    private bool IsSceneLoaded(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).name == sceneName)
            {
                return true;
            }
        }
        return false;
    }
}

