// PointStar.cs
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class PointStar : MonoBehaviour
{
    [Header("Pickup")]
    [SerializeField] private int points = 10;

    [Header("Audio")]
    [SerializeField] private AudioClip collectSfx;
    [SerializeField, Range(0f, 1f)] private float collectSfxVolume = 0.8f;

    [Header("Bobbing")]
    [SerializeField, Min(0f)] private float amplitude = 0.15f;
    [SerializeField, Min(0f)] private float frequency = 1.0f;
    [SerializeField] private bool randomizePhase = true;

    private Vector3 baseLocalPos;
    private float phase;
    private Rigidbody rb;
    private Transform parent;

    void Awake()
    {
        // make sure the collider works as a trigger
        var col = GetComponent<Collider>();
        col.isTrigger = true;

        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        parent = transform.parent;
        baseLocalPos = transform.localPosition;
        phase = randomizePhase ? Random.value * Mathf.PI * 2f : 0f;
    }

    void OnEnable()
    {
        // reset anchor when reused from pool
        baseLocalPos = transform.localPosition;
    }

    void FixedUpdate()
    {
        float t = (Time.time * Mathf.PI * 2f * frequency) + phase;
        float offset = Mathf.Sin(t) * amplitude;

        Vector3 targetLocal = baseLocalPos + Vector3.up * offset;
        Vector3 targetWorld = parent ? parent.TransformPoint(targetLocal) : targetLocal;

        if (rb != null)
            rb.MovePosition(targetWorld);
        else
            transform.position = targetWorld;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        PlayOneShot2D(collectSfx, collectSfxVolume);

        StarManager.Instance.AddStars(points);
        Destroy(gameObject);
    }

    private static void PlayOneShot2D(AudioClip clip, float volume)
    {
        if (!clip) return;

        var go = new GameObject("OneShot2D_Audio");
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.spatialBlend = 0f; // force 2D
        src.dopplerLevel = 0f;
        src.playOnAwake = false;
        src.loop = false;

        src.Play();
        Object.Destroy(go, clip.length);
    }

#if UNITY_EDITOR
    void Start()
    {
        // guard rail for physics setup: OnTriggerEnter requires at least one Rigidbody
        var player = GameObject.FindGameObjectWithTag("Player");
        bool playerHasRB = player && player.GetComponent<Rigidbody>() != null;
        bool starHasRB = TryGetComponent<Rigidbody>(out _);
        if (!playerHasRB && !starHasRB)
        {
            Debug.LogWarning("PointStar: Neither the Player nor this Star has a Rigidbody. OnTriggerEnter will not fire. Add a kinematic Rigidbody to the star or a Rigidbody to the Player.");
        }
    }
#endif
}
