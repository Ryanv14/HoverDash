//OrbitHazard.cs
using UnityEngine;

public class OrbitingHazards : MonoBehaviour
{
    [Header("Structure")]
    public Transform pivot; // rotates to move orbs

    [Header("Motion")]
    public float degreesPerSecond = 90f;
    [Tooltip("Randomize direction (CW/CCW) at start for variation.")]
    public bool randomizeDirection = true;
    [Range(0f, 1f)] public float randomPhase = 0f; // random starting angle

    [Header("Bob (optional)")]
    public bool verticalBob = false;
    public float bobAmplitude = 0.25f;
    public float bobSpeed = 1.5f;

    private float _startAngle;
    private float _dir = 1f;
    private Vector3 _pivotStartPos;

    void Awake()
    {
        if (!pivot) pivot = transform;
        _pivotStartPos = pivot.localPosition;

        if (randomizeDirection) _dir = Random.value < 0.5f ? -1f : 1f;
        _startAngle = randomPhase * 360f;
        pivot.localRotation = Quaternion.Euler(0f, _startAngle, 0f);
    }

    void Update()
    {
        if (!pivot) return;

        // constant angular velocity
        pivot.localRotation *= Quaternion.Euler(0f, _dir * degreesPerSecond * Time.deltaTime, 0f);

        if (verticalBob)
        {
            float y = Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
            pivot.localPosition = _pivotStartPos + new Vector3(0f, y, 0f);
        }
    }
}
