// GameManager.cs

using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    private const string leaderboardSceneName = "Level2"; // change if needed
    private const string leaderboardLevelId = "level-2";

    [SerializeField] private NamePromptUI namePromptUI;   // assign in Inspector
    [SerializeField] private LeaderboardUI leaderboardUI;  // assign in Inspector 

    private bool isLeaderboardLevel;
    private LeaderboardClient lb;

    private void Awake()
    {
        lb = FindFirstObjectByType<LeaderboardClient>();
        isLeaderboardLevel = SceneManager.GetActiveScene().name == leaderboardSceneName;

        if (isLeaderboardLevel && lb == null)
            Debug.LogWarning("Level 2 is a leaderboard level but no LeaderboardClient was found.");
    }

    private void Start() => StartRun();

    public void StartLevel() => StartRun();

    private void StartRun()
    {
        UIManager.Instance.StartTimer();
        ScoreManager.Instance.StartRun();

        if (isLeaderboardLevel && lb != null)
            StartCoroutine(lb.StartLevel(leaderboardLevelId));

        if (namePromptUI) namePromptUI.Hide();
        if (leaderboardUI) leaderboardUI.Hide();
    }

    public void FinishRun()
    {
        if (isLeaderboardLevel && lb != null && namePromptUI != null)
        {
            // Ask for a name first (no persistence; blank prefill)
            namePromptUI.Show(
                prefill: "",
                onConfirm: name =>
                {
                    // Submit to backend using the provided name just once
                    StartCoroutine(lb.FinishLevel(leaderboardLevelId, StarManager.Instance.Stars, name, serverScore =>
                    {
                        ScoreManager.Instance.ApplyServerScore(serverScore);
                        UIManager.Instance.ShowLevelComplete(ScoreManager.Instance.FinalScore);

                        // Optionally fetch & show leaderboard
                        if (leaderboardUI)
                            StartCoroutine(lb.GetLeaderboard(leaderboardLevelId,
                                rows => leaderboardUI.Show(rows),
                                err => Debug.LogWarning("Leaderboard fetch failed: " + err)));
                    }));
                },
                onCancel: () =>
                {
                    // If cancelled, don't submit; just show local score
                    ScoreManager.Instance.FinishLevel();
                    UIManager.Instance.ShowLevelComplete(ScoreManager.Instance.FinalScore);
                }
            );
        }
        else
        {
            // Non-leaderboard levels or missing UI/client: local-only
            ScoreManager.Instance.FinishLevel();
            UIManager.Instance.ShowLevelComplete(ScoreManager.Instance.FinalScore);
        }
    }

    public void RestartLevel()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}


