// PointSphere.cs
using UnityEngine;

public class PointSphere : MonoBehaviour
{
    [SerializeField] private int points = 10; // Value awarded on pickup

    private void OnTriggerEnter(Collider other)
    {
        // Only award points if player collides
        if (!other.CompareTag("Player"))
            return;

        StarManager.Instance.AddStars(points); // Update global star count
        Destroy(gameObject);                   // Remove from scene
    }
}
