// ScoreManager.cs
using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    private float startTime;
    private bool running;

    public float FinalScore { get; private set; }
    public float FinishedDuration { get; private set; }

    public float FrozenScore { get; private set; }
    public int FrozenStars { get; private set; }

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
        FrozenStars = 0;
    }

    // ---------------- run flow ----------------
    public void StartRun()
    {
        startTime = Time.time;
        FinalScore = 0f;
        FinishedDuration = 0f;
        FrozenScore = 0f;
        FrozenStars = 0;
        running = true;
    }

    public void FinishLevel()
    {
        if (!running) return;

        float duration = Mathf.Max(0f, Time.time - startTime);
        FinishedDuration = duration;

        int stars = StarManager.Instance ? StarManager.Instance.Stars : 0;

        // simple score formula: stars weighted by speed
        FinalScore = Mathf.Max(0.0f, stars * (1000f / Mathf.Max(0.0001f, duration)));

        // snapshot for submission
        FrozenStars = stars;
        FrozenScore = FinalScore;

        running = false;
    }

    public void ApplyServerScore(double serverScore)
    {
        // prefer server’s authority if it supplies a score
        FinalScore = Mathf.Max(0f, (float)serverScore);
    }
}
