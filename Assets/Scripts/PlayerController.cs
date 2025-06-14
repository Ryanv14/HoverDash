// Scripts/PlayerController.cs
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Speeds")]
    public float forwardSpeed = 10f;   // Speed when moving forward/backward
    public float strafeSpeed = 8f;    // Speed when strafing left/right

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        // Read input:
        //   Vertical:  W/S or Up/Down arrows - move forward/backward
        //   Horizontal: A/D or Left/Right arrows - strafe left/right
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        // Calculate forward/backward velocity
        Vector3 forwardVelocity = transform.forward * v * forwardSpeed;
        // Calculate sideways velocity
        Vector3 strafeVelocity = transform.right * h * strafeSpeed;

        Vector3 newVelocity = new Vector3(
            forwardVelocity.x + strafeVelocity.x,
            rb.linearVelocity.y,
            forwardVelocity.z + strafeVelocity.z
        );

        rb.linearVelocity = newVelocity;
    }
}
