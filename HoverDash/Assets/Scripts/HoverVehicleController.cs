// HoverVehicleController.cs
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

    // ---------------- AUDIO ----------------
    [Header("Audio")]
    [SerializeField, Tooltip("Looping engine/hover sound (seamless loop).")]
    private AudioClip hoverLoop;
    [SerializeField, Tooltip("Optional jump SFX (one-shot).")]
    private AudioClip jumpSfx;
    [SerializeField, Tooltip("Optional landing SFX (one-shot).")]
    private AudioClip landSfx;

    [SerializeField, Tooltip("AudioSource used for the engine loop. If not set, one will be created.")]
    private AudioSource engineSource;
    [SerializeField, Tooltip("AudioSource for one-shots (jump/land). If not set, one will be created.")]
    private AudioSource sfxSource;

    [SerializeField, Tooltip("Pitch at rest.")]
    private float basePitch = 1f;
    [SerializeField, Tooltip("Additional pitch from flat speed (units per second).")]
    private float pitchFromSpeed = 0.02f;
    [SerializeField, Tooltip("How quickly pitch/volume follow targets.")]
    private float audioSmoothing = 8f;
    [SerializeField, Tooltip("Max pitch clamp for the engine.")]
    private float maxPitch = 2f;
    [SerializeField, Tooltip("Engine volume when grounded.")]
    private float volumeWhenGrounded = 0.9f;
    [SerializeField, Tooltip("Engine volume when airborne (no ground hits).")]
    private float volumeWhenAirborne = 0.6f;
    [SerializeField, Tooltip("Extra volume when player is actively giving input.")]
    private float volumeBoostOnInput = 0.1f;
    [SerializeField, Tooltip("3D rolloff min distance.")]
    private float min3DDistance = 2f;
    [SerializeField, Tooltip("3D rolloff max distance.")]
    private float max3DDistance = 25f;

    // ---------------------------------------

    private Rigidbody rb;
    private float inputH, inputV;
    private float lastJumpTime = -999f;
    private float hoverResumeTime = 0f;
    private bool gravityWasEnabled = true;
    private bool wasGrounded = false;

    [SerializeField] private LayerMask groundMask = ~0;

    // ---------------- public toggles / helpers ----------------
    public void SetMovementEnabled(bool enabled) => movementEnabled = enabled;
    public void SetControlsEnabled(bool enabled) => SetMovementEnabled(enabled);

    public void ZeroOutVelocity()
    {
        if (!rb) return;
        // instant stop (used at run gates)
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    // ---------------- lifecycle ----------------
    private void Awake()
    {
        // ensure audio sources exist (keeps scene clean if not wired in Inspector)
        if (!engineSource)
        {
            var go = new GameObject("Engine_AudioSource");
            go.transform.SetParent(transform, false);
            engineSource = go.AddComponent<AudioSource>();
        }
        if (!sfxSource)
        {
            var go = new GameObject("SFX_AudioSource");
            go.transform.SetParent(transform, false);
            sfxSource = go.AddComponent<AudioSource>();
        }

        ConfigureEngineSource();
        ConfigureSfxSource();
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;                // let us manage tilt ourselves
        rb.interpolation = RigidbodyInterpolation.Interpolate;               // smoother visuals
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearDamping = linearDrag;
        gravityWasEnabled = rb.useGravity;

        // kick off engine loop if assigned
        if (hoverLoop)
        {
            engineSource.clip = hoverLoop;
            engineSource.loop = true;
            if (!engineSource.isPlaying) engineSource.Play();
        }

        wasGrounded = IsGrounded();
        SetEngineInstant(wasGrounded, 0f);                                   // bring audio to a sane starting state
    }

    private void OnEnable()
    {
        // resume loop if we were paused by disable
        if (hoverLoop && engineSource && !engineSource.isPlaying)
            engineSource.Play();
    }

    private void OnDisable()
    {
        // pause (not stop) to keep time position
        if (engineSource) engineSource.Pause();
    }

    // ---------------- run flow / input ----------------
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
            // hard gate: ignore player input
            inputV = 0f;
            inputH = 0f;
        }

        UpdateEngineAudio();
    }

    private void FixedUpdate()
    {
        // re-enable gravity after the brief jump suspension window
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
            // when locked, bleed horizontal velocity and keep upright yaw-only
            Vector3 velFlat = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
            rb.AddForce(-velFlat, ForceMode.VelocityChange);

            Quaternion yawOnly = Quaternion.LookRotation(transform.forward, Vector3.up);
            Quaternion smoothed = Quaternion.Slerp(rb.rotation, yawOnly, 10f * Time.fixedDeltaTime);
            rb.MoveRotation(smoothed);
        }

        // landing tick (edge trigger) for SFX
        bool grounded = IsGrounded();
        if (!wasGrounded && grounded)
            PlayOneShot(landSfx);
        wasGrounded = grounded;
    }

    // ---------------- hover & movement ----------------
    private void GetFlatBasis(out Vector3 fwdFlat, out Vector3 rightFlat)
    {
        // derive a horizontal basis from the hovercraft's forward
        Vector3 up = Vector3.up;
        fwdFlat = Vector3.ProjectOnPlane(transform.forward, up);
        if (fwdFlat.sqrMagnitude < 1e-6f)
            fwdFlat = Vector3.ProjectOnPlane(transform.up, up);
        fwdFlat.Normalize();
        rightFlat = Vector3.Cross(up, fwdFlat).normalized;
    }

    private void ApplyHover()
    {
        // during jump suspension, skip adding hover forces
        if (Time.time < hoverResumeTime)
            return;

        int n = Mathf.Max(1, hoverPointsLocal.Length);
        // split gravity compensation across hover points (slightly overcompensated by multiplier)
        float gravityShare = (rb.mass * Physics.gravity.magnitude / n) * hoverGravityCompensation;

        int hits = 0;
        for (int i = 0; i < hoverPointsLocal.Length; i++)
        {
            Vector3 p = transform.TransformPoint(hoverPointsLocal[i]);

            // sample ground below each hover point
            if (Physics.Raycast(p, Vector3.down, out var hit, hoverHeight * 2f, groundMask, QueryTriggerInteraction.Ignore))
            {
                hits++;

                float error = hoverHeight - hit.distance;                    // positive if we're too low
                float pointVelUp = Vector3.Dot(rb.GetPointVelocity(p), Vector3.up);

                // basic spring-damper with gravity compensation per point
                float up = gravityShare + (error * hoverSpring) + (-pointVelUp * hoverDamper);
                if (up < 0f) up = 0f;                                        // don't pull down

                rb.AddForceAtPosition(Vector3.up * up, p, ForceMode.Force);
            }
            else
            {
                // no ground under this point → small lift to feel less "dead" in mid-air
                rb.AddForceAtPosition(Vector3.up * (gravityShare * 0.25f), p, ForceMode.Force);
            }
        }

        // gentle upright torque, with roll target coming from input/velocity
        float targetRollDeg = movementEnabled ? ComputeTargetRollDegrees() : 0f;
        Vector3 desiredUp = Quaternion.AngleAxis(targetRollDeg, transform.forward) * Vector3.up;
        Vector3 uprightTorque = Vector3.Cross(transform.up, desiredUp) * stabilizationForce;
        rb.AddTorque(uprightTorque, ForceMode.Acceleration);

        // if none of the points saw ground, fake some lift so we don't plummet too fast
        if (hits == 0)
            rb.AddForce(Vector3.up * (rb.mass * Physics.gravity.magnitude * -2f), ForceMode.Force);
    }

    private void ApplyMovement()
    {
        GetFlatBasis(out var fwdFlat, out var rightFlat);

        // target flat velocity from input (impulse towards desired)
        Vector3 desired = fwdFlat * (inputV * moveSpeed) + rightFlat * (inputH * strafeSpeed);
        Vector3 velFlat = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        Vector3 velocityDiff = desired - velFlat;

        rb.AddForce(velocityDiff, ForceMode.VelocityChange);
    }

    private float ComputeTargetRollDegrees()
    {
        // input roll plus a bit of "real" banking from lateral speed
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

        // keep yaw from current forward, overlay roll, then smooth
        Quaternion yawOnly = Quaternion.LookRotation(transform.forward, Vector3.up);
        Quaternion targetRot = Quaternion.AngleAxis(targetRollDeg, transform.forward) * yawOnly;
        Quaternion smoothed = Quaternion.Slerp(rb.rotation, targetRot, bankSmoothing * Time.fixedDeltaTime);

        rb.MoveRotation(smoothed);
    }

    private bool IsGrounded()
    {
        // slightly longer ray than hoverHeight to make contact stable at edges/ramps
        float maxRay = Mathf.Max(hoverHeight * groundCheckMultiplier, 0.1f);
        return Physics.Raycast(transform.position, Vector3.down, maxRay, groundMask, QueryTriggerInteraction.Ignore);
    }

    private void TryJump()
    {
        if (!movementEnabled) return;
        if (Time.time - lastJumpTime < jumpCooldown) return;
        if (!IsGrounded()) return;

        // uses SpendForJump so Zen levels don't charge stars
        if (StarManager.Instance != null && StarManager.Instance.SpendForJump(jumpCost))
        {
            // cancel downward velocity so small hops feel responsive
            var v = rb.linearVelocity;
            if (v.y < 0f) v.y = 0f;
            rb.linearVelocity = v;

            rb.AddForce(Vector3.up * jumpVelocityChange, ForceMode.VelocityChange);

            // briefly disable gravity so the hover springs don't fight the launch
            gravityWasEnabled = rb.useGravity;
            rb.useGravity = false;
            hoverResumeTime = Time.time + hoverSuspendTime;

            lastJumpTime = Time.time;
            PlayOneShot(jumpSfx);
        }
    }

    // ---------------- audio ----------------
    private void ConfigureEngineSource()
    {
        if (!engineSource) return;
        engineSource.playOnAwake = false;
        engineSource.loop = true;
        engineSource.spatialBlend = 1f;                 // fully 3D
        engineSource.dopplerLevel = 0.5f;
        engineSource.rolloffMode = AudioRolloffMode.Logarithmic;
        engineSource.minDistance = min3DDistance;
        engineSource.maxDistance = max3DDistance;
        engineSource.volume = 0f;                       // fade in via logic
        engineSource.pitch = basePitch;
        engineSource.priority = 64;                     // keep SFX audible
    }

    private void ConfigureSfxSource()
    {
        if (!sfxSource) return;
        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 1f;
        sfxSource.dopplerLevel = 0.5f;
        sfxSource.rolloffMode = AudioRolloffMode.Logarithmic;
        sfxSource.minDistance = min3DDistance;
        sfxSource.maxDistance = max3DDistance;
        sfxSource.priority = 128;
    }

    private void UpdateEngineAudio()
    {
        if (!engineSource) return;

        // pitch from flat speed (keeps airborne pitch sensible)
        Vector3 velFlat = Vector3.ProjectOnPlane(rb ? rb.linearVelocity : Vector3.zero, Vector3.up);
        float speed = velFlat.magnitude;
        float targetPitch = Mathf.Min(basePitch + speed * pitchFromSpeed, maxPitch);

        // volume from grounded state with a small input boost
        bool grounded = IsGrounded();
        float targetVol = grounded ? volumeWhenGrounded : volumeWhenAirborne;

        bool hasInput = movementEnabled && (Mathf.Abs(inputH) > 0.01f || Mathf.Abs(inputV) > 0.01f);
        if (hasInput) targetVol += volumeBoostOnInput;
        targetVol = Mathf.Clamp01(targetVol);

        // gentle follow so audio feels stable
        float t = Time.deltaTime * audioSmoothing;
        engineSource.pitch = Mathf.Lerp(engineSource.pitch, targetPitch, t);
        engineSource.volume = Mathf.Lerp(engineSource.volume, targetVol, t);

        // lazy-start loop if clip was assigned late
        if (hoverLoop && !engineSource.isPlaying)
        {
            engineSource.clip = hoverLoop;
            engineSource.Play();
        }
    }

    private void SetEngineInstant(bool grounded, float speed)
    {
        if (!engineSource) return;
        engineSource.pitch = Mathf.Min(basePitch + speed * pitchFromSpeed, maxPitch);
        float v = grounded ? volumeWhenGrounded : volumeWhenAirborne;
        engineSource.volume = Mathf.Clamp01(v);
    }

    private void PlayOneShot(AudioClip clip)
    {
        if (!clip || !sfxSource) return;
        sfxSource.PlayOneShot(clip);
    }

#if UNITY_EDITOR
    // simple viz for hover points and target height
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
