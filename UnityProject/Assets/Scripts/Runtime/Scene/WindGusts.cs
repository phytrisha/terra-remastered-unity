using UnityEngine;

/// Gust fronts for LowPolyFoliage: a band of stronger wind travels across the jungle every so often (plants sway up to
/// ~2.5x and lean downwind as it passes). Sets shader globals only; the shader keeps its calm sway without this
/// component (globals default to 0).
public class WindGusts : MonoBehaviour
{
    public Vector2 direction = new(0.8f, 0.6f);     // world xz, toward where the wind blows
    public float bandSpacing = 45f;                 // m between fronts
    public float frontSpeed = 6f;                   // m/s
    public float strength = 1.5f;                   // extra sway at the band's peak (x the calm sway)

    static readonly int ParamsId = Shader.PropertyToID("_GustParams"), TimeId = Shader.PropertyToID("_GustTime");
    float phase;

    void Update()
    {
        var d = direction.normalized;
        phase += Time.deltaTime * frontSpeed * (2f * Mathf.PI / bandSpacing);
        // fronts come and go: strength drifts slowly between calm and gusty
        float s = strength * Mathf.SmoothStep(0.15f, 1f, Mathf.PerlinNoise(Time.time * 0.03f, 0.5f));
        Shader.SetGlobalVector(ParamsId, new Vector4(d.x, d.y, 2f * Mathf.PI / bandSpacing, s));
        Shader.SetGlobalFloat(TimeId, phase);
    }

    void OnDisable() { Shader.SetGlobalVector(ParamsId, Vector4.zero); }
}
