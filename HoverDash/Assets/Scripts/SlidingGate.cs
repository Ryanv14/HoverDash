// SlidingGate.cs
using UnityEngine;

public class SlidingGate : MonoBehaviour
{
    [Header("Panels")]
    public Transform panelLeft;
    public Transform panelRight;

    [Header("Motion")]
    [Tooltip("How far each panel travels from its start local position.")]
    public float travel = 1.25f;
    [Tooltip("Seconds for a full open->closed->open cycle.")]
    public float cycleSeconds = 2.5f;
    [Tooltip("0 = start open, 0.5 = start closed, etc.")]
    [Range(0f, 1f)] public float phaseOffset = 0f;
    public AnimationCurve easing = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Axis")]
    [Tooltip("Panels slide along this local axis (usually X).")]
    public Vector3 slideAxis = Vector3.right;

    // cached
    private Vector3 _leftStart;
    private Vector3 _rightStart;

    void Awake()
    {
        if (panelLeft) _leftStart = panelLeft.localPosition;
        if (panelRight) _rightStart = panelRight.localPosition;
    }

    void Update()
    {
        if (!panelLeft || !panelRight || cycleSeconds <= 0f) return;

        // normalized time 0..1
        float t = Mathf.Repeat((Time.time / cycleSeconds) + phaseOffset, 1f);

        // ping-pong 0->1->0 with custom easing
        float open01 = t <= 0.5f ? (t / 0.5f) : (1f - (t - 0.5f) / 0.5f);
        open01 = easing.Evaluate(open01);

        // closed when open01=0, fully open when open01=1
        float offset = (1f - open01) * travel;

        Vector3 dir = slideAxis.normalized;
        panelLeft.localPosition = _leftStart - dir * offset;
        panelRight.localPosition = _rightStart + dir * offset;
    }

#if UNITY_EDITOR
    void OnValidate() { slideAxis = slideAxis == Vector3.zero ? Vector3.right : slideAxis; }
#endif
}
