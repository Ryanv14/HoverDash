// ScoreManager.cs
using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    private float startTime;
    private bool running;

    // What the UI should show
    public float FinalScore { get; private set; }

    // Frozen at finish line; sent to server
    public float FinishedDuration { get; private set; }

    public float FrozenScore { get; private set; }
    public bool ScoreLocked { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        running = false;
        FinalScore = 0f;
        FinishedDuration = 0f;
        FrozenScore = 0f;
        ScoreLocked = false;
    }

    public void StartRun()
    {
        startTime = Time.time;
        FinalScore = 0f;
        FinishedDuration = 0f;
        FrozenScore = 0f;
        ScoreLocked = false;
        running = true;
    }

    public void FinishLevel()
    {
        if (!running) return;

        float duration = Time.time - startTime;
        FinishedDuration = Mathf.Max(0f, duration);

        int stars = StarManager.Instance != null ? StarManager.Instance.Stars : 0;

        float score = stars * (1000f / Mathf.Max(0.0001f, FinishedDuration));

        FinalScore = Mathf.Max(0f, score);

        // Snapshot + lock for the UI
        FrozenScore = FinalScore;
        ScoreLocked = true;

        running = false;
    }

    // Called when server replies
    public void ApplyServerScore(double serverScore)
    {
        if (!ScoreLocked)
        {
            FinalScore = Mathf.Max(0f, (float)serverScore);
        }
    }
}


