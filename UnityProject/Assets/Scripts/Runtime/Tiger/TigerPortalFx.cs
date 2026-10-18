using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Effects that sell the low-poly -> HD swap during the tiger's pounce (driven by TigerEncounter, wired by
/// RainforestBuilder.Encounter). Timeline in pounce time t (game seconds, the clip's own clock):
///   crouch     low-poly tiger charges up: energy shell on its facets, sparkles rising off the skin, camera pushes in
///   takeoff    FOV punch, camera shake, lens-distortion kick, motion blur
///   swap       (the portal rim covers the tiger) bullet time, camera arcs around the tiger with a dutch roll, the
///              low-poly facets shatter off as glowing shards, sparkle burst from the HD skin, rim flash, chromatic hit
///   after      HD tiger materializes (scan band head -> tail, its shell cools off), time ramps back to normal and the
///              camera rushes into the showcase framing with motion blur
/// All particles live on the PortalFX layer, which both the forest and the portal camera render, so they look the
/// same on both sides of the rim. Camera offsets are applied on top of TigerEncounter's glide pose each frame.
/// Particles are emitted from the skinned surfaces in code (baked pose, random triangle points): Unity's skinned-mesh
/// shape mis-places them under the FBX axis-conversion node (rotation 270, scale 130) -- 4 % landed on the tiger.
public class TigerPortalFx : MonoBehaviour
{
    [Header("Scene")]
    public SkinnedMeshRenderer lowRenderer, hdRenderer;
    public ParticleSystem charge, burst, shards, trail, shockwave;
    public Volume volume;                          // global, PortalFX layer, high priority (runtime profile copy)
    public Material shellLow, shellHd;             // EnergyShell instances (faceted / smooth)
    public AudioSource whoosh;                     // optional one-shot at takeoff

    [Header("Timing (pounce game time)")]
    public float slowScale = 0.28f;                // bullet-time speed
    public float slowIn = 0.10f, slowHold = 0.22f, slowOut = 0.20f;   // around the swap
    public float swapCoverage = 0.55f;             // the swap fires when the portal radius reaches this x the tiger's screen radius

    [Header("Camera")]
    public float pushInFov = 5f, punchFov = 16f, shakeDeg = 1.1f, rollDeg = 5f, arcDeg = 20f;

    [Header("Particles")]
    public int burstCount = 600, shardCount = 220;
    public float chargeRate = 120f, trailRate = 150f;
    public Vector2 chargeSpeed = new(0.05f, 0.3f), burstSpeed = new(0.5f, 2.2f), shardSpeed = new(0.8f, 2.2f), trailSpeed = new(0f, 0.12f);

    float baseTimeScale = 1f, baseFov = 60f, takeoffT, swapT = -1f, startTime, lastScale = -1f, lastFactor = 1f;
    bool active;
    Vector3 tigerFwd;
    MotionBlur blur; ChromaticAberration chroma; LensDistortion lens;
    Material[] lowMats, hdMats;
    float chargeAcc, trailAcc;
    // surface sampling: baked skinned pose per renderer (lists reused, rebaked at most once per frame)
    class Surface { public Mesh mesh; public readonly System.Collections.Generic.List<Vector3> v = new(), n = new(); public int[] tri; public int frame = -1; }
    readonly System.Collections.Generic.Dictionary<SkinnedMeshRenderer, Surface> surfaces = new();
    static readonly int IntensityId = Shader.PropertyToID("_Intensity"), ScanId = Shader.PropertyToID("_Scan"),
        ScanOriginId = Shader.PropertyToID("_ScanOrigin"), ScanDirId = Shader.PropertyToID("_ScanDir");

    public bool Swapped => swapT >= 0;
    public float Flash { get; private set; }

    void Awake()
    {
        if (volume)
        {
            var p = volume.profile;                                     // runtime copy: the asset stays untouched
            p.TryGet(out blur); p.TryGet(out chroma); p.TryGet(out lens);
            volume.weight = 0f;
        }
        if (shellLow) shellLow = new Material(shellLow);
        if (shellHd) shellHd = new Material(shellHd);
    }

    public void Begin(Camera cam, float takeoff, Vector3 forward)
    {
        active = true; takeoffT = takeoff; swapT = -1f; tigerFwd = forward; startTime = Time.time;
        baseTimeScale = Time.timeScale; baseFov = cam.fieldOfView; lastScale = -1f; lastFactor = 1f;
        if (volume) volume.weight = 1f;
        lowMats = AddShell(lowRenderer, shellLow); hdMats = AddShell(hdRenderer, shellHd);
        SetShell(shellLow, 0f, -1f); SetShell(shellHd, 0f, -1f);
        foreach (var ps in new[] { charge, burst, shards, trail, shockwave }) if (ps) ps.Play();   // emission is all from code
        chargeAcc = trailAcc = 0f;
        if (whoosh) whoosh.Play();
    }

