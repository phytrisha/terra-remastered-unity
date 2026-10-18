using UnityEngine;

/// Drifting cumulus shadows on the far landscape (SoftShadows.hlsl CloudShadow: LowPolyLit, LowPolyFoliage, LowPolyWater):
/// sets the shader globals only. Faded in from fadeStart to fadeEnd metres from the camera, so the baked light around
/// the viewer stays as it is and the hills, lake and mountains get slow moving shade.
[ExecuteAlways]
public class CloudShadows : MonoBehaviour
{
    public float cloudSize = 140f;                  // m (noise period)
    [Range(0, 1)] public float strength = 0.55f;    // light removed under a cloud
    public float fadeStart = 40f, fadeEnd = 95f;    // m from the camera
    public Vector2 wind = new(2.4f, 3.2f);          // m/s drift (xz)

    static readonly int ParamsId = Shader.PropertyToID("_CloudShadow"), OffsetId = Shader.PropertyToID("_CloudOffset");
    Vector2 offset;

    void OnEnable() => Update();                    // edit mode: Update only runs on scene changes

    void Update()
    {
        offset -= wind * Time.deltaTime;
        Shader.SetGlobalVector(ParamsId, new Vector4(1f / cloudSize, strength, fadeStart, fadeEnd));
        Shader.SetGlobalVector(OffsetId, offset);
    }

    void OnDisable() { Shader.SetGlobalVector(ParamsId, Vector4.zero); }
}
