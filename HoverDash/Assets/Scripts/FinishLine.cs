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

        // Some setups need a kinematic rigidbody on triggers to register consistently
        if (ensureKinematicRigidbody)
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    // Lets the same finish line be reused without a scene reload
    public void ResetGate()
    {
        if (finishCollider) finishCollider.enabled = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        Debug.Log("[FinishLine] Player crossed the finish line.");

        var gm = FindFirstObjectByType<GameManager>();
        if (gm != null)
        {
            gm.FinishRun();
        }
        else
        {
            // Fallback if no GameManager is present
            Debug.LogWarning("[FinishLine] GameManager not found; using local score fallback.");
            ScoreManager.Instance.FinishLevel();
            UIManager.Instance.StopTimer();
            UIManager.Instance.ShowLevelComplete(ScoreManager.Instance.FinalScore);
        }

        // Disable until reset to avoid multiple triggers in one run
        if (finishCollider) finishCollider.enabled = false;
    }
}
