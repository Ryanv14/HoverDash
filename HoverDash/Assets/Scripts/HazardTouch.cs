// HazardTouch.cs 
using UnityEngine;

[DisallowMultipleComponent]
public class HazardTouch : MonoBehaviour
{
    [Tooltip("if true, use OnTriggerEnter (make this collider 'is trigger'). otherwise use collisions.")]
    public bool useTrigger = true;

    private bool _consumed; // stop double-firing

    private void Reset()
    {
        // editor helper: if we're using trigger mode, mark the collider accordingly
        var col = GetComponent<Collider>();
        if (useTrigger && col) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!useTrigger) return;       // wrong mode
        HandleHit(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (useTrigger) return;        // wrong mode
        HandleHit(collision.gameObject);
    }

    private void HandleHit(GameObject other)
    {
        if (_consumed) return;                 // already handled
        if (!other.CompareTag("Player")) return;

        _consumed = true;

        // show game over ui
        if (UIManager.Instance != null)
            UIManager.Instance.ShowGameOver();

        // stop player control
        var controller = other.GetComponent<HoverVehicleController>();
        if (controller) controller.enabled = false;

        // stop player motion (freeze where they died)
        var rb = other.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }
}

