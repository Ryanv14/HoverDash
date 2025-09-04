// ScoreManager.cs
using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    private float startTime;
    private bool running;

    public float FinalScore { get; private set; }

    
    public float FinishedDuration { get; private set; }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        running = false;
        FinalScore = 0f;
        FinishedDuration = 0f;
    }

    public void StartRun()
    {
        startTime = Time.time;
        FinalScore = 0f;
        FinishedDuration = 0f;
        running = true;
    }

    public void FinishLevel()
    {
        if (!running) return;

        float duration = Time.time - startTime;
        FinishedDuration = Mathf.Max(0f, duration);

        int stars = StarManager.Instance != null ? StarManager.Instance.Stars : 0;
        FinalScore = Mathf.Max(0.0f, stars * (1000f / Mathf.Max(0.0001f, FinishedDuration)));

        running = false;
    }

    public void ApplyServerScore(double serverScore)
    {
        FinalScore = Mathf.Max(0f, (float)serverScore);
    }
}



