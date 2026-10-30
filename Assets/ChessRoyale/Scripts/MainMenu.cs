using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public static bool LaunchMultiplayer { get; private set; }

    public void StartSinglePlayer()
    {
        LaunchMultiplayer = false;
        SceneManager.LoadScene("ChessRoyaleVR");
    }

    public void StartMultiplayer()
    {
        LaunchMultiplayer = true;
        SceneManager.LoadScene("ChessRoyaleVR");
    }

    public void StartGame()
    {
        StartSinglePlayer();
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}