    static Material[] AddShell(Renderer r, Material shell)
    {
        if (!r || !shell) return null;
        var orig = r.sharedMaterials;
        var m = new Material[orig.Length + 1]; orig.CopyTo(m, 0); m[orig.Length] = shell;
        r.sharedMaterials = m;
        return orig;
    }

    void SetShell(Material m, float intensity, float scan)
    {
        if (!m) return;
        m.SetFloat(IntensityId, intensity); m.SetFloat(ScanId, scan);
    }

    static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
    static float Pulse(float t0, float x, float decay) => x < t0 ? 0f : Mathf.Exp(-(x - t0) / decay) * (1f - Mathf.Exp(-(x - t0) / 0.02f));

    /// Per frame while the pounce/landing runs. portalRadius/center in pixels (the composite's values).
    public void Tick(Camera cam, float t, float portalRadius, Vector2 center)
    {
        if (!active) return;
        // ---- the swap: the rim has reached the tiger's outline
        if (swapT < 0 && portalRadius > 0 && portalRadius >= swapCoverage * TigerScreenRadius(cam)) Swap(t);

        // ---- time: bullet time around the swap (anchored at takeoff until the swap is known)
        float anchor = swapT >= 0 ? swapT : takeoffT + 0.1f;
        float s = Smooth(anchor - slowIn - 0.08f, anchor - 0.08f, t) * (1f - Smooth(anchor + slowHold, anchor + slowHold + slowOut, t));
        // someone else changed the time scale (pause menu, test harness): that becomes the new base
        if (lastScale >= 0f && !Mathf.Approximately(Time.timeScale, lastScale)) baseTimeScale = Time.timeScale / lastFactor;
        lastFactor = Mathf.Lerp(1f, slowScale, s);
        Time.timeScale = lastScale = baseTimeScale * lastFactor;

        // ---- post: motion blur on the takeoff burst and the exit rush, chromatic hit + lens bulge at the swap
        float takeoffHit = Pulse(takeoffT, t, 0.18f);
        float swapHit = swapT >= 0 ? Pulse(swapT, t, 0.28f) : 0f;
        float exitRush = swapT >= 0 ? Smooth(swapT + slowHold, swapT + slowHold + slowOut, t) * (1f - Smooth(swapT + 0.9f, swapT + 1.4f, t)) : 0f;
        // none during bullet time: the HD tiger must read sharp while it appears
        if (blur) blur.intensity.value = Mathf.Clamp01((0.55f * takeoffHit + 0.4f * exitRush) * (1f - s));
        if (chroma) chroma.intensity.value = Mathf.Clamp01(0.1f * s + 0.5f * swapHit + 0.2f * takeoffHit);
        if (lens) { lens.intensity.value = -0.22f * takeoffHit + 0.15f * swapHit; lens.scale.value = 1f + 0.04f * takeoffHit; }
        Flash = 0.45f * swapHit;

        // ---- shells: low-poly charges up during the crouch, HD materializes after the swap
        float chargeUp = Smooth(0f, takeoffT, t) * (swapT >= 0 ? 1f - Smooth(swapT, swapT + 0.25f, t) : 1f);
        SetShell(shellLow, 0.55f * chargeUp, -1f);
        if (swapT >= 0 && hdRenderer && shellHd)
        {
            var b = hdRenderer.bounds;
            float len = Vector3.Dot(b.extents, new Vector3(Mathf.Abs(tigerFwd.x), Mathf.Abs(tigerFwd.y), Mathf.Abs(tigerFwd.z))) * 2f;
            shellHd.SetVector(ScanOriginId, b.center - tigerFwd * len * 0.5f);           // tail end
            shellHd.SetVector(ScanDirId, tigerFwd);
            float u = Smooth(swapT, swapT + 0.75f, t);
            SetShell(shellHd, 0.5f * (1f - Smooth(swapT + 0.25f, swapT + 0.9f, t)), Mathf.Lerp(len * 1.1f, -0.1f, u));   // head -> tail
        }

        // ---- particles
        float dt = Time.deltaTime;
        if (charge && swapT < 0) { chargeAcc += chargeRate * Smooth(0f, takeoffT, t) * dt; int k = (int)chargeAcc; chargeAcc -= k; EmitFrom(charge, lowRenderer, k, chargeSpeed); }
        if (trail && t > takeoffT && (swapT < 0 || t < swapT + 0.8f))
        {
            trailAcc += trailRate * dt; int k = (int)trailAcc; trailAcc -= k;
            EmitFrom(trail, swapT >= 0 ? hdRenderer : lowRenderer, k, trailSpeed);
        }
    }

    Surface Bake(SkinnedMeshRenderer r)
    {
        if (!surfaces.TryGetValue(r, out var s)) surfaces[r] = s = new Surface { mesh = new Mesh { name = r.name + "_FxBake" } };
        if (s.frame == Time.frameCount) return s;
        r.BakeMesh(s.mesh);                                                  // skinned pose in the renderer's frame at scale 1
        s.mesh.GetVertices(s.v); s.mesh.GetNormals(s.n);
        if (s.tri == null) s.tri = s.mesh.triangles;
        s.frame = Time.frameCount;
        return s;
    }

