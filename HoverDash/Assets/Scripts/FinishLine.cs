// FinishLine.cs
using UnityEngine;

public class FinishLine : MonoBehaviour
{
    private static FinishLine instance;
    private Collider finishCollider;

    private void Awake()
    {
        // Enforce a single persistent finish-line object across reloads
        if (instance && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        // Cache collider so we can disable it after trigger
        finishCollider = GetComponent<Collider>();
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only trigger when the player crosses the line
        if (!other.CompareTag("Player"))
            return;

        // Calculate score and show the completion UI
        ScoreManager.Instance.FinishLevel();
        float score = ScoreManager.Instance.FinalScore;
        UIManager.Instance.ShowLevelComplete(score);

        // Prevent double-triggering
        if (finishCollider)
            finishCollider.enabled = false;
    }
}


