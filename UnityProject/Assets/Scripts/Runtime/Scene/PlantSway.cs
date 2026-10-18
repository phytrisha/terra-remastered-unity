using UnityEngine;

/// Cheap wind for a fixed-camera vista: rotates the plant root a little around its pivot with two sine layers.
/// Only on plants near the viewer (the builder adds it); far vegetation stays static.
public class PlantSway : MonoBehaviour
{
    public float amplitude = 2.5f;     // degrees
    public float frequency = 0.35f;    // Hz
    float phase; Quaternion baseRot;

    void OnEnable()
    {
        baseRot = transform.localRotation;
        var p = transform.position;
        phase = p.x * 0.37f + p.z * 0.21f;   // neighbours move together like a gust front
    }

    void Update()
    {
        float t = Time.time * frequency * Mathf.PI * 2f + phase;
        float gust = 0.6f + 0.4f * Mathf.Sin(Time.time * 0.13f + phase * 0.2f);
        float ax = (Mathf.Sin(t) + 0.35f * Mathf.Sin(t * 2.7f + 1.3f)) * amplitude * gust;
        float az = Mathf.Sin(t * 0.8f + 2.1f) * amplitude * 0.6f * gust;
        transform.localRotation = baseRot * Quaternion.Euler(ax, 0f, az);
    }
}
