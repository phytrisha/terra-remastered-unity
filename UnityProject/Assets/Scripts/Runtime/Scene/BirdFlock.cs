using UnityEngine;

/// A small flock crossing the sky every 20-45 s: dark low-poly birds in a loose V along a gentle arc over the lake /
/// canopy, flapping with glides. Distance and side vary per pass. Built by RainforestBuilder.Life.
public class BirdFlock : MonoBehaviour
{
    public Mesh bodyMesh, wingMesh;
    public Material mat;
    public Transform viewer;
    public Vector2 interval = new(20f, 45f);
    public Vector2Int birds = new(4, 8);
    public float speed = 11f, wingspan = 1.3f;

    class Bird { public Transform root, wl, wr; public Vector3 offset; public float phase, rate; }
    Bird[] pool;
    float wait, t, dur; Vector3 p0, p1, p2; int n;

    void Start()
    {
        pool = new Bird[birds.y];
        for (int i = 0; i < pool.Length; i++)
        {
            var b = new Bird { root = new GameObject("Bird_" + i).transform, rate = Random.Range(3.2f, 4.2f), phase = Random.value * 6.3f };
            b.root.SetParent(transform, false); b.root.localScale = Vector3.one * wingspan;
            Part("Body", b.root, bodyMesh, false); b.wl = Part("WingL", b.root, wingMesh, true); b.wr = Part("WingR", b.root, wingMesh, false);
            b.root.gameObject.SetActive(false);
            pool[i] = b;
        }
        wait = Random.Range(4f, 10f);                 // the first pass comes soon
    }

    Transform Part(string name, Transform parent, Mesh mesh, bool mirror)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        if (mirror) go.transform.localScale = new Vector3(-1, 1, 1);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return go.transform;
    }

    public void TestPassAhead() { forceAhead = true; StartPass(); }   // test hook: a pass over the lake right now
    bool forceAhead;

    void StartPass()
    {
        var c = viewer ? viewer.position : Vector3.zero;
        // mostly over the lake (+Z, the main view), sometimes across the canopy behind or to the sides
        float yaw = Random.value < 0.65f ? Random.Range(-35f, 35f) : Random.Range(0f, 360f);
        float dist = Random.Range(35f, 95f), h = Random.Range(14f, 32f), half = Random.Range(80f, 120f);
        if (forceAhead) { yaw = 8f; dist = 60f; h = 15f; }
        var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward; var side = Vector3.Cross(Vector3.up, fwd) * (Random.value < 0.5f ? 1 : -1);
        var mid = c + fwd * dist + Vector3.up * h;
        p0 = mid - side * half + Vector3.up * Random.Range(-6f, 6f); p2 = mid + side * half + Vector3.up * Random.Range(-6f, 6f);
        p1 = mid + fwd * Random.Range(-25f, 25f) + Vector3.up * Random.Range(0f, 8f);
        dur = (p2 - p0).magnitude / speed; t = 0f;
        forceAhead = false;
        n = Random.Range(birds.x, birds.y + 1);
        for (int i = 0; i < pool.Length; i++)
        {
            var b = pool[i]; bool on = i < n; b.root.gameObject.SetActive(on);
            int row = (i + 1) / 2, s = i % 2 == 0 ? 1 : -1;                    // loose V: leader, then pairs behind
            b.offset = new Vector3(s * row * 1.8f, Random.Range(-0.6f, 0.6f), -row * 2.2f) + Random.insideUnitSphere * 0.5f;
        }
    }

    static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, float u) => Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(b, c, u), u);

    void Update()
    {
        float dt = Time.deltaTime;
        if (dur <= 0f) { if ((wait -= dt) <= 0f) StartPass(); return; }
        t += dt;
        float u = t / dur;
        if (u >= 1f) { foreach (var b in pool) b.root.gameObject.SetActive(false); dur = 0f; wait = Random.Range(interval.x, interval.y); return; }
        var pos = Bez(p0, p1, p2, u); var dir = (Bez(p0, p1, p2, Mathf.Min(1f, u + 0.01f)) - pos).normalized;
        var rot = Quaternion.LookRotation(dir);
        for (int i = 0; i < n; i++)
        {
            var b = pool[i];
            float glide = Mathf.PerlinNoise(Time.time * 0.25f, i * 3.7f) > 0.55f ? 1f : 0f;
            b.phase += b.rate * Mathf.PI * 2f * dt * (1f - 0.85f * glide);
            float flap = Mathf.Sin(b.phase);
            b.root.SetPositionAndRotation(pos + rot * b.offset + Vector3.up * 0.15f * flap, rot * Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 0.7f + i) * 6f));
            float ang = Mathf.Lerp(35f * flap, 4f, glide);
            b.wr.localRotation = Quaternion.Euler(0, 0, ang); b.wl.localRotation = Quaternion.Euler(0, 0, -ang);
        }
    }
}
