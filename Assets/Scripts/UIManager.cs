// UIManager.cs
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("In-Game HUD")]
    [SerializeField] private TextMeshProUGUI starText;  // Displays current stars
    [SerializeField] private TextMeshProUGUI timerText; // Displays elapsed time

    [Header("End-Game Screens")]
    [SerializeField] private GameObject gameOverPanel;        // Shown on crash
    [SerializeField] private GameObject levelCompletePanel;   // Shown on success
    [SerializeField] private TextMeshProUGUI finalScoreText;  // Displays final score

    private float startTime;
    private bool timerRunning;

    private void Awake()
    {
        // Singleton pattern for easy access
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        // Reset UI elements on each load
        starText.text = StarManager.Instance.Stars.ToString();
        timerText.text = "0.00";
        gameOverPanel.SetActive(false);
        levelCompletePanel.SetActive(false);
        timerRunning = false;

        // Optionally start timing immediately
        StartTimer();
    }

    private void Update()
    {
        if (!timerRunning) return;

        // Update timer display every frame
        float elapsed = Time.time - startTime;
        timerText.text = elapsed.ToString("F2");
    }

    /// Call this to begin or restart the level timer.
    public void StartTimer()
    {
        startTime = Time.time;
        timerRunning = true;
    }

    /// Update the star count in the HUD.
    public void UpdateStarCount(int stars) =>
        starText.text = stars.ToString();

    /// Show the Game Over screen and stop the timer.
    public void ShowGameOver()
    {
        timerRunning = false;
        gameOverPanel.SetActive(true);
    }

    /// Show the Level Complete screen with the final score.
    public void ShowLevelComplete(float finalScore)
    {
        timerRunning = false;
        levelCompletePanel.SetActive(true);
        if (finalScoreText)
            finalScoreText.text = $"Final Score: {finalScore:F1}";
    }

    /// Button handler: return to main menu.
    public void GoToMainMenu() =>
        SceneManager.LoadScene("MainMenu");

    /// Reload the current scene, resetting both game and UI.
    public void ResetUI() =>
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
}


