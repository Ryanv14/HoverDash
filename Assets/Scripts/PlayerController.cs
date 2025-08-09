// PlayerController.cs
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class HoverVehicleController : MonoBehaviour
{
    [Header("Hover Settings")]
    [SerializeField] private float hoverHeight = 1f; // Ideal distance above ground
    [SerializeField] private float hoverForce = 1.5f;
    [SerializeField] private float stabilizationForce = 5f; // Torque to stay level

    [Header("Movement Speeds")]
    [SerializeField] private float moveSpeed = 25f;   // Forward/reverse
    [SerializeField] private float strafeSpeed = 20f; // Left/right

    [Header("Responsiveness")]
    [SerializeField, Tooltip("How quickly the vehicle stops when input ceases")]
    private float linearDrag = 1f;

    [Header("Jump")]
    [SerializeField] private int jumpCost = 10;             // stars per jump
    [SerializeField] private float jumpVelocityChange = 15f; // upward velocity delta
    [SerializeField] private float jumpCooldown = 0.25f;    // seconds between jumps
    [SerializeField] private float hoverSuspendTime = 0.25f;// seconds to disable hover after jump
    [SerializeField] private float groundCheckMultiplier = 1.25f; // ray length factor

    private Rigidbody rb;
    private float inputH, inputV;
    private float lastJumpTime = -999f;
    private float hoverResumeTime = 0f;
    private bool gravityWasEnabled = true;

    // Ground detection
    [SerializeField] private LayerMask groundMask = ~0; // default: everything

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation; // strafe-only
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearDamping = linearDrag;

        gravityWasEnabled = rb.useGravity;
    }

    private void Update()
    {
        inputV = Input.GetAxis("Vertical");
        inputH = Input.GetAxis("Horizontal");

        if (Input.GetKeyDown(KeyCode.Space))
            TryJump();
    }

    private void FixedUpdate()
    {
        // Re-enable gravity after the hover suspend window
        if (!rb.useGravity && Time.time >= hoverResumeTime)
            rb.useGravity = gravityWasEnabled;

        ApplyHover();
        ApplyMovement();
    }

    private void ApplyHover()
    {
        // Skip hover briefly after jumping so it cannot cancel the jump
        if (Time.time < hoverResumeTime)
            return;

        // Raycast down to detect ground and apply corrective forces
        if (Physics.Raycast(transform.position, Vector3.down, out var hit, hoverHeight * 2f, groundMask, QueryTriggerInteraction.Ignore))
        {
            float liftProportion = (hoverHeight - hit.distance) / hoverHeight;
            rb.AddForce(Vector3.up * liftProportion * hoverForce, ForceMode.Acceleration);

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
                        + transform.right   * inputH * strafeSpeed;

        // Only alter XZ plane, preserve vertical motion
        Vector3 currentXZ   = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        Vector3 velocityDiff = desired - currentXZ;
        rb.AddForce(velocityDiff, ForceMode.VelocityChange);
    }

    private bool IsGrounded()
    {
        float maxRay = Mathf.Max(hoverHeight * groundCheckMultiplier, 0.1f);
        return Physics.Raycast(transform.position, Vector3.down, maxRay, groundMask, QueryTriggerInteraction.Ignore);
    }

    private void TryJump()
    {
        if (Time.time - lastJumpTime < jumpCooldown)
            return;

        if (!IsGrounded())
            return;

        // Spend stars; only jump if we have enough starts (10 or more)
        if (StarManager.Instance != null && StarManager.Instance.SpendStars(jumpCost))
        {
            // Ensure we don't start with downward velocity
            var v = rb.linearVelocity;
            if (v.y < 0f) v.y = 0f;
            rb.linearVelocity = v;

            // Make the jump go upward, independent of mass
            rb.AddForce(Vector3.up * jumpVelocityChange, ForceMode.VelocityChange);

            // Temporarily disable gravity & hover so they don't cancel the jump
            gravityWasEnabled = rb.useGravity;
            rb.useGravity = false;
            hoverResumeTime = Time.time + hoverSuspendTime;

            lastJumpTime = Time.time;
        }
    }
}