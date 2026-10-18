using UnityEngine;

/// Small involuntary motion on top of the clips (after the Animator and QuadrupedLocomotion): an ear flicks every few
/// seconds (one at a time, sometimes both), and while the animal stands the tail swishes in bursts. Additive rotations
/// about the head / body axes, so it works on any of the quadruped rigs (ear_L/R, earHD_L/R, tail1..n).
public class AnimalIdleLife : MonoBehaviour
{
    public Transform head;
    public Transform[] earsL, earsR, tail;
    public AnimalWander wander;
    public float flickDeg = 28f, swishDeg = 9f;

    float nextFlick, flickT = -1f, swishT = -1f, nextSwish; int flickSide; float seed;

    void Start() { seed = Random.value * 50f; nextFlick = Time.time + Random.Range(1f, 4f); nextSwish = Time.time + Random.Range(2f, 6f); }

    void LateUpdate()
    {
        float t = Time.time;
        // ---- ear flick: quick back-and-return, 0.28 s
        if (t >= nextFlick) { flickT = 0f; flickSide = Random.value < 0.25f ? 2 : Random.Range(0, 2); nextFlick = t + Random.Range(2.5f, 7f); }
        if (flickT >= 0f && head)
        {
            flickT += Time.deltaTime;
            float u = flickT / 0.28f, k = u < 1f ? Mathf.Sin(Mathf.PI * u) * Mathf.Exp(-1.5f * u) : 0f;
            if (u >= 1f) flickT = -1f;
            var axis = head.right;
            if (flickSide != 1) foreach (var e in earsL) if (e) e.rotation = Quaternion.AngleAxis(-flickDeg * k, axis) * Quaternion.AngleAxis(-12f * k, head.up) * e.rotation;
            if (flickSide != 0) foreach (var e in earsR) if (e) e.rotation = Quaternion.AngleAxis(-flickDeg * k, axis) * Quaternion.AngleAxis(12f * k, head.up) * e.rotation;
        }
        // ---- tail swish while standing: a travelling wave, stronger toward the tip, in bursts of ~2.5 s
        bool standing = !wander || !wander.enabled || !wander.Walking;
        if (standing && swishT < 0f && t >= nextSwish) swishT = 0f;
        if (swishT >= 0f && tail != null && tail.Length > 0)
        {
            swishT += Time.deltaTime;
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(swishT / 2.5f));
            if (swishT >= 2.5f) { swishT = -1f; nextSwish = t + Random.Range(3f, 9f); }
            var up = transform.up;
            for (int i = 0; i < tail.Length; i++)
            {
                if (!tail[i]) continue;
                float a = swishDeg * env * (0.4f + 0.6f * i / Mathf.Max(1, tail.Length - 1)) * Mathf.Sin(t * 3.1f + seed - i * 0.6f);
                tail[i].rotation = Quaternion.AngleAxis(a, up) * tail[i].rotation;
            }
        }
    }
}
