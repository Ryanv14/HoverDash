// ColumnObstacle.cs
using UnityEngine;

public class ColumnObstacle : MonoBehaviour
{
    [SerializeField] private GameObject gameOverPanel; // UI panel shown on crash
    [SerializeField] private GameObject timerText;     // In-game timer display
    [SerializeField] private GameObject starText;      // In-game star count display

    private void OnCollisionEnter(Collision collision)
    {
        // Only react when the player hits this obstacle
        if (!collision.gameObject.CompareTag("Player"))
            return;

        // Activate game-over screen, hide HUD elements
        UIManager.Instance.ShowGameOver();

        // Stop the player from moving any further
        var hoverController = collision.gameObject.GetComponent<HoverVehicleController>();
        if (hoverController)
            hoverController.enabled = false;
    }
}

