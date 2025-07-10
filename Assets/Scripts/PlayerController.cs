// HoverVehicleController.cs
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class HoverVehicleController : MonoBehaviour
{
    [Header("Hover Settings")]
    [SerializeField] private float hoverHeight = 1f; // Ideal distance above ground
    [SerializeField] private float hoverForce = 1.5f;
    [SerializeField] private float stabilizationForce = 5f; // Torque to stay level

    [Header("Movement Speeds")]
    [SerializeField] private float moveSpeed = 25f; // Forward/reverse
    [SerializeField] private float strafeSpeed = 20f; // Left/right

    [Header("Responsiveness")]
    [SerializeField, Tooltip("How quickly the vehicle stops when input ceases")]
    private float linearDrag = 1f;

    private Rigidbody rb;
    private float inputH, inputV;

    private void Start()
    {
        // Grab Rigidbody and configure physics for smooth hovering
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearDamping = linearDrag;
    }

    private void Update()
    {
        // Read player input each frame
        inputV = Input.GetAxis("Vertical");
        inputH = Input.GetAxis("Horizontal");
    }

    private void FixedUpdate()
    {
        // Physics updates: maintain hover and apply movement
        ApplyHover();
        ApplyMovement();
    }

    private void ApplyHover()
    {
        // Raycast down to detect ground and apply corrective forces
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, hoverHeight * 2f))
        {
            float liftProportion = (hoverHeight - hit.distance) / hoverHeight;
            rb.AddForce(Vector3.up * liftProportion * hoverForce, ForceMode.Acceleration);

            // Keep vehicle oriented upright
            Vector3 torque = Vector3.Cross(transform.up, Vector3.up) * stabilizationForce;
            rb.AddTorque(torque, ForceMode.Acceleration);
        }
        else
        {
            // If too far from ground, fall naturally
            rb.AddForce(Vector3.down * hoverForce, ForceMode.Acceleration);
        }
    }

    private void ApplyMovement()
    {
        // Compute desired horizontal velocity vector
        Vector3 desired = transform.forward * inputV * moveSpeed
                        + transform.right * inputH * strafeSpeed;

        // Only alter XZ plane, preserve vertical motion
        Vector3 currentXZ = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        Vector3 velocityDiff = desired - currentXZ;
        rb.AddForce(velocityDiff, ForceMode.VelocityChange);
    }
}










