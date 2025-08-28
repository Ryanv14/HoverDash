// ScoreManager.cs
using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    private float startTime;
    public float FinalScore { get; private set; }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        // Record when the level begins
        startTime = Time.time;
    }

    // Call when a new run starts (even without scene reload)
    public void StartRun()
    {
        startTime = Time.time;
        FinalScore = 0f;
    }

    // Local scoring path (used on non-leaderboard levels)
    public void FinishLevel()
    {
        float duration = Time.time - startTime;
        int stars = StarManager.Instance != null ? StarManager.Instance.Stars : 0;
        FinalScore = Mathf.Max(0.0f, stars * (1000f / Mathf.Max(0.0001f, duration)));
    }

    // Sets the score returned by the server
    public void ApplyServerScore(double serverScore)
    {
        FinalScore = Mathf.Max(0f, (float)serverScore);
    }
}




