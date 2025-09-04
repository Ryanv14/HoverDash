// PlayerController.cs
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class HoverVehicleController : MonoBehaviour
{
    [Header("Hover Settings")]
    [SerializeField] private float hoverHeight = 1f;

    [SerializeField, Tooltip("Small upright assistance")]
    private float stabilizationForce = 2f;

    [Header("Movement Speeds")]
    [SerializeField] private float moveSpeed = 25f;
    [SerializeField] private float strafeSpeed = 20f;

    [Header("Responsiveness")]
    [SerializeField, Tooltip("How quickly the vehicle stops when input ceases")]
    private float linearDrag = 1f;

    [Header("Jump")]
    [SerializeField] private int jumpCost = 10;
    [SerializeField] private float jumpVelocityChange = 15f;
    [SerializeField] private float jumpCooldown = 0.25f;
    [SerializeField] private float hoverSuspendTime = 0.25f;
    [SerializeField] private float groundCheckMultiplier = 1.25f;

    [Header("Banking (Lean)")]
    [SerializeField, Tooltip("Max roll angle (degrees) when fully strafing")]
    private float maxBankAngle = 25f;
    [SerializeField, Tooltip("How quickly the craft settles to its banked orientation")]
    private float bankSmoothing = 8f;
    [SerializeField, Tooltip("Extra bank from actual lateral velocity (0 = none)")]
    private float velBankFactor = 0.5f;

    [Header("Hover Suspension (multi-point)")]
    [SerializeField, Tooltip("Spring strength at each hover point")]
    private float hoverSpring = 400f;
    [SerializeField, Tooltip("Vertical damping at each hover point")]
    private float hoverDamper = 50f;
    [SerializeField, Tooltip("Multiply the per-point gravity share (1.0 = exact)")]
    private float hoverGravityCompensation = 1.02f;
    [SerializeField, Tooltip("Local-space hover points")]
    private Vector3[] hoverPointsLocal = new Vector3[]
    {
        new Vector3( 0.7f, 0f,  0.7f),
        new Vector3(-0.7f, 0f,  0.7f),
        new Vector3( 0.7f, 0f, -0.7f),
        new Vector3(-0.7f, 0f, -0.7f)
    };

    [Header("Run Gate")]
    [SerializeField, Tooltip("Movement & input are ignored when disabled. Hover still works.")]
    private bool movementEnabled = false;

    private Rigidbody rb;
    private float inputH, inputV;
    private float lastJumpTime = -999f;
    private float hoverResumeTime = 0f;
    private bool gravityWasEnabled = true;

    [SerializeField] private LayerMask groundMask = ~0;

    // Public toggles/wrappers
    public void SetMovementEnabled(bool enabled) => movementEnabled = enabled;
    public void SetControlsEnabled(bool enabled) => SetMovementEnabled(enabled);
    public void ZeroOutVelocity()
    {
        if (!rb) return;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearDamping = linearDrag;
        gravityWasEnabled = rb.useGravity;
    }

    private void Update()
    {
        if (movementEnabled)
        {
            inputV = Input.GetAxis("Vertical");
            inputH = Input.GetAxis("Horizontal");

            if (Input.GetKeyDown(KeyCode.Space))
                TryJump();
        }
        else
        {
            inputV = 0f;
            inputH = 0f;
        }
    }

    private void FixedUpdate()
    {
        if (!rb.useGravity && Time.time >= hoverResumeTime)
            rb.useGravity = gravityWasEnabled;

        ApplyHover();

        if (movementEnabled)
        {
            ApplyMovement();
            ApplyBanking();
        }
        else
        {
            Vector3 velFlat = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
            rb.AddForce(-velFlat, ForceMode.VelocityChange);

            Quaternion yawOnly = Quaternion.LookRotation(transform.forward, Vector3.up);
            Quaternion smoothed = Quaternion.Slerp(rb.rotation, yawOnly, 10f * Time.fixedDeltaTime);
            rb.MoveRotation(smoothed);
        }
    }

    private void GetFlatBasis(out Vector3 fwdFlat, out Vector3 rightFlat)
    {
        Vector3 up = Vector3.up;
        fwdFlat = Vector3.ProjectOnPlane(transform.forward, up);
        if (fwdFlat.sqrMagnitude < 1e-6f)
            fwdFlat = Vector3.ProjectOnPlane(transform.up, up);
        fwdFlat.Normalize();
        rightFlat = Vector3.Cross(up, fwdFlat).normalized;
    }

    private void ApplyHover()
    {
        if (Time.time < hoverResumeTime)
            return;

        int n = Mathf.Max(1, hoverPointsLocal.Length);
        float gravityShare = (rb.mass * Physics.gravity.magnitude / n) * hoverGravityCompensation;

        int hits = 0;

        for (int i = 0; i < hoverPointsLocal.Length; i++)
        {
            Vector3 p = transform.TransformPoint(hoverPointsLocal[i]);

            if (Physics.Raycast(p, Vector3.down, out var hit, hoverHeight * 2f, groundMask, QueryTriggerInteraction.Ignore))
            {
                hits++;

                float error = hoverHeight - hit.distance;
                float pointVelUp = Vector3.Dot(rb.GetPointVelocity(p), Vector3.up);

                float up = gravityShare + (error * hoverSpring) + (-pointVelUp * hoverDamper);
                if (up < 0f) up = 0f;

                rb.AddForceAtPosition(Vector3.up * up, p, ForceMode.Force);
            }
            else
            {
                rb.AddForceAtPosition(Vector3.up * (gravityShare * 0.25f), p, ForceMode.Force);
            }
        }

        float targetRollDeg = movementEnabled ? ComputeTargetRollDegrees() : 0f;
        Vector3 desiredUp = Quaternion.AngleAxis(targetRollDeg, transform.forward) * Vector3.up;
        Vector3 uprightTorque = Vector3.Cross(transform.up, desiredUp) * stabilizationForce;
        rb.AddTorque(uprightTorque, ForceMode.Acceleration);

        if (hits == 0)
            rb.AddForce(Vector3.up * (rb.mass * Physics.gravity.magnitude * -2f), ForceMode.Force);
    }

    private void ApplyMovement()
    {
        GetFlatBasis(out var fwdFlat, out var rightFlat);
        Vector3 desired = fwdFlat * (inputV * moveSpeed) + rightFlat * (inputH * strafeSpeed);
        Vector3 velFlat = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        Vector3 velocityDiff = desired - velFlat;
        rb.AddForce(velocityDiff, ForceMode.VelocityChange);
    }

    private float ComputeTargetRollDegrees()
    {
        float target = -inputH * maxBankAngle;
        GetFlatBasis(out var _, out var rightFlat);
        Vector3 velFlat = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        float lateralSpeed = Vector3.Dot(velFlat, rightFlat);
        float lateral01 = Mathf.Clamp(lateralSpeed / Mathf.Max(1f, strafeSpeed), -1f, 1f);
        target += -lateral01 * (maxBankAngle * velBankFactor);
        return target;
    }

    private void ApplyBanking()
    {
        float targetRollDeg = ComputeTargetRollDegrees();
        Quaternion yawOnly = Quaternion.LookRotation(transform.forward, Vector3.up);
        Quaternion targetRot = Quaternion.AngleAxis(targetRollDeg, transform.forward) * yawOnly;
        Quaternion smoothed = Quaternion.Slerp(rb.rotation, targetRot, bankSmoothing * Time.fixedDeltaTime);
        rb.MoveRotation(smoothed);
    }

    private bool IsGrounded()
    {
        float maxRay = Mathf.Max(hoverHeight * groundCheckMultiplier, 0.1f);
        return Physics.Raycast(transform.position, Vector3.down, maxRay, groundMask, QueryTriggerInteraction.Ignore);
    }

    private void TryJump()
    {
        if (!movementEnabled) return;
        if (Time.time - lastJumpTime < jumpCooldown) return;
        if (!IsGrounded()) return;

        if (StarManager.Instance != null && StarManager.Instance.SpendStars(jumpCost))
        {
            var v = rb.linearVelocity;
            if (v.y < 0f) v.y = 0f;
            rb.linearVelocity = v;

            rb.AddForce(Vector3.up * jumpVelocityChange, ForceMode.VelocityChange);

            gravityWasEnabled = rb.useGravity;
            rb.useGravity = false;
            hoverResumeTime = Time.time + hoverSuspendTime;

            lastJumpTime = Time.time;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        GetFlatBasis(out var _, out var rightFlat);
        Gizmos.color = Color.yellow;
        if (hoverPointsLocal != null)
        {
            foreach (var lp in hoverPointsLocal)
            {
                Vector3 p = Application.isPlaying ? transform.TransformPoint(lp) : (transform.position + transform.rotation * lp);
                Gizmos.DrawSphere(p, 0.07f);
                Gizmos.DrawLine(p, p + Vector3.down * hoverHeight);
            }
        }
    }
#endif
}
