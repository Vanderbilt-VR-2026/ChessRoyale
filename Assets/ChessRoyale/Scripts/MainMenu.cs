using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public void StartGame()
    {
        SceneManager.LoadScene("ChessRoyaleVR");
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}