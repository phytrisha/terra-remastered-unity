using UnityEngine;

/// Butterflies fluttering around flowering plants (heliconias) near the viewer. Each one: a body and two hinged wings
/// (children, so flapping is a rotation, no skinning); it flies a jittery loop around its anchor, flaps in bursts with
/// short glides, and now and then moves to another anchor. Built by RainforestBuilder.Life; costs a few dozen tiny
/// shadowless renderers and one Update.
public class Butterflies : MonoBehaviour
{
    public Transform[] anchors;
    public Mesh bodyMesh, wingMesh;
    public Material bodyMat;
    public Material[] wingMats;
    public int count = 12;
    public float radius = 1.4f, height = 1.1f, speed = 0.9f;
    public float size = 1f;                         // x the mesh (~10 cm wingspan): stylized larger, to read from the viewer

    class B { public Transform root, wl, wr; public Vector3 vel; public int anchor; public float phase, rate, seed, glide, hop; }
    B[] bs;

    void Start()
    {
        if (anchors == null || anchors.Length == 0) { enabled = false; return; }
        bs = new B[count];
        for (int i = 0; i < count; i++)
        {
            var b = new B { anchor = i % anchors.Length, seed = Random.value * 100f, rate = Random.Range(7f, 11f), phase = Random.value * 6.3f, hop = Random.Range(8f, 20f) };
            b.root = new GameObject("Butterfly_" + i).transform; b.root.SetParent(transform, false);
            b.root.position = anchors[b.anchor].position + Random.insideUnitSphere * radius + Vector3.up * height;
            b.root.localScale = Vector3.one * Random.Range(1.6f, 2.3f) * size;
            Part("Body", b.root, bodyMesh, bodyMat, false);
            var mat = wingMats[i % wingMats.Length];
            b.wl = Part("WingL", b.root, wingMesh, mat, true); b.wr = Part("WingR", b.root, wingMesh, mat, false);
            bs[i] = b;
        }
    }

    static Transform Part(string name, Transform parent, Mesh mesh, Material mat, bool mirror)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        if (mirror) go.transform.localScale = new Vector3(-1, 1, 1);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
        return go.transform;
    }

    void Update()
    {
        float t = Time.time, dt = Time.deltaTime;
        foreach (var b in bs)
        {
            if ((b.hop -= dt) <= 0f) { b.anchor = Random.Range(0, anchors.Length); b.hop = Random.Range(8f, 20f); }
            var a = anchors[b.anchor].position;
            // a wandering point around the anchor: slow loop + fast jitter (the typical zig-zag)
            float s = b.seed;
            var loop = new Vector3(Mathf.Sin(t * 0.37f + s), 0.4f * Mathf.Sin(t * 0.53f + s * 2f), Mathf.Cos(t * 0.31f + s)) * radius;
            var jit = new Vector3(Mathf.PerlinNoise(t * 1.7f, s) - 0.5f, Mathf.PerlinNoise(s, t * 1.9f) - 0.5f, Mathf.PerlinNoise(t * 1.3f + s, s) - 0.5f) * 1.2f;
            var target = a + Vector3.up * height + loop + jit;
            var want = Vector3.ClampMagnitude((target - b.root.position) * 1.5f, speed);
            b.vel = Vector3.Lerp(b.vel, want, 1f - Mathf.Exp(-3f * dt));
            // flap in bursts with short glides; each downstroke lifts the body a little
            b.glide = Mathf.PerlinNoise(t * 0.8f, s + 7f) > 0.68f ? Mathf.MoveTowards(b.glide, 1f, dt * 4f) : Mathf.MoveTowards(b.glide, 0f, dt * 6f);
            b.phase += b.rate * Mathf.PI * 2f * dt * (1f - 0.8f * b.glide);
            float flap = Mathf.Sin(b.phase);
            b.root.position += (b.vel + Vector3.up * 0.25f * flap * (1f - b.glide)) * dt;
            var flat = new Vector3(b.vel.x, 0f, b.vel.z);
            if (flat.sqrMagnitude > 1e-4f) b.root.rotation = Quaternion.Slerp(b.root.rotation, Quaternion.LookRotation(flat), 1f - Mathf.Exp(-6f * dt));
            float ang = Mathf.Lerp(10f + 70f * (0.5f + 0.5f * flap), 12f, b.glide);
            b.wr.localRotation = Quaternion.Euler(0, 0, ang);
            b.wl.localRotation = Quaternion.Euler(0, 0, -ang);
        }
    }
}