    /// count particles at random points of the renderer's current skinned surface, flying off along the normal
    public void EmitFrom(ParticleSystem ps, SkinnedMeshRenderer r, int count, Vector2 speed)
    {
        if (!ps || !r || count <= 0) return;
        var s = Bake(r);
        // BakeMesh(useScale: false) = world-sized vertices in the renderer's rotated frame: position + rotation only
        // (measured with unity/fx_probe.cs: the full matrix put them 130x too far, useScale: true 130x too close)
        var m = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
        var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        int tris = s.tri.Length / 3;
        for (int i = 0; i < count; i++)
        {
            int t3 = Random.Range(0, tris) * 3;
            float a = Random.value, b = Random.value; if (a + b > 1) { a = 1 - a; b = 1 - b; }
            int i0 = s.tri[t3], i1 = s.tri[t3 + 1], i2 = s.tri[t3 + 2];
            var p = s.v[i0] + a * (s.v[i1] - s.v[i0]) + b * (s.v[i2] - s.v[i0]);
            var n = m.MultiplyVector((s.n[i0] + s.n[i1] + s.n[i2]).normalized).normalized;
            ep.position = m.MultiplyPoint3x4(p) + n * 0.01f;
            ep.velocity = n * Random.Range(speed.x, speed.y);
            ps.Emit(ep, 1);
        }
    }

    void Swap(float t)
    {
        swapT = t;
        EmitFrom(burst, hdRenderer, burstCount, burstSpeed);
        EmitFrom(shards, lowRenderer, shardCount, shardSpeed);
        if (shockwave && lowRenderer) shockwave.Emit(new ParticleSystem.EmitParams { position = lowRenderer.bounds.center, applyShapeToPosition = false }, 1);
    }

    float TigerScreenRadius(Camera cam)
    {
        var b = lowRenderer ? lowRenderer.bounds : new Bounds(transform.position, Vector3.one);
        var c = cam.WorldToScreenPoint(b.center);
        float r = 0f;
        for (int i = 0; i < 8; i++)
        {
            var p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var sp = cam.WorldToScreenPoint(p); if (sp.z <= 0) continue;
            r = Mathf.Max(r, Vector2.Distance(sp, c));
        }
        return r * 0.75f;                                                // bounds corners overshoot the silhouette
    }

    /// Camera offsets on top of the glide pose (call after TigerEncounter set the pose this frame).
    public void ApplyCamera(Camera cam, Vector3 center, float t)
    {
        if (!active) return;
        float crouch = Smooth(0f, takeoffT, t) * (1f - Smooth(takeoffT, takeoffT + 0.08f, t));
        float punch = Pulse(takeoffT, t, swapT >= 0 ? 0.35f : 0.5f);
        float settle = swapT >= 0 ? 1f - Smooth(swapT + 0.3f, swapT + 1.2f, t) : 1f;
        cam.fieldOfView = baseFov - pushInFov * crouch + punchFov * punch * settle;
        // bullet-time arc around the tiger + dutch roll, both gone by the time the camera settles
        float arc = (swapT >= 0 ? Smooth(swapT - 0.12f, swapT + slowHold, t) : Smooth(takeoffT, takeoffT + 0.3f, t) * 0.3f) * settle;
        var pos = cam.transform.position; var rot = cam.transform.rotation;
        var q = Quaternion.AngleAxis(arcDeg * arc, Vector3.up);
        pos = center + q * (pos - center); rot = q * rot;
        // shake: decaying noise after takeoff and the swap
        float amp = shakeDeg * (Pulse(takeoffT, t, 0.3f) + (swapT >= 0 ? 0.7f * Pulse(swapT, t, 0.25f) : 0f));
        float n = (Time.time - startTime) * 22f;
        var shake = Quaternion.Euler((Mathf.PerlinNoise(n, 0.3f) - 0.5f) * 2f * amp, (Mathf.PerlinNoise(0.7f, n) - 0.5f) * 2f * amp,
                                     rollDeg * arc * Mathf.Sin(Mathf.PI * Mathf.Clamp01(arc)) + (Mathf.PerlinNoise(n, n) - 0.5f) * amp);
        cam.transform.SetPositionAndRotation(pos, rot * shake);
    }

    public void End(Camera cam)
    {
        if (!active) return;
        active = false;
        Time.timeScale = baseTimeScale;
        if (cam) cam.fieldOfView = baseFov;
        if (volume) volume.weight = 0f;
        if (lowRenderer && lowMats != null) lowRenderer.sharedMaterials = lowMats;
        if (hdRenderer && hdMats != null) hdRenderer.sharedMaterials = hdMats;
        foreach (var ps in new[] { charge, burst, shards, trail, shockwave }) if (ps) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Flash = 0f;
    }

    void OnDestroy() { if (active) Time.timeScale = baseTimeScale; }
}
