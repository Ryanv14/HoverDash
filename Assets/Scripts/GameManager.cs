// GameManager.cs
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    private void Start()
    {
        // Begin timing immediately when the scene loads
        UIManager.Instance.StartTimer();
    }

    public void StartLevel()
    {
        // Called by UI button: restart the timer for a new run
        UIManager.Instance.StartTimer();
    }

    public void RestartLevel()
    {
        // Reload current scene to reset everything
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}

