// FinishLine.cs (persistent variant)
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider))]
public class FinishLine : MonoBehaviour
{
    private static FinishLine instance;
    private Collider finishCollider;

    private void Awake()
    {
        if (instance && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);

        finishCollider = GetComponent<Collider>();
        finishCollider.isTrigger = true;

        // Re-enable on every scene load
        SceneManager.sceneLoaded += (_, __) => { if (finishCollider) finishCollider.enabled = true; };
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= (_, __) => { if (finishCollider) finishCollider.enabled = true; };
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        var gm = FindObjectOfType<GameManager>();
        if (gm != null)
            gm.FinishRun();
        else
        {
            ScoreManager.Instance.FinishLevel();
            UIManager.Instance.ShowLevelComplete(ScoreManager.Instance.FinalScore);
        }

        if (finishCollider) finishCollider.enabled = false;
    }
}
