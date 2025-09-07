// HazardTouch.cs 
using UnityEngine;

[DisallowMultipleComponent]
public class HazardTouch : MonoBehaviour
{
    [Tooltip("If true, use OnTriggerEnter. Make this collider 'Is Trigger'.")]
    public bool useTrigger = true;

    private bool _consumed; // prevent double-firing

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (useTrigger && col) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!useTrigger) return;
        HandleHit(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (useTrigger) return;
        HandleHit(collision.gameObject);
    }

    private void HandleHit(GameObject other)
    {
        if (_consumed) return;
        if (!other.CompareTag("Player")) return;

        _consumed = true;

        // Show game over UI
        if (UIManager.Instance != null)
            UIManager.Instance.ShowGameOver();

        // Stop player control
        var controller = other.GetComponent<HoverVehicleController>();
        if (controller) controller.enabled = false;

        // Stop player motion
        var rb = other.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }
}
