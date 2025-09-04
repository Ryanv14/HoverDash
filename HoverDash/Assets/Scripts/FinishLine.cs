// FinishLine.cs
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class FinishLine : MonoBehaviour
{
    [Tooltip("Keep ON if your Player uses CharacterController. Ensures triggers fire reliably.")]
    [SerializeField] private bool ensureKinematicRigidbody = true;

    private Collider finishCollider;
    private Rigidbody rb;

    private void Awake()
    {
        finishCollider = GetComponent<Collider>();
        finishCollider.isTrigger = true; // must be a trigger

        // Ensure there's a kinematic rigidbody on this trigger 
        if (ensureKinematicRigidbody)
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    // Re-enable the trigger for another run (useful if you don't reload the scene)
    public void ResetGate()
    {
        if (finishCollider) finishCollider.enabled = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        Debug.Log("[FinishLine] Player crossed the finish line.");

        // Delegate finish logic to GameManager
        var gm = FindFirstObjectByType<GameManager>();
        if (gm != null)
        {
            gm.FinishRun();
        }
        else
        {
            Debug.LogWarning("[FinishLine] GameManager not found; using local score fallback.");
            ScoreManager.Instance.FinishLevel();
            UIManager.Instance.StopTimer();
            UIManager.Instance.ShowLevelComplete(ScoreManager.Instance.FinalScore);
        }

        // Prevent double-triggering until next run/reset
        if (finishCollider) finishCollider.enabled = false;
    }
}
