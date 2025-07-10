// ScoreManager.cs
using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    private float startTime;
    public float FinalScore { get; private set; }

    private void Awake()
    {
        // Standard singleton setup
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        // Record the moment level begins
        startTime = Time.time;
    }

    public void FinishLevel()
    {
        // Called when player crosses the finish line
        float duration = Time.time - startTime;
        int stars = StarManager.Instance.Stars;

        // Scoring: faster & more stars = higher score
        FinalScore = (1000f / duration) * Mathf.Sqrt(stars);
    }
}


