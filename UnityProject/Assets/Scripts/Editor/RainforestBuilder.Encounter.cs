using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Random = System.Random;

/// Tiger encounter wiring (runtime logic: Scripts/Runtime/Tiger/TigerEncounter): animator states, click collider,
/// the abstract white showcase room (inactive until the portal), the portal disc, the HD twin and the UI Toolkit HUD.
public static partial class RainforestBuilder
{
    const string HudUxml = ProjectPaths.UI + "/Tiger/TigerHud.uxml";
    const string PanelSettingsPath = ProjectPaths.UISettings + "/PS_Hud.asset";
    const string ThemePath = ProjectPaths.UISettings + "/HudTheme.tss";

    static Material VfxMat(string name, string shader)
    {
        var path = $"{ProjectPaths.VFXMaterials}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find(shader);
        return m;
    }

    static void EnsureTigerStates(AnimatorController ac, string fbx)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToDictionary(c => c.name);
        var sm = ac.layers[0].stateMachine;
        foreach (var n in new[] { "Idle", "Walk", "Run", "Pounce", "Land" })
        {
            if (!clips.TryGetValue(n, out var clip)) continue;
            var st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == n) ?? sm.AddState(n);
            st.motion = clip; st.writeDefaultValues = true;
            if (n == "Idle") sm.defaultState = st;
            // "Walk" = root-motion blend of WalkTurnL / Walk / WalkTurnR by "Turn" (-1 left .. +1 right)
            if (n == "Walk" && clips.TryGetValue("WalkTurnL", out var tl) && clips.TryGetValue("WalkTurnR", out var tr))
            {
                if (!ac.parameters.Any(p => p.name == "Turn")) ac.AddParameter("Turn", AnimatorControllerParameterType.Float);
                var bt = st.motion as BlendTree;
                if (!bt || bt.name != "WalkBlend") { bt = new BlendTree { name = "WalkBlend" }; AssetDatabase.AddObjectToAsset(bt, ac); }
                bt.blendType = BlendTreeType.Simple1D; bt.blendParameter = "Turn"; bt.useAutomaticThresholds = false;
                bt.children = new ChildMotion[0];
                bt.AddChild(tl, -1f); bt.AddChild(clip, 0f); bt.AddChild(tr, 1f);
                st.motion = bt;
                // playback of this state only (AnimalWander.speedParam): a global Animator.speed also changed the Idle
                // being faded in, so the crossfade into Idle sped up / slowed down
                if (!ac.parameters.Any(p => p.name == "WalkSpeed")) ac.AddParameter(new AnimatorControllerParameter { name = "WalkSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
                st.speedParameter = "WalkSpeed"; st.speedParameterActive = true;
            }
        }
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(ac)).OfType<BlendTree>())
            if (sub.name == "WalkBlend" && !sm.states.Any(s => s.state.motion == sub)) Object.DestroyImmediate(sub, true);   // stale copies
        EditorUtility.SetDirty(ac);
    }

    static void BuildEncounter(Transform root, Camera cam, Light sun, List<string> log)
    {
        var tiger = root.Find("Wildlife")?.Cast<Transform>().FirstOrDefault(t => t.name.Contains("WhiteTiger"));
        if (!tiger) { log.Add("WARN no tiger for the encounter"); return; }
        var anim = tiger.GetComponentInChildren<Animator>();
        // the HD file's takes = the low-poly takes + curves for its own ear bones (earHD_L/R), so they drive both meshes
        EnsureTigerStates(anim.runtimeAnimatorController as AnimatorController, ProjectPaths.Find("SK_WhiteTigerHD.fbx") ?? ProjectPaths.Find("SK_WhiteTiger.fbx"));

        // click target: a box around the body (local space of the tiger root)
        var smr = tiger.GetComponentInChildren<SkinnedMeshRenderer>();
        var b = smr.bounds;
        var box = tiger.gameObject.AddComponent<BoxCollider>();
        box.center = tiger.InverseTransformPoint(b.center);
        box.size = new Vector3(0.62f, 1.15f, 2.15f);

        // ---- white room (inactive): floor disc, floating shapes, its own post volume
        var room = new GameObject("WhiteRoom").transform; room.SetParent(root, false);
        float floorY = cam.transform.position.y - EyeHeight;
        var floorMesh = SaveMesh("SM_Gen_WhiteFloor", m =>
        {
            int seg = 96; float R = 120f;
            var v = new List<Vector3> { Vector3.zero }; var t = new List<int>();
            for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2 / seg; v.Add(new Vector3(Mathf.Cos(a) * R, 0, Mathf.Sin(a) * R)); }
            for (int i = 1; i <= seg; i++) { t.Add(0); t.Add(i + 1); t.Add(i); }
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        });
        var floor = new GameObject("Floor", typeof(MeshFilter), typeof(MeshRenderer));
        floor.transform.SetParent(room, false); floor.transform.position = new Vector3(cam.transform.position.x, floorY, cam.transform.position.z);
        floor.GetComponent<MeshFilter>().sharedMesh = floorMesh;
        var fr = floor.GetComponent<MeshRenderer>(); fr.sharedMaterial = VfxMat("M_WhiteVoidFloor", "TerraRemastered/WhiteVoidFloor");
        fr.shadowCastingMode = ShadowCastingMode.Off; fr.lightProbeUsage = LightProbeUsage.Off;

        var shapeMat = LitMat(ProjectPaths.VFXMaterials, "M_WhiteShape", new Color(0.95f, 0.96f, 0.97f), smooth: 0.35f);
        var rnd = new Random(77); int shapes = 0;
        var ico = SaveMesh("SM_Gen_WhiteIco", m =>
        {
            var phi = (1 + Mathf.Sqrt(5)) / 2;
            var p = new[] { new Vector3(-1, phi, 0), new Vector3(1, phi, 0), new Vector3(-1, -phi, 0), new Vector3(1, -phi, 0), new Vector3(0, -1, phi), new Vector3(0, 1, phi),
                            new Vector3(0, -1, -phi), new Vector3(0, 1, -phi), new Vector3(phi, 0, -1), new Vector3(phi, 0, 1), new Vector3(-phi, 0, -1), new Vector3(-phi, 0, 1) };
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < f.Length; i++) { v.Add(p[f[i]].normalized * 0.5f); t.Add(i); }   // unshared verts: flat facets
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        });
        for (int i = 0; i < 26; i++)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI * 2, r = 7f + (float)rnd.NextDouble() * 22f;
            var go = new GameObject("Shape_" + i, typeof(MeshFilter), typeof(MeshRenderer), typeof(WhiteRoomDrift));
            go.transform.SetParent(room, false);
            float s = 0.3f + (float)rnd.NextDouble() * (r * 0.09f);
            go.transform.position = new Vector3(Mathf.Cos(a) * r, floorY + 0.6f + (float)rnd.NextDouble() * 5.5f, Mathf.Sin(a) * r);
            go.transform.rotation = Quaternion.Euler(rnd.Next(360), rnd.Next(360), rnd.Next(360));
            go.transform.localScale = new Vector3(s, s * (rnd.NextDouble() < 0.3 ? 2.2f : 1f), s);
            go.GetComponent<MeshFilter>().sharedMesh = ico;
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = shapeMat; mr.lightProbeUsage = LightProbeUsage.Off;
            var d = go.GetComponent<WhiteRoomDrift>(); d.bob = 0.1f + s * 0.15f; d.spin = 3f + rnd.Next(8); d.axis = Random3(rnd);
            shapes++;
        }
        // thin halo ring on the floor around the tiger's landing spot
        var ringMesh = SaveMesh("SM_Gen_WhiteRing", m =>
        {
            int seg = 128; var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2 / seg; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); v.Add(dir * 2.6f); v.Add(dir * 2.64f); }
            for (int i = 0; i < seg; i++) { int k = i * 2; t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        });
        var fwd = cam.transform.forward; fwd.y = 0; fwd.Normalize();
        var ring = new GameObject("LandingRing", typeof(MeshFilter), typeof(MeshRenderer));
        ring.transform.SetParent(room, false); ring.transform.position = new Vector3(cam.transform.position.x, floorY + 0.004f, cam.transform.position.z) + fwd * 3.4f;
        ring.GetComponent<MeshFilter>().sharedMesh = ringMesh;
        var ringMat = LitMat(ProjectPaths.VFXMaterials, "M_WhiteRing", new Color(0.40f, 0.85f, 0.92f), smooth: 0.1f);
        ring.GetComponent<MeshRenderer>().sharedMaterial = ringMat; ring.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // post for the white room: slightly lower exposure so the whites keep detail, cooler and less saturated
        const string ppPath = "Assets/Settings/PostProcessing/PP_WhiteRoom.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ppPath);
        if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, ppPath); }
        T Get<T>() where T : VolumeComponent { if (!profile.TryGet<T>(out var c)) { c = profile.Add<T>(true); AssetDatabase.AddObjectToAsset(c, profile); } c.active = true; return c; }
        Get<Tonemapping>().mode.Override(TonemappingMode.Neutral);
        // photographic: neutral-warm (a cool grade turned the cream coat blue-grey), shallow depth of field (focus set per
        // frame by TigerEncounter), fine grain
        var ca = Get<ColorAdjustments>(); ca.postExposure.Override(-0.1f); ca.saturation.Override(0f); ca.contrast.Override(10f);
        var wb = Get<WhiteBalance>(); wb.temperature.Override(4f);
        var dof = Get<DepthOfField>(); dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focusDistance.Override(3f);
        dof.focalLength.Override(40f); dof.aperture.Override(10f);   // subtle (50 mm f/5.6 blurred the body out) dof.bladeCount.Override(7);
        var grain = Get<FilmGrain>(); grain.type.Override(FilmGrainLookup.Thin1); grain.intensity.Override(0.08f); grain.response.Override(0.8f);
        var bl = Get<Bloom>(); bl.intensity.Override(0.25f); bl.threshold.Override(1.2f);
        var vg = Get<Vignette>(); vg.intensity.Override(0.2f); vg.color.Override(new Color(0.12f, 0.11f, 0.1f));   // neutral (a teal tint coloured the tiger's legs)
        EditorUtility.SetDirty(profile);
        var vol = new GameObject("WhiteRoomVolume", typeof(Volume)); vol.transform.SetParent(room, false);
        var volume = vol.GetComponent<Volume>(); volume.isGlobal = true; volume.priority = 10; volume.sharedProfile = profile;
        // studio reflections for the HD tiger only: a probe far below the world that only renderers anchored there use
        var probeGo = new GameObject("StudioProbe", typeof(ReflectionProbe)); probeGo.transform.SetParent(room, false);
        probeGo.transform.position = new Vector3(0, -500, 0);
        var probe = probeGo.GetComponent<ReflectionProbe>(); probe.mode = ReflectionProbeMode.Custom; probe.customBakedTexture = StudioCubemap();
        probe.size = new Vector3(10, 10, 10); probe.importance = 100; probe.blendDistance = 0; probe.boxProjection = false; probe.intensity = 1f;
        room.gameObject.SetActive(false);
        EnsureLayer(WhiteLayer, "WhiteRoom"); EnsureLayer(TigerLayer, "Tiger");

        // ---- HUD (UI Toolkit)
        var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (!ps) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, PanelSettingsPath); }
        ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        ps.scaleMode = PanelScaleMode.ScaleWithScreenSize; ps.referenceResolution = new Vector2Int(1920, 1080); ps.match = 0.5f;
        ps.clearColor = false; ps.sortingOrder = 10;
        EditorUtility.SetDirty(ps);
        var hudGo = new GameObject("TigerHud", typeof(UIDocument), typeof(TigerHud));
        var doc = hudGo.GetComponent<UIDocument>(); doc.panelSettings = ps; doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudUxml);

        // ---- encounter component
        var enc = tiger.gameObject.AddComponent<TigerEncounter>();
        enc.cam = cam; enc.look = cam.GetComponent<FixedLookCamera>(); enc.forestRoot = root; enc.whiteRoom = room.gameObject;
        enc.whiteVolume = volume; enc.sun = sun; enc.hud = hudGo.GetComponent<TigerHud>();
        enc.whiteLayer = WhiteLayer; enc.tigerLayer = TigerLayer; enc.whiteFloorY = floorY;
        enc.portalCompositeShader = Shader.Find("Hidden/TerraRemastered/PortalComposite");   // serialized: kept in builds
        enc.markerMaterial = VfxMat("M_Hotspot", "TerraRemastered/HotspotMarker"); enc.clickCollider = box;
        enc.keepActive = new[] { "Wildlife", "Sun", "WhiteRoom", "Global Volume" };
        // the white room renders only through the portal camera until the swap; the tiger is seen by both cameras
        SetLayer(room.gameObject, WhiteLayer); SetLayer(tiger.gameObject, TigerLayer);
        // HD twin: only the portal camera sees it, so the tiger turns HD exactly where the portal covers it
        var hd = AttachHdTiger(smr, log);
        if (hd) { hd.probeAnchor = room.Find("StudioProbe"); hd.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes; }
        if (hd) { EnsureLayer(TigerHDLayer, "TigerHD"); SetLayer(hd.gameObject, TigerHDLayer); }
        enc.lowRenderer = smr; enc.hdRenderer = hd; enc.hdLayer = TigerHDLayer;
        cam.cullingMask &= ~((1 << WhiteLayer) | (1 << TigerHDLayer));
        EnsureLayer(PortalFxLayer, "PortalFX");
        enc.fx = BuildPortalFx(tiger, smr, hd, log); enc.fxLayer = PortalFxLayer;
        // the tiger paces a loop in front of the viewer (arc walks: the body turns and curves, feet stay planted)
        enc.wander = AddTigerLoop(tiger, cam.transform, log);
        var cd = cam.GetComponent<UniversalAdditionalCameraData>(); if (cd) cd.volumeLayerMask = 1 | (1 << PortalFxLayer);   // jungle post + transition FX
        log.Add($"encounter: white room ({shapes} shapes, floor y {floorY:F2}), portal, HUD, {TigerFacts.Points.Length} hotspots");
    }

    const int WhiteLayer = 8, TigerLayer = 9, TigerHDLayer = 10, PortalFxLayer = 11;
    // The tiger's pacing loop in front of the viewer (world x, z), all on the flat jungle path (y -0.05..-0.17): down its
    // right edge toward the viewer (x -0.22, the foot of the bank), a stop facing him at ~5 m, a U-turn at ~4.2 m, back
    // up the left edge (x -1.46, clear of the hero leaves' tips), a U-turn at ~9 m. U-turn radius 0.62 m: tighter than the turn clips' arc (0.98 m at
    // this scale), possible because QuadrupedLocomotion locks the planted paws (AnimalWander.turnBoost). Kept off: the
    // bank right of the path (x 0.1-0.8, 27-56 deg: walking along or across it left legs 10-20 cm short of the ground),
    // the boulder (box x 0.8-2.2, z 3.7-5.3), the hero leaves / heliconias left (x < -1.8): unity/ground_grid.cs,
    // unity/footprint.cs, unity/near_objects.cs
    // (2026-10-18: left side -1.46 -> -1.30 and the far U-turn 0.2 m nearer: in that turn the paws reached the root
    //  flare of the buttress tree at (-8.6, 11) at x -1.99, recorded body audit)
    static readonly Vector2[] TigerLoop = { new(-0.22f, 8.2f), new(-0.22f, 6.6f), new(-0.22f, 4.8f), new(-0.4f, 4.33f), new(-0.84f, 4.15f),
                                            new(-1.2f, 4.33f), new(-1.30f, 4.8f), new(-1.30f, 6.6f), new(-1.30f, 8.2f), new(-1.15f, 8.62f),
                                            new(-0.78f, 8.8f), new(-0.4f, 8.62f) };
    static readonly Vector2 TigerStop = new(-0.25f, 5.0f);   // heading toward the viewer here
    static readonly string[] ClearOnLoop = { "Grass", "Fern", "Cover", "Litter", "Twig", "Pebble", "Mushroom", "Seedling" };

    static AnimalWander AddTigerLoop(Transform tiger, Transform viewer, List<string> log)
    {
        // closed Catmull-Rom through the control points, resampled every 0.35 m, on the ground
        var pts = new List<Vector3>(); int n = TigerLoop.Length;
        for (int i = 0; i < n; i++)
        {
            Vector2 p0 = TigerLoop[(i - 1 + n) % n], p1 = TigerLoop[i], p2 = TigerLoop[(i + 1) % n], p3 = TigerLoop[(i + 2) % n];
            int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 0.35f));
            for (int k = 0; k < steps; k++)
            {
                float t = k / (float)steps, t2 = t * t, t3 = t2 * t;
                var q = 0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
                OnTerrain(q.x, q.y, out var hit); pts.Add(new Vector3(q.x, hit.point.y, q.y));
            }
        }
        // clear small plants / litter on the loop (the big composition pieces stay)
        int cleared = 0; var root = tiger.parent.parent;
        foreach (var g in new[] { "Jungle", "Floor", "PathDebris", "Hero" })
        {
            var grp = root.Find(g); if (!grp) continue;
            foreach (var c in grp.Cast<Transform>().ToList())
            {
                if (!ClearOnLoop.Any(c.name.Contains)) continue;
                var p = new Vector2(c.position.x, c.position.z);
                float d = float.MaxValue;
                for (int i = 0; i < pts.Count; i++) d = Mathf.Min(d, SegDist(p, new Vector2(pts[i].x, pts[i].z), new Vector2(pts[(i + 1) % pts.Count].x, pts[(i + 1) % pts.Count].z), out _));
                if (d < 0.7f) { Object.DestroyImmediate(c.gameObject); cleared++; }
            }
        }
        ResolveClearance(root, pts, log);                     // nothing in the tiger's way, no plants growing through each other
        var w = tiger.GetComponent<AnimalWander>() ?? tiger.gameObject.AddComponent<AnimalWander>();
        w.route = pts.ToArray(); w.loop = true; w.patrol = false; w.viewer = viewer;
        float best = float.MaxValue; int bi = 0;
        for (int i = 0; i < pts.Count; i++) { float d = Vector2.Distance(new Vector2(pts[i].x, pts[i].z), TigerStop); if (d < best) { best = d; bi = i; } }
        float total = 0, at = 0; for (int i = 0; i < pts.Count; i++) { if (i == bi) at = total; total += Vector3.Distance(pts[i], pts[(i + 1) % pts.Count]); }
        w.stops = new[] { at / total }; w.nearIdle = new Vector2(4f, 7f);
        w.arcClips = true; w.turnBoost = 1.6f; w.lookAhead = 0.8f; w.walkNoSlideSpeed = 0.45f; w.clipTurnRate = 34.4f; w.walkSpeed = 0.55f; w.turnRadius = 0.75f;   // build.py: WALK_SPEED, WALK_SPEED / WALK_TURN_RADIUS
        w.lookChanceWalk = 0.55f; w.lookChanceIdle = 0.9f; w.idleTime = new Vector2(2f, 4f);
        // start ON the loop, facing along it (from the composed spot it first walked a 1 m detour off the path)
        int si = 0; float sd = float.MaxValue;
        for (int i = 0; i < pts.Count; i++) { float d = Vector2.Distance(new Vector2(pts[i].x, pts[i].z), new Vector2(tiger.position.x, tiger.position.z)); if (d < sd) { sd = d; si = i; } }
        var dir = pts[(si + 1) % pts.Count] - pts[si]; dir.y = 0;
        tiger.SetPositionAndRotation(pts[si], Quaternion.LookRotation(dir.normalized));
        AddLocomotion(tiger, log);
        log.Add($"{tiger.name}: pacing loop {pts.Count} pts, {total:F1} m, stop at u {at / total:F2}, cleared {cleared} plants");
        return w;
    }
    /// Per-joint layer over the tiger's clips: locked paws, leg IK, body on the terrain, tail lag (QuadrupedLocomotion).
    /// Gait table = build.py WALK_LEGS (touchdown, lift-off).
    // gait table from a lateral-sequence walk (quadruped.py LEG_ORDER): touchdown = the leg's phase offset, lift-off after
    // the duty factor (the tiger's clips use 0.65 too)
    static void AddLocomotion(Transform tiger, List<string> log, float duty = 0.65f)
    {
        var lo = tiger.GetComponentInChildren<SkinnedMeshRenderer>();                // the low-poly skin: its bones drive every mesh
        var bones = lo.bones.Where(t => t).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        Transform B(string n) => bones.TryGetValue(n, out var t) ? t : null;
        var q = tiger.GetComponent<QuadrupedLocomotion>() ?? tiger.gameObject.AddComponent<QuadrupedLocomotion>();
        var gait = new (string leg, float td, float lo)[] { ("BL", 0f, duty), ("FL", 0.25f, 0.25f + duty), ("BR", 0.50f, 0.5f + duty), ("FR", 0.75f, 0.75f + duty) };
        q.legs = gait.Select(g => new QuadrupedLocomotion.Leg
        {
            name = g.leg, upper = B($"upper_{g.leg}"), lower = B($"lower_{g.leg}"), foot = B($"foot_{g.leg}"), toe = B($"toe_{g.leg}"),
            touchdown = g.td, liftoff = g.lo,
        }).ToArray();
        q.hips = B("hips"); q.head = B("head");
        q.tail = Enumerable.Range(1, 6).Select(i => B($"tail{i}")).Where(t => t).ToArray();
        OnTerrain(tiger.position.x, tiger.position.z, out _); q.ground = terrainCol;               // Terrain_Mesh (plants are not ground)
        var wd = tiger.GetComponent<AnimalWander>(); if (wd) wd.ground = terrainCol;
        q.walkState = "Walk";
        // the layer edits the pose on top of the Animator's output every frame: a culled Animator stops rewriting it and
        // the edits pile up (the animals behind the viewer sank metres into the ground while off-screen)
        var an = tiger.GetComponentInChildren<Animator>(); if (an) an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // involuntary motion (ear flicks, tail swish) and footfalls; a cat treads softly, the tapir is heavy
        var life = tiger.GetComponent<AnimalIdleLife>() ?? tiger.gameObject.AddComponent<AnimalIdleLife>();
        life.head = q.head; life.wander = wd; life.tail = q.tail;
        Transform F(string n) => tiger.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);   // earHD_*: HD-only bones
        life.earsL = new[] { F("ear_L"), F("earHD_L") }.Where(t => t).ToArray(); life.earsR = new[] { F("ear_R"), F("earHD_R") }.Where(t => t).ToArray();
        var steps = tiger.GetComponent<Footsteps>() ?? tiger.gameObject.AddComponent<Footsteps>(); steps.loco = q;
        (steps.volume, steps.pitch) = tiger.name.Contains("Tiger") ? (0.15f, 0.85f) : tiger.name.Contains("Tapir") ? (0.45f, 0.7f) : (0.3f, 1.25f);
        int missing = q.legs.Sum(l => (l.upper ? 0 : 1) + (l.lower ? 0 : 1) + (l.foot ? 0 : 1) + (l.toe ? 0 : 1));
        log.Add($"{tiger.name}: locomotion layer ({q.legs.Length} legs, {q.tail.Length} tail joints, ground {(q.ground ? q.ground.name : "NONE")}, missing bones {missing}), ears {life.earsL.Length}+{life.earsR.Length}, footsteps");
    }
    const float WanderShiftX = 0.35f;                         // route shifted off the path centre, away from the buttress trunk

    /// AnimalWander on an animal: route = the path centreline between z0 and z1 (resampled every 0.5 m, on the ground)
    static AnimalWander AddWander(GameObject animal, Transform viewer, Vector2 zRange, List<string> log, float shiftX = WanderShiftX, float lateral = 0.35f)
    {
        var route = new List<Vector3>();
        for (float z = zRange.x; z <= zRange.y + 1e-3f; z += 0.5f)
        {
            float x = PathXAt(z) + shiftX; OnTerrain(x, z, out var hit);
            route.Add(new Vector3(x, hit.point.y, z));
        }
        var w = animal.GetComponent<AnimalWander>() ?? animal.AddComponent<AnimalWander>();
        w.route = route.ToArray(); w.viewer = viewer; w.lateral = lateral;
        log.Add($"{animal.name}: wander route {route.Count} pts, z {zRange.x}-{zRange.y}");
        return w;
    }

    static float PathXAt(float z)
    {
        for (int i = 1; i < PathPts.Length; i++)
            if (z >= PathPts[i - 1].y && z <= PathPts[i].y)
                return Mathf.Lerp(PathPts[i - 1].x, PathPts[i].x, Mathf.InverseLerp(PathPts[i - 1].y, PathPts[i].y, z));
        return PathPts[^1].x;
    }

    /// SK_WhiteTigerHD's skinned mesh, re-bound to the low-poly tiger's bones: same rig and rest pose (bind poses are
    /// identical, see unity/tiger_hd_check.cs), so one Animator drives both meshes and they cannot drift apart.
    static SkinnedMeshRenderer AttachHdTiger(SkinnedMeshRenderer lo, List<string> log)
    {
        var path = ProjectPaths.Find("SK_WhiteTigerHD.fbx");
        if (path == null) { log.Add("WARN no SK_WhiteTigerHD: the portal shows the low-poly tiger"); return null; }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var srcs = model.GetComponentsInChildren<SkinnedMeshRenderer>();
        var srcSkin = srcs.FirstOrDefault(r => r.name == "SK_WhiteTigerHD") ?? srcs[0];
        var srcFur = srcs.FirstOrDefault(r => r.name == "SK_WhiteTigerHD_Fur");
        var bones = lo.bones.ToDictionary(b => b.name);
        // bones only the HD rig has (earHD_L/R: its ears sit ~5 cm behind the low-poly ones): same local pose under the
        // same parent bone of the shared skeleton
        foreach (var b in srcSkin.bones)
        {
            if (bones.ContainsKey(b.name)) continue;
            if (!bones.TryGetValue(b.parent.name, out var parent)) { log.Add($"WARN HD bone {b.name}: parent {b.parent.name} not in the low-poly rig"); continue; }
            var extra = parent.Find(b.name) ?? new GameObject(b.name).transform;
            extra.SetParent(parent, false); extra.localPosition = b.localPosition; extra.localRotation = b.localRotation; extra.localScale = b.localScale;
            bones[b.name] = extra;
        }
        SkinnedMeshRenderer Bind(SkinnedMeshRenderer src)
        {
            var go = new GameObject(src.name, typeof(SkinnedMeshRenderer));
            go.transform.SetParent(lo.transform.parent, false);      // sibling of the low-poly mesh under the model root
            go.transform.localPosition = src.transform.localPosition; go.transform.localRotation = src.transform.localRotation;
            go.transform.localScale = src.transform.localScale;
            var r = go.GetComponent<SkinnedMeshRenderer>();
            r.sharedMesh = src.sharedMesh; r.sharedMaterials = src.sharedMaterials;
            r.bones = src.bones.Select(b => bones[b.name]).ToArray();
            r.rootBone = bones[src.rootBone.name]; r.localBounds = src.localBounds;
            r.quality = SkinQuality.Bone4; r.updateWhenOffscreen = false;
            r.shadowCastingMode = ShadowCastingMode.Off;             // TigerEncounter sets casting per camera (skin)
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            return r;
        }
        var smr = Bind(srcSkin);
        foreach (var m in smr.sharedMaterials)
        {   // skin under the shell fur: roots are shadowed (0.82)
            m.SetColor("_BaseColor", new Color(0.82f, 0.82f, 0.82f));
            // reflections on: only the wet eyes / nose are smooth (MS map), they catch the studio probe (StudioProbe)
            m.SetFloat("_EnvironmentReflections", 1); m.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(m);
        }
        // shell fur on the decimated fur mesh (22k tris instead of 89k per shell; fur data + baked AO in that mesh)
        var fur = VfxMat("M_WhiteTigerFur", "TerraRemastered/FurShell");
        fur.SetTexture("_BaseMap", smr.sharedMaterial.GetTexture("_BaseMap")); fur.SetFloat("_FurLength", 0.011f);     // a tiger coat is short and sleek (plush at 1.8 cm)
        fur.SetFloat("_Density", 520f); fur.SetFloat("_Comb", 2.4f); fur.SetFloat("_Thickness", 0.5f); fur.SetFloat("_Elongate", 3f);
        fur.SetFloat("_RootAO", 0.55f); fur.SetFloat("_TipLighten", 0f); fur.SetFloat("_Wrap", 0.3f); fur.SetFloat("_Translucency", 0.2f);
        fur.SetFloat("_ColorVar", 0.08f); fur.SetFloat("_Clump", 0.45f); fur.SetFloat("_ClumpSize", 2.5f); fur.SetFloat("_BakedAO", 0.85f);
        EditorUtility.SetDirty(fur);
        var furSmr = Bind(srcFur ?? srcSkin);
        furSmr.gameObject.layer = TigerHDLayer;
        var tf = furSmr.gameObject.AddComponent<TigerFur>(); tf.shell = fur;
        tf.lodLayers = new[] { 24, 16, 10 }; tf.lodDistance = new[] { 1.8f, 4.2f };   // fragment-bound: shells cover the silhouette
        log.Add($"tiger HD twin: skin {smr.sharedMesh.triangles.Length / 3} tris, fur mesh {furSmr.sharedMesh.triangles.Length / 3} tris x {string.Join("/", tf.lodLayers)} shells, {smr.bones.Length} bones ({lo.bones.Length} shared, {smr.bones.Length - lo.bones.Length} HD-only)");
        return smr;
    }

    /// Transition effects for the pounce (runtime: TigerPortalFx): energy shells, charge / burst / shard / trail particles
    /// emitting from the tiger skins, a global post volume (motion blur, chromatic aberration, lens distortion) and the
    /// whoosh. Everything on the PortalFX layer, which both the forest and the portal camera render.
    static TigerPortalFx BuildPortalFx(Transform tiger, SkinnedMeshRenderer lo, SkinnedMeshRenderer hd, List<string> log)
    {
        var root = new GameObject("PortalFX").transform; root.SetParent(tiger, false);
        var fx = root.gameObject.AddComponent<TigerPortalFx>();
        fx.lowRenderer = lo; fx.hdRenderer = hd ? hd : lo;
        var sparkle = VfxMat("M_PortalSparkle", "TerraRemastered/Sparkle"); sparkle.SetFloat("_Mode", 0); sparkle.SetFloat("_Intensity", 1.6f);
        var shardMat = VfxMat("M_PortalShard", "TerraRemastered/Sparkle"); shardMat.SetFloat("_Mode", 1); shardMat.SetFloat("_Intensity", 2.4f);
        var ringMat = VfxMat("M_PortalShockwave", "TerraRemastered/Sparkle"); ringMat.SetFloat("_Mode", 2); ringMat.SetFloat("_Intensity", 2.2f);
        fx.shellLow = VfxMat("M_EnergyShellLow", "TerraRemastered/EnergyShell"); fx.shellLow.SetFloat("_Faceted", 1); fx.shellLow.SetFloat("_Push", 0.004f);
        fx.shellHd = VfxMat("M_EnergyShellHD", "TerraRemastered/EnergyShell"); fx.shellHd.SetFloat("_Faceted", 0); fx.shellHd.SetFloat("_Push", 0.003f);
        foreach (var m in new[] { sparkle, shardMat, ringMat, fx.shellLow, fx.shellHd }) EditorUtility.SetDirty(m);
        var shard = SaveMesh("SM_Gen_Shard", m =>
        {   // one facet: an irregular triangle, uv corners for the edge glow
            m.SetVertices(new List<Vector3> { new(-0.5f, -0.4f, 0), new(0.55f, -0.3f, 0), new(-0.1f, 0.6f, 0) });
            m.SetUVs(0, new List<Vector2> { new(0, 0), new(1, 0), new(0, 1) });
            m.SetTriangles(new[] { 0, 2, 1 }, 0); m.RecalculateNormals(); m.RecalculateBounds();
        });
        var hdr = new Color(0.3f, 0.85f, 1f);                              // saturated: reads on the white room too
        Gradient Fade(Color a, Color b) => new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(a, 0.25f), new GradientColorKey(b, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) }
        };
        ParticleSystem Ps(string name, int max, Vector2 life, Vector2 speed, Vector2 size, float gravity, Material mat)
        {
            var go = new GameObject(name, typeof(ParticleSystem)); go.transform.SetParent(root, false);
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.loop = true; main.maxParticles = max;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y); main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y); main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startColor = hdr;
            var em = ps.emission; em.rateOverTime = 0;
            var sh = ps.shape; sh.enabled = false;              // TigerPortalFx emits from the skinned surface in code
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(hdr, new Color(0.1f, 0.5f, 1f));
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, 0.2f));
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
                                                                            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomX });
            return ps;
        }
        // charge: sparkles lifting off the low-poly skin while it crouches (rate driven by code)
        fx.charge = Ps("Charge", 1500, new Vector2(0.5f, 0.9f), new Vector2(0.05f, 0.3f), new Vector2(0.03f, 0.07f), -0.12f, sparkle);
        var noise = fx.charge.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 1.4f;
        // burst: the HD skin throws off a cloud of sparkles at the swap (Emit from code)
        fx.burst = Ps("Burst", 1500, new Vector2(0.6f, 1.4f), new Vector2(0.5f, 2.4f), new Vector2(0.04f, 0.12f), 0.05f, sparkle);
        var lv = fx.burst.limitVelocityOverLifetime; lv.enabled = true; lv.dampen = 0.12f; lv.limit = 0.4f;
        var bn = fx.burst.noise; bn.enabled = true; bn.strength = 0.5f; bn.frequency = 2f;
        // shards: low-poly facets breaking off and spinning away (mesh particles)
        fx.shards = Ps("Shards", 400, new Vector2(0.5f, 1.0f), new Vector2(0.8f, 2.2f), new Vector2(0.12f, 0.24f), 0.35f, shardMat);
        var smain = fx.shards.main; smain.startRotation3D = true;
        smain.startRotationX = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2); smain.startRotationY = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2); smain.startRotationZ = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        var rot = fx.shards.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-6f, 6f); rot.y = new ParticleSystem.MinMaxCurve(-6f, 6f); rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        var sr = fx.shards.GetComponent<ParticleSystemRenderer>(); sr.renderMode = ParticleSystemRenderMode.Mesh; sr.mesh = shard; sr.alignment = ParticleSystemRenderSpace.Local;
        sr.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
        // shockwave: one ring growing from the tiger's centre at the swap
        fx.shockwave = Ps("Shockwave", 4, new Vector2(0.45f, 0.45f), Vector2.zero, new Vector2(4.5f, 4.5f), 0f, ringMat);
        var wsz = fx.shockwave.sizeOverLifetime; wsz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.08f, 0, 4f), new Keyframe(1, 1f, 0.3f, 0)));
        var wcol = fx.shockwave.colorOverLifetime;
        wcol.color = new Gradient { colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(hdr, 0.4f), new GradientColorKey(new Color(0.1f, 0.5f, 1f), 1) },
                                    alphaKeys = new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0, 1) } };
        // trail: sparkles left behind along the flight path
        fx.trail = Ps("Trail", 1200, new Vector2(0.35f, 0.8f), new Vector2(0f, 0.12f), new Vector2(0.015f, 0.04f), -0.05f, sparkle);

        // post volume (runtime copy of the profile is animated by TigerPortalFx)
        const string ppPath = "Assets/Settings/PostProcessing/PP_PortalFx.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ppPath);
        if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, ppPath); }
        T Get<T>() where T : VolumeComponent { if (!profile.TryGet<T>(out var c)) { c = profile.Add<T>(true); AssetDatabase.AddObjectToAsset(c, profile); } c.active = true; return c; }
        var mb = Get<MotionBlur>(); mb.mode.Override(MotionBlurMode.CameraAndObjects); mb.quality.Override(MotionBlurQuality.High); mb.intensity.Override(0f); mb.clamp.Override(0.12f);
        Get<ChromaticAberration>().intensity.Override(0f);
        var ld = Get<LensDistortion>(); ld.intensity.Override(0f); ld.scale.Override(1f);
        EditorUtility.SetDirty(profile);
        var vgo = new GameObject("PortalFxVolume", typeof(Volume)); vgo.transform.SetParent(root, false);
        fx.volume = vgo.GetComponent<Volume>(); fx.volume.isGlobal = true; fx.volume.priority = 50; fx.volume.sharedProfile = profile; fx.volume.weight = 0;

        // whoosh (2D one-shot, real time)
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/Audio/SFX/A_PortalWhoosh.wav");
        if (clip)
        {
            var a = root.gameObject.AddComponent<AudioSource>(); a.clip = clip; a.playOnAwake = false; a.spatialBlend = 0f; a.volume = 0.8f;
            fx.whoosh = a;
        }
        SetLayer(root.gameObject, PortalFxLayer);
        log.Add($"portal fx: 5 particle systems, shells, volume, whoosh {(clip ? "yes" : "MISSING")}");
        return fx;
    }

    /// HDR studio environment for reflections: soft overhead light, one big key softbox (front-left, high), light
    /// horizon, light floor. Gives the wet eyes a photographic catchlight.
    static Cubemap StudioCubemap()
    {
        var path = $"{ProjectPaths.VFXTextures}/T_StudioReflection.asset";
        ProjectPaths.EnsureFolder(ProjectPaths.VFXTextures);
        const int n = 128;
        var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        if (!cube) { cube = new Cubemap(n, TextureFormat.RGBAHalf, true); AssetDatabase.CreateAsset(cube, path); }
        var key = new Vector3(-0.55f, 0.55f, 0.63f).normalized;
        Vector3 Dir(CubemapFace f, float u, float v)
        {
            u = 2 * u - 1; v = 2 * v - 1;
            return (f switch
            {
                CubemapFace.PositiveX => new Vector3(1, -v, -u), CubemapFace.NegativeX => new Vector3(-1, -v, u),
                CubemapFace.PositiveY => new Vector3(u, 1, v), CubemapFace.NegativeY => new Vector3(u, -1, -v),
                CubemapFace.PositiveZ => new Vector3(u, -v, 1), _ => new Vector3(-u, -v, -1)
            }).normalized;
        }
        var px = new Color[n * n];
        foreach (CubemapFace f in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var d = Dir(f, (x + 0.5f) / n, (y + 0.5f) / n);
                    float sky = Mathf.Lerp(0.85f, 1.25f, Mathf.Clamp01(d.y));                 // brighter overhead
                    float floorV = d.y < 0 ? Mathf.Lerp(0.85f, 0.7f, Mathf.Clamp01(-d.y)) : sky;
                    float box = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Mathf.Cos(24 * Mathf.Deg2Rad), Mathf.Cos(18 * Mathf.Deg2Rad), Vector3.Dot(d, key))) * 3.5f;
                    float v = floorV + box;
                    px[y * n + x] = new Color(v, v * 0.99f, v * 0.97f, 1);
                }
            cube.SetPixels(px, f);
        }
        cube.Apply(true); EditorUtility.SetDirty(cube);
        return cube;
    }

    static void EnsureLayer(int index, string name)
    {
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        var el = layers.GetArrayElementAtIndex(index);
        if (el.stringValue != name) { el.stringValue = name; tm.ApplyModifiedPropertiesWithoutUndo(); }
    }

    static void SetLayer(GameObject go, int layer) { foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }

    static Vector3 Random3(Random r) => new Vector3((float)r.NextDouble() - 0.5f, (float)r.NextDouble() - 0.5f, (float)r.NextDouble() - 0.5f).normalized;
}
