using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Random = System.Random;

/// <summary>
/// Builds the low-poly rainforest vista (sources/reference/terra_rainforest.png) into Assets/Scenes/Rainforest.unity.
/// Fully re-runnable: deletes and regenerates the "Rainforest" root, the generated meshes and materials, from fixed seeds.
/// Viewer: fixed at the origin on the jungle path, looking +Z toward the lake (FixedLookCamera).
/// Layout (metres, x right, z forward): path winds from the viewer to the lake shore (z~50); stream with cascades on the
///   right (x 4..20); buttress trees, big leaves and heliconia on the left; palms right-centre; lake z 50..290;
///   forested hills behind; low-poly mountain ridge 1.3-3.4 km away with the main peak slightly right of centre.
/// Menu: Tools > Terra > Build Rainforest Scene.   CLI: return RainforestBuilder.Build();
/// </summary>
public static partial class RainforestBuilder
{
    const string ScenePath = "Assets/Scenes/Rainforest.unity";
    const string GenFolder = "Assets/Art/Environment/Terrain/Generated";
    const string MatFolder = "Assets/Art/Materials/Surfaces";
    const string WaterMatFolder = "Assets/Art/Materials/Water";
    const string FoliageMatFolder = "Assets/Art/Materials/Foliage";
    const string Q = "Assets/ThirdParty/Quaternius/UltimateNature/";
    const string K = "Assets/ThirdParty/Kenney/NatureKit/";
    const float EyeHeight = 1.65f;
    const float LakeLevel = -4.6f;

    // ---------------------------------------------------------------- layout curves
    static readonly Vector2[] PathPts =
    {
        new(1.5f, -45), new(0.4f, -30), new(-0.6f, -12), new(-1.0f, -4), new(-1.3f, 0), new(-1.7f, 5), new(-2.0f, 10), new(-1.6f, 16), new(-0.8f, 22),
        new(-0.2f, 28), new(0.2f, 34), new(0.0f, 40), new(-0.6f, 47), new(-1.0f, 54)
    };
    static readonly Vector2[] StreamPts =
    {
        new(34, 46), new(25, 37), new(17, 30), new(11, 24), new(7.2f, 18), new(5.0f, 12.5f), new(3.9f, 8), new(3.4f, 4.5f),
        new(3.8f, 1.0f), new(5.4f, -2.5f), new(8.5f, -6.5f), new(12, -12)
    };

    static List<Vector2> streamS;      // resampled centreline, upstream -> downstream, 0.3 m spacing
    static List<float> streamLevel;    // water surface height per sample (stepped pools)
    static List<int> cascades;         // sample index where a drop starts

    // ---------------------------------------------------------------- entry point
    public static bool QuickBuild;                       // unity/build_scene.cs "quick": skip the slow overlap pass while iterating
    [MenuItem("Tools/Terra/Build Rainforest Scene")]
    public static string Build()
    {
        var log = new List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        void T(string what) { log.Add($"  [{sw.Elapsed.TotalSeconds:F1}s] {what}"); }
        foreach (var f in new[] { GenFolder, MatFolder, WaterMatFolder, FoliageMatFolder, "Assets/Settings/PostProcessing", "Assets/Settings/Lighting" })
            ProjectPaths.EnsureFolder(f);
        ConfigureUrp(log);
        ConfigureRendererFeatures(log);

        var scene = System.IO.File.Exists(ScenePath)
            ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        foreach (var go in scene.GetRootGameObjects())
            if (go.name is "Rainforest" or "Main Camera" or "Sun" or "Directional Light" or "Global Volume" or "TigerHud") UnityEngine.Object.DestroyImmediate(go);

        var root = new GameObject("Rainforest").transform;
        Transform Group(string n) { var t = new GameObject(n).transform; t.SetParent(root, false); return t; }

        BuildStreamProfile();
        var mats = Materials();

        // terrain + water + mountains
        T("setup"); var terrain = BuildTerrain(Group("Terrain"), mats, log); T("terrain");
        BuildLake(Group("Lake"), log);
        BuildStream(Group("Stream"), log);
        BuildMountains(Group("Mountains"), mats, log);
        Physics.SyncTransforms(); T("water+mountains");

        // vegetation and rocks
        var rnd = new Random(1234);
        Place(Group("Hero"), Group("Jungle"), Group("StreamRocks"), Group("Forest"), Group("PathDebris"), rnd, log); T("placement");
        PlaceTiger(Group("Wildlife"), log);

        // sky, light, atmosphere, viewer
        BuildSky(Group("Sky"), log);
        BuildLightShafts(Group("LightShafts"));
        BuildMotes(Group("SunMotes"));
        BuildAudio(Group("Audio"), log);
        var sun = BuildLighting(root, log);
        BuildPost(root);
        var cam = BuildCamera();
        ConfigureBakedLighting(root, sun, log); T("baked-lighting setup (bake: unity/bake_lighting.cs)");
        BuildEncounter(root, cam, sun, log); T("encounter");
        PlaceAnimals(Group("Wildlife"), cam.transform, log); T("animals");
        AddAnimalEncounters(root, log); T("animal encounters");
        BuildLife(Group("Life"), root, cam.transform, log); T("life");
        BuildEpiphytes(Group("Epiphytes"), root, cam.transform.position, log); T("epiphytes");
        BuildWaterEdges(Group("Edges"), cam.transform.position, log); T("edges");
        if (QuickBuild) log.Add("WARN overlaps: skipped (quick build: plants may interpenetrate; run a full build before committing)");
        else { ResolveOverlaps(root, cam.transform.position, AnimalCorridors(root, log), log); T("overlaps"); }   // last: nothing grows through anything
        // fixed benchmark views for PerfBench (player builds with -perfbench): the viewer looking around
        var pv = Group("PerfViews");
        foreach (var (n, yaw, pitch) in new[] { ("front", 5f, 5f), ("left", -80f, 0f), ("right", 95f, 5f), ("back", 185f, 0f), ("up", -20f, -55f) })
        {
            var v = new GameObject(n).transform; v.SetParent(pv, false);
            v.SetPositionAndRotation(cam.transform.position, Quaternion.Euler(pitch, yaw, 0));
        }

        // no static batching anywhere: sway moves plant roots, and batching broke the big terrain mesh in Play Mode
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var f = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
            if ((f & StaticEditorFlags.BatchingStatic) != 0) GameObjectUtility.SetStaticEditorFlags(t.gameObject, f & ~StaticEditorFlags.BatchingStatic);
        }
        // ...except the forest-floor pieces on plain URP Lit (no wind): 2.3k small renderers cost ~1.8 ms CPU unbatched
        int batched = 0;
        foreach (var r in root.Find("Floor").GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.sharedMaterials.Any(m => m && m.shader.name == "TerraRemastered/LowPolyFoliage")) continue;
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, GameObjectUtility.GetStaticEditorFlags(r.gameObject) | StaticEditorFlags.BatchingStatic);
            batched++;
        }
        log.Add($"static batching: {batched} floor renderers");
        T("sky+light"); EditorSceneManager.SaveScene(scene, ScenePath); T("save");
        var bs = EditorBuildSettings.scenes.ToList();
        if (!bs.Any(s => s.path == ScenePath)) { bs.Insert(0, new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = bs.ToArray(); }
        AssetDatabase.SaveAssets();
        int tris = root.GetComponentsInChildren<MeshFilter>().Where(m => m.sharedMesh).Sum(m => (int)(m.sharedMesh.GetIndexCount(0) / 3 + (m.sharedMesh.subMeshCount > 1 ? Enumerable.Range(1, m.sharedMesh.subMeshCount - 1).Sum(i => m.sharedMesh.GetIndexCount(i) / 3) : 0)));
        log.Add($"renderers {root.GetComponentsInChildren<Renderer>().Length}, tris ~{tris / 1000}k, camera at {cam.transform.position}");
        return string.Join("\n", log);
    }

    // ---------------------------------------------------------------- render pipeline settings
    static void ConfigureUrp(List<string> log)
    {
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (!urp) { log.Add("WARN no URP asset"); return; }
        var so = new SerializedObject(urp);
        void Set(string p, Action<SerializedProperty> a) { var sp = so.FindProperty(p); if (sp != null) a(sp); else log.Add("WARN urp prop " + p); }
        Set("m_RequireDepthTexture", p => p.boolValue = true);
        Set("m_ShadowDistance", p => p.floatValue = 140f);
        Set("m_ShadowCascadeCount", p => p.intValue = 4);
        Set("m_SoftShadowsSupported", p => p.boolValue = true);
        Set("m_MainLightShadowmapResolution", p => p.intValue = 4096);
        Set("m_MSAA", p => p.intValue = 4);
        Set("m_SupportsHDR", p => p.boolValue = true);
        // lights use the pipeline biases by default: normal bias opens light cracks between flat-shaded faces
        Set("m_ShadowNormalBias", p => p.floatValue = 0f);
        Set("m_ShadowDepthBias", p => p.floatValue = 1.5f);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(urp);
        log.Add("URP: depth texture, shadows 140 m x4 cascades soft 4096, MSAA 4x, HDR");
    }

    // ---------------------------------------------------------------- materials
    class Mats { public Dictionary<string, Material> m = new(); public Material this[string k] => m[k]; }

    static Material LitMat(string folder, string name, Color srgb, float smooth = 0.1f, bool twoSided = false)
    {
        var path = $"{folder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path) ?? AssetDatabase.LoadAssetAtPath<Material>(ProjectPaths.Find(name + ".mat") ?? "");
        if (!mat) { mat = new Material(LowPolyLit) { name = name }; AssetDatabase.CreateAsset(mat, path); }
        LowPolyLitSetup(mat, srgb, smooth);
        mat.SetFloat("_Cull", twoSided ? 0f : 2f);
        mat.doubleSidedGI = twoSided;
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Shader LowPolyLit => Shader.Find("TerraRemastered/LowPolyLit");

    /// Faceted matte surface (see Shaders/LowPolyLit.shader): per-face value jitter, cool shade, soft sheen.
    static void LowPolyLitSetup(Material mat, Color srgb, float smooth = 0.1f, float jitter = 0.07f, float facetScale = 97f)
    {
        mat.SetFloat("_FacetScale", facetScale);
        mat.shader = LowPolyLit;
        mat.SetColor("_BaseColor", srgb);
        mat.SetFloat("_FacetJitter", jitter); mat.SetFloat("_HueJitter", 0.02f);
        mat.SetColor("_ShadeTint", new Color(0.86f, 0.92f, 1.08f));
        mat.SetFloat("_TopLight", 0.06f); mat.SetFloat("_Sheen", Mathf.Clamp01(smooth * 1.5f));
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
    }

    static Color C(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1);

    /// Vegetation material on TerraRemastered/LowPolyFoliage (two-sided facets, wrap light, translucency, wind).
    /// Every plant material shares the wind values, so trunks and crowns move together.
    // wrap 0.12 (was 0.35): wrap lighting smoothed neighbouring facets into one tone; the faceted look needs a crisp key
    static Material Foliage(string name, Color? srgb, float trans = 0.6f, float flutter = 0.015f, float wrap = 0.12f, float sheen = 0.25f)
    {
        var path = ProjectPaths.Find(name + ".mat") ?? $"{FoliageMatFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find("TerraRemastered/LowPolyFoliage");
        if (!mat) { mat = new Material(sh) { name = name }; AssetDatabase.CreateAsset(mat, path); }
        var keep = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.green;
        mat.shader = sh;
        mat.SetColor("_BaseColor", srgb ?? keep);
        mat.SetFloat("_Translucency", trans); mat.SetFloat("_Flutter", flutter); mat.SetFloat("_Wrap", wrap); mat.SetFloat("_Smoothness", sheen);
        mat.SetFloat("_WindStrength", 0.012f); mat.SetFloat("_WindSpeed", 0.9f); mat.SetFloat("_AmbientBoost", 1.0f);
        mat.SetFloat("_FacetJitter", 0.06f); mat.SetColor("_ShadeTint", new Color(0.86f, 0.92f, 1.08f));
        mat.SetColor("_TransColor", new Color(0.85f, 1f, 0.35f));
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Mats Materials()
    {
        var m = new Mats();
        m.m["dirt"] = LitMat(MatFolder, "MI_Dirt", C(146, 82, 54));
        m.m["ground"] = LitMat(MatFolder, "MI_Ground", C(66, 114, 34));
        m.m["groundDark"] = LitMat(MatFolder, "MI_GroundDark", C(46, 88, 28));
        m.m["forest"] = LitMat(MatFolder, "MI_HillForest", C(52, 112, 42));
        m.m["shore"] = LitMat(MatFolder, "MI_Shore", C(176, 150, 102));
        m.m["bank"] = LitMat(MatFolder, "MI_StreamBank", C(116, 84, 60));
        m.m["lakebed"] = LitMat(MatFolder, "MI_LakeBed", C(84, 122, 96));
        m.m["mountain"] = LitMat(MatFolder, "MI_Mountain", C(70, 108, 96));
        m.m["mountainFar"] = LitMat(MatFolder, "MI_MountainFar", C(92, 116, 140));
        m.m["ridge"] = LitMat(MatFolder, "MI_Ridge", C(58, 110, 56));
        // generated terrain is nearly flat near the viewer: finer facet hash + stronger jitter so the ground reads faceted
        foreach (var k in new[] { "dirt", "ground", "groundDark", "forest", "shore", "bank", "lakebed" })
            LowPolyLitSetup(m.m[k], m.m[k].GetColor("_BaseColor"), 0.08f, 0.10f, 420f);
        // vegetation: foliage shader (leaf colours sRGB)
        Foliage("MI_Leaf", C(88, 170, 40));
        m.m["leafLight"] = Foliage("MI_LeafLight", C(136, 200, 50));
        m.m["leafDark"] = Foliage("MI_LeafDark", C(52, 128, 34));
        m.m["leafYellow"] = Foliage("MI_LeafYellow", C(160, 196, 60));
        Foliage("MI_LeafStem", null, 0.4f, 0.01f);
        Foliage("MI_Grass", null, 0.5f, 0.02f);
        Foliage("MI_Canopy", null, 0.25f, 0f, 0.1f, 0.05f);
        foreach (var n in new[] { "MI_Canopy", "MI_Leaf", "MI_LeafLight", "MI_LeafDark", "MI_LeafYellow", "MI_PalmTrunk", "MI_Bark", "MI_LeafStem", "MI_Grass", "MI_Heliconia", "MI_HeliconiaTip", "MI_Vine" })
            if (AssetDatabase.LoadAssetAtPath<Material>(ProjectPaths.Find(n + ".mat") ?? "") is { } fm)
            {   // crowns cast perforated shadows -> dappled light on the floor; everything else casts solid
                // crowns: shadows only from the bake (gap texture via _TransparencyLM, set in ConfigureBakedLighting)
                fm.SetFloat("_CastRealtime", n == "MI_Canopy" ? 0f : 1f); fm.SetFloat("_ShadowHoles", 0f); EditorUtility.SetDirty(fm);
            }
        Foliage("MI_Heliconia", C(214, 44, 26), 0.5f, 0.01f, 0.1f, 0.15f);
        Foliage("MI_HeliconiaTip", null, 0.5f, 0.01f, 0.1f, 0.15f);
        Foliage("MI_Vine", null, 0.2f, 0.01f);
        Foliage("MI_Orchid", C(240, 200, 230), 0.5f, 0.01f, 0.1f, 0.2f);
        Foliage("MI_PalmTrunk", null, 0f, 0f, 0.15f, 0f);
        Foliage("MI_Bark", C(112, 88, 66), 0f, 0f, 0.15f, 0f);
        var rockM = AssetDatabase.LoadAssetAtPath<Material>(ProjectPaths.Find("MI_Rock.mat") ?? "");
        if (rockM) LowPolyLitSetup(rockM, rockM.HasProperty("_BaseColor") ? rockM.GetColor("_BaseColor") : C(150, 95, 60), 0.12f, 0.09f);   // rocks: strongest facet read
        var rock = AssetDatabase.LoadAssetAtPath<Material>(ProjectPaths.Find("MI_Rock.mat") ?? "");
        if (rock) rock.SetColor("_BaseColor", C(150, 98, 64));
        return m;
    }

    static Material WaterMat(string name, Action<Material> setup)
    {
        var path = $"{WaterMatFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("TerraRemastered/LowPolyWater")) { name = name }; AssetDatabase.CreateAsset(mat, path); }
        mat.shader = Shader.Find("TerraRemastered/LowPolyWater");
        setup(mat);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---------------------------------------------------------------- noise and distance helpers
    static float Fbm(float x, float z, int oct = 4)
    {
        float s = 0, a = 0.5f, f = 1, n = 0;
        for (int i = 0; i < oct; i++) { s += a * Mathf.PerlinNoise(x * f + 31.7f * i, z * f - 17.3f * i); n += a; a *= 0.5f; f *= 2.03f; }
        return s / n;
    }

    static float SegDist(Vector2 p, Vector2 a, Vector2 b, out float t)
    {
        var ab = b - a; t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return (p - (a + ab * t)).magnitude;
    }

    static float PolyDist(Vector2 p, IList<Vector2> pts)
    {
        float best = float.MaxValue;
        for (int i = 0; i < pts.Count - 1; i++) best = Mathf.Min(best, SegDist(p, pts[i], pts[i + 1], out _));
        return best;
    }

    static float StreamDist(Vector2 p, out int idx)
    {
        float best = float.MaxValue; idx = 0;
        // coarse then fine
        for (int i = 0; i < streamS.Count; i += 4) { float d = (streamS[i] - p).sqrMagnitude; if (d < best) { best = d; idx = i; } }
        int lo = Mathf.Max(0, idx - 6), hi = Mathf.Min(streamS.Count - 2, idx + 6);
        best = float.MaxValue;
        for (int i = lo; i <= hi; i++) { float d = SegDist(p, streamS[i], streamS[i + 1], out float t); if (d < best) { best = d; idx = t < 0.5f ? i : i + 1; } }
        return best;
    }

    static float LakeEllipse(float x, float z)
    {
        float ex = (x + 14f) / (150f + 0.12f * Mathf.Max(0, z - 100)), ez = (z - 335f) / 275f;
        return ex * ex + ez * ez + (Fbm(x * 0.012f, z * 0.012f, 3) - 0.5f) * 0.35f;
    }

    // ---------------------------------------------------------------- height field
    static float BaseHeight(float x, float z)
    {
        float h = (Fbm(x * 0.09f, z * 0.09f) - 0.5f) * 0.7f;                         // small undulation
        float rightBank = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.5f, 12f, x));    // right side climbs with the stream
        h += rightBank * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-5f, 50f, z)) * 5.5f;
        h += rightBank * 0.8f;
        float toLake = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(20f, 52f, z));        // path descends to the shore
        h -= toLake * 5.8f * (1f - rightBank);
        // valley walls (jungle slopes left and right, wider with distance)
        float xc = -2f, w = 16f + 0.42f * Mathf.Max(0, z - 15f);
        float side = Mathf.Max(0, Mathf.Abs(x - xc) - w);
        h += side * (0.30f + 0.35f * Fbm(x * 0.01f, z * 0.01f)) + Mathf.Pow(side, 1.25f) * 0.04f;
        // forested hills behind the lake
        float far = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(560f, 820f, z));
        h += far * (18f + 55f * Fbm(x * 0.0045f + 3, z * 0.0045f, 4)) * (1f + 0.0015f * Mathf.Max(0, z - 820));
        // lake basin
        float e = LakeEllipse(x, z);
        if (e < 1.25f) h = Mathf.Lerp(h, LakeLevel - 5f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.25f, 0.75f, e)));
        return h;
    }

    static float Height(float x, float z)
    {
        var p = new Vector2(x, z);
        float h = BaseHeight(x, z);
        // stream channel
        float ds = StreamDist(p, out int si);
        if (ds < 3.2f)
        {
            float lvl = streamLevel[si];
            float half = 1.25f + 0.25f * (Fbm(x * 0.3f, z * 0.3f, 2) - 0.5f);
            float bed = lvl - 0.45f + 0.35f * Mathf.Pow(Mathf.Clamp01(ds / half), 2);
            float bank = Mathf.Max(h, lvl + 0.25f);
            h = ds < half ? bed : Mathf.Lerp(lvl + 0.1f, bank, Mathf.SmoothStep(0, 1, (ds - half) / (3.2f - half)));
        }
        // path: a slight groove
        float dp = PolyDist(p, PathPts);
        if (dp < 1.8f) h -= 0.09f * (1f - Mathf.SmoothStep(0, 1, (dp - 0.8f) / 1.0f));
        // gentle near-field relief: low-poly ground only reads as facets when its faces tilt a little (references);
        // ~+-0.17 m, fades out by 45 m, softer on the path, none at the stream banks
        float near = 1f - Mathf.SmoothStep(0, 1, (p.magnitude - 25f) / 20f);
        float onPath = Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0, 1, (dp - 0.9f) / 1.2f));
        float offStream = Mathf.SmoothStep(0, 1, (ds - 3.3f) / 1.5f);
        h += (Fbm(x * 0.55f + 17f, z * 0.55f - 4f, 2) - 0.5f) * 0.7f * near * onPath * offStream;
        return h;
    }

    static void BuildStreamProfile()
    {
        streamS = new List<Vector2>();
        for (int i = 0; i < StreamPts.Length - 1; i++)
        {
            var a = StreamPts[i]; var b = StreamPts[i + 1];
            int n = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / 0.3f));
            for (int k = 0; k < n; k++)
            {
                // Catmull-Rom for a smooth centreline
                var p0 = StreamPts[Mathf.Max(0, i - 1)]; var p3 = StreamPts[Mathf.Min(StreamPts.Length - 1, i + 2)];
                float t = k / (float)n;
                streamS.Add(0.5f * (2 * a + (-p0 + b) * t + (2 * p0 - 5 * a + 4 * b - p3) * t * t + (-p0 + 3 * a - 3 * b + p3) * t * t * t));
            }
        }
        streamS.Add(StreamPts[^1]);
        streamLevel = new List<float>(); cascades = new List<int>();
        float running = float.MaxValue, pool = float.MaxValue;
        for (int i = 0; i < streamS.Count; i++)
        {
            float lvl = BaseHeight(streamS[i].x, streamS[i].y) - 0.35f;
            running = Mathf.Min(running, lvl);
            if (pool == float.MaxValue) pool = running;
            if (pool - running > 0.7f) { pool = running; cascades.Add(i); }
            streamLevel.Add(pool);
        }
    }

    // ---------------------------------------------------------------- terrain mesh
    static List<float> Axis(float min, float max, float near, float growth, float jitterSeed)
    {
        var pos = new List<float> { 0 };
        for (float v = 0; v < max;) { v += near + growth * v; pos.Add(Mathf.Min(v, max)); }
        for (float v = 0; v > min;) { v -= near + growth * -v; pos.Insert(0, Mathf.Max(v, min)); }
        return pos;
    }

    static GameObject BuildTerrain(Transform parent, Mats mats, List<string> log)
    {
        var xs = Axis(-1500, 1500, 0.75f, 0.045f, 0);
        var zs = Axis(-400, 1500, 0.75f, 0.045f, 1);
        int nx = xs.Count, nz = zs.Count;
        var grid = new Vector3[nx, nz];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                float x = xs[i], z = zs[j];
                float cx = (i > 0 && i < nx - 1) ? (xs[i + 1] - xs[i - 1]) * 0.22f : 0;
                float cz = (j > 0 && j < nz - 1) ? (zs[j + 1] - zs[j - 1]) * 0.22f : 0;
                uint hsh = (uint)(i * 73856093 ^ j * 19349663);
                x += cx * (((hsh & 1023) / 1023f) - 0.5f);
                z += cz * ((((hsh >> 10) & 1023) / 1023f) - 0.5f);
                grid[i, j] = new Vector3(x, Height(x, z), z);
            }
        var keys = new[] { "dirt", "ground", "groundDark", "forest", "shore", "bank", "lakebed" };
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uv2 = new List<Vector2>();
        var subs = keys.ToDictionary(k => k, _ => new List<int>());
        // lightmap UVs in grid-index space: one chart, and texel density follows the grid (dense near the viewer)
        Vector2 U(int i, int j) => new(i / (nx - 1f), j / (nz - 1f));
        void Tri((int, int) ia, (int, int) ib, (int, int) ic)
        {
            Vector3 a = grid[ia.Item1, ia.Item2], b = grid[ib.Item1, ib.Item2], c = grid[ic.Item1, ic.Item2];
            var n = Vector3.Cross(b - a, c - a).normalized;
            if (n.y < 0) { (b, c) = (c, b); (ib, ic) = (ic, ib); n = -n; }
            var cen = (a + b + c) / 3f;
            string k = Classify(cen, n);
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); norms.Add(n); norms.Add(n); norms.Add(n);
            uv2.Add(U(ia.Item1, ia.Item2)); uv2.Add(U(ib.Item1, ib.Item2)); uv2.Add(U(ic.Item1, ic.Item2));
            subs[k].Add(s); subs[k].Add(s + 1); subs[k].Add(s + 2);
        }
        for (int i = 0; i < nx - 1; i++)
            for (int j = 0; j < nz - 1; j++)
            {
                var a = (i, j); var b = (i + 1, j); var c = (i + 1, j + 1); var d = (i, j + 1);
                if (((i + j) & 1) == 0) { Tri(a, d, c); Tri(a, c, b); } else { Tri(a, d, b); Tri(d, c, b); }
            }
        var used = keys.Where(k => subs[k].Count > 0).ToArray();
        var mesh = SaveMesh("SM_Gen_Terrain", m =>
        {
            m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(1, uv2);
            m.subMeshCount = used.Length;
            for (int s = 0; s < used.Length; s++) m.SetTriangles(subs[used[s]], s);
            m.RecalculateBounds();
        });
        var go = new GameObject("Terrain_Mesh", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterials = used.Select(k => mats[k]).ToArray();
        go.GetComponent<MeshCollider>().sharedMesh = mesh;
        go.isStatic = false;   // static batching broke this 219k-vertex multi-submesh mesh in Play Mode (flat grey)
        log.Add($"terrain {nx}x{nz} grid, {verts.Count / 3} tris, submeshes {string.Join(",", used)}");
        return go;
    }

    static string Classify(Vector3 c, Vector3 n)
    {
        var p = new Vector2(c.x, c.z);
        if (c.y < LakeLevel - 0.15f && LakeEllipse(c.x, c.z) < 1.3f) return "lakebed";
        float dp = PolyDist(p, PathPts);
        float wob = (Fbm(c.x * 0.6f, c.z * 0.6f, 2) - 0.5f) * 0.9f;
        if (dp < 1.45f + wob && c.z < 56) return "dirt";
        if (StreamDist(p, out _) < 1.9f + wob * 0.5f && c.z < 70) return "bank";
        if (c.y < LakeLevel + 0.45f && LakeEllipse(c.x, c.z) < 1.5f) return "shore";
        if (c.z > 70 || Mathf.Abs(c.x) > 60) return "forest";
        if (n.y < 0.8f) return "groundDark";
        return Fbm(c.x * 0.25f, c.z * 0.25f, 2) > 0.52f ? "groundDark" : "ground";
    }

    static Mesh SaveMesh(string name, Action<Mesh> fill)
    {
        var path = $"{GenFolder}/{name}.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool isNew = !mesh;
        if (isNew) mesh = new Mesh { name = name };
        mesh.Clear();
        fill(mesh);
        if (isNew) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // ---------------------------------------------------------------- water
    static void BuildLake(Transform parent, List<string> log)
    {
        var mat = WaterMat("M_Lake", m =>
        {
            m.SetColor("_ShallowColor", new Color(0.40f, 0.84f, 0.82f, 0.55f));
            m.SetColor("_DeepColor", new Color(0.10f, 0.47f, 0.58f, 0.96f));
            m.SetColor("_HorizonColor", new Color(0.72f, 0.88f, 0.96f, 1));
            m.SetFloat("_WaveHeight", 0.12f); m.SetFloat("_WaveScale", 0.35f); m.SetFloat("_WaveSpeed", 0.7f);
            m.SetVector("_Flow", new Vector4(0, 0, 1, 0));
            m.SetFloat("_DepthFade", 3.5f); m.SetFloat("_FoamDist", 0.35f); m.SetFloat("_BaseFoam", 0);
            m.SetFloat("_Gloss", 220); m.SetFloat("_Spec", 1.6f);
        });
        var xs = Axis(-260, 260, 1.6f, 0.03f, 0).Select(x => x - 14f).ToList();
        var zs = Axis(-300, 320, 1.6f, 0.03f, 0).Select(z => z + 335f).Where(z => z > 30).ToList();
        var verts = new List<Vector3>(); var tris = new List<int>();
        for (int j = 0; j < zs.Count; j++) for (int i = 0; i < xs.Count; i++) verts.Add(new Vector3(xs[i], LakeLevel, zs[j]));
        for (int j = 0; j < zs.Count - 1; j++)
            for (int i = 0; i < xs.Count - 1; i++)
            {
                // skip quads fully under high terrain
                var c = verts[j * xs.Count + i];
                if (LakeEllipse(c.x, c.z) > 1.4f) continue;
                int a = j * xs.Count + i, b = a + 1, d = a + xs.Count, e = d + 1;
                tris.AddRange(new[] { a, d, e, a, e, b });
            }
        var mesh = SaveMesh("SM_Gen_Lake", m => { m.indexFormat = IndexFormat.UInt32; m.SetVertices(verts); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds(); });
        var go = new GameObject("Lake_Water", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
        log.Add($"lake {tris.Count / 3} tris");
    }

    static void BuildStream(Transform parent, List<string> log)
    {
        var water = WaterMat("M_Stream", m =>
        {
            m.SetColor("_ShallowColor", new Color(0.50f, 0.86f, 0.86f, 0.45f));
            m.SetColor("_DeepColor", new Color(0.16f, 0.52f, 0.60f, 0.9f));
            m.SetColor("_HorizonColor", new Color(0.75f, 0.9f, 0.96f, 1));
            m.SetFloat("_WaveHeight", 0.03f); m.SetFloat("_WaveScale", 1.6f); m.SetFloat("_WaveSpeed", 1.6f);
            m.SetVector("_Flow", new Vector4(-0.35f, 0, -1, 1.2f));
            m.SetFloat("_DepthFade", 0.9f); m.SetFloat("_FoamDist", 0.18f); m.SetFloat("_BaseFoam", 0);
            m.SetFloat("_Gloss", 160); m.SetFloat("_Spec", 1.4f);
            m.SetFloat("_Streaks", 1f); m.SetFloat("_StreakSpeed", 0.9f); m.SetFloat("_Lap", 0.6f);
        });
        var white = WaterMat("M_StreamCascade", m =>
        {
            m.CopyPropertiesFromMaterial(water);
            m.SetColor("_ShallowColor", new Color(0.82f, 0.94f, 0.97f, 0.85f));
            m.SetFloat("_BaseFoam", 0.85f); m.SetVector("_Flow", new Vector4(-0.35f, 0, -1, 3.5f)); m.SetFloat("_WaveHeight", 0.02f);
        });
        var froth = WaterMat("M_StreamFroth", m =>
        {
            m.CopyPropertiesFromMaterial(water);
            m.SetFloat("_BaseFoam", 0.4f); m.SetVector("_Flow", new Vector4(-0.35f, 0, -1, 2.0f));
        });
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var sub = new[] { new List<int>(), new List<int>(), new List<int>() };
        const int across = 4;
        var cascadeSet = new HashSet<int>(cascades);
        for (int i = 0; i < streamS.Count; i++)
        {
            var p = streamS[i];
            var t = (streamS[Mathf.Min(i + 1, streamS.Count - 1)] - streamS[Mathf.Max(i - 1, 0)]).normalized;
            var side = new Vector2(t.y, -t.x);
            float half = 1.25f + 0.25f * (Fbm(p.x * 0.3f, p.y * 0.3f, 2) - 0.5f) + 0.35f;
            for (int k = 0; k <= across; k++)
            {
                float u = k / (float)across * 2 - 1;
                var q = p + side * u * half;
                verts.Add(new Vector3(q.x, streamLevel[i] + 0.02f, q.y));
                uvs.Add(new Vector2(k / (float)across, i * 0.3f));                // across 0-1, metres downstream (streaks)
            }
        }
        int rowLen = across + 1;
        for (int i = 0; i < streamS.Count - 1; i++)
        {
            float drop = streamLevel[i] - streamLevel[i + 1];
            int s = drop > 0.05f ? 1 : 0;
            if (s == 0) for (int c = 1; c <= 5; c++) if (cascadeSet.Contains(i - c + 1)) { s = 2; break; }
            for (int k = 0; k < across; k++)
            {
                int a = i * rowLen + k, b = a + 1, d = a + rowLen, e = d + 1;
                sub[s].AddRange(new[] { a, e, b, a, d, e });
            }
        }
        var mesh = SaveMesh("SM_Gen_Stream", m =>
        {
            m.indexFormat = IndexFormat.UInt32; m.SetVertices(verts); m.SetUVs(0, uvs); m.subMeshCount = 3;
            for (int s = 0; s < 3; s++) m.SetTriangles(sub[s], s);
            m.RecalculateNormals(); m.RecalculateBounds();
        });
        var go = new GameObject("Stream_Water", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>(); r.sharedMaterials = new[] { water, white, froth }; r.shadowCastingMode = ShadowCastingMode.Off;
        log.Add($"stream {streamS.Count} samples, {cascades.Count} cascades at z={string.Join(",", cascades.Select(c => streamS[c].y.ToString("F0")))}");
    }

    // ---------------------------------------------------------------- mountains
    static void BuildMountains(Transform parent, Mats mats, List<string> log)
    {
        BuildRidgeRing(parent, mats, log);
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uv2 = new List<Vector2>(); var near = new List<int>(); var far = new List<int>();
        int na = 90, nr = 16;
        float a0 = -95f * Mathf.Deg2Rad, a1 = 95f * Mathf.Deg2Rad, r0 = 1500, r1 = 3800;
        var g = new Vector3[na + 1, nr + 1];
        var rnd = new Random(7);
        for (int i = 0; i <= na; i++)
            for (int j = 0; j <= nr; j++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)na) + (float)(rnd.NextDouble() - 0.5) * 0.012f;
                float r = Mathf.Lerp(r0, r1, j / (float)nr) + (float)(rnd.NextDouble() - 0.5) * 60f;
                float x = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r;
                float ridge = 1f - Mathf.Abs(Fbm(x * 0.0009f + 5, z * 0.0009f, 4) * 2 - 1);      // ridged noise
                float h = 40f + 260f * ridge * ridge * Mathf.SmoothStep(0, 1, (r - r0) / 700f);
                // main peak: slightly right of the lake gap
                var pk = new Vector2(Mathf.Sin(9f * Mathf.Deg2Rad) * 2500f, Mathf.Cos(9f * Mathf.Deg2Rad) * 2500f);
                float dpk = (new Vector2(x, z) - pk).magnitude;
                h += 560f * Mathf.Pow(Mathf.Max(0, 1 - dpk / 650f), 1.3f);
                // secondary peak left
                var pk2 = new Vector2(Mathf.Sin(-14f * Mathf.Deg2Rad) * 2900f, Mathf.Cos(-14f * Mathf.Deg2Rad) * 2900f);
                h += 260f * Mathf.Pow(Mathf.Max(0, 1 - (new Vector2(x, z) - pk2).magnitude / 600f), 1.2f);
                if (j == 0) h = -30f;                                                              // tuck under the hills
                g[i, j] = new Vector3(x, h, z);
            }
        void Tri((int, int) ia, (int, int) ib, (int, int) ic, List<int> list)
        {
            Vector3 a = g[ia.Item1, ia.Item2], b = g[ib.Item1, ib.Item2], c = g[ic.Item1, ic.Item2];
            var n = Vector3.Cross(b - a, c - a).normalized; if (n.y < 0) { (b, c) = (c, b); (ib, ic) = (ic, ib); n = -n; }
            int s = verts.Count; verts.Add(a); verts.Add(b); verts.Add(c); norms.Add(n); norms.Add(n); norms.Add(n);
            foreach (var q in new[] { ia, ib, ic }) uv2.Add(new Vector2(q.Item1 / (float)na, q.Item2 / (float)nr));   // grid-index lightmap UVs
            list.Add(s); list.Add(s + 1); list.Add(s + 2);
        }
        for (int i = 0; i < na; i++)
            for (int j = 0; j < nr; j++)
            {
                var list = j < nr / 2 ? near : far;
                var a = (i, j); var b = (i + 1, j); var c = (i + 1, j + 1); var d = (i, j + 1);
                if (((i + j) & 1) == 0) { Tri(a, b, c, list); Tri(a, c, d, list); } else { Tri(a, b, d, list); Tri(b, c, d, list); }
            }
        var mesh = SaveMesh("SM_Gen_Mountains", m =>
        {
            m.indexFormat = IndexFormat.UInt32; m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(1, uv2); m.subMeshCount = 2;
            m.SetTriangles(near, 0); m.SetTriangles(far, 1); m.RecalculateBounds();
        });
        var go = new GameObject("Mountains_Mesh", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterials = new[] { mats["mountain"], mats["mountainFar"] }; mr.shadowCastingMode = ShadowCastingMode.Off;
        log.Add($"mountains {verts.Count / 3} tris");
    }

    /// Lower green ridges between the lake hills and the mountains (0.9-1.5 km): the layered, hazy valley sides.
    static void BuildRidgeRing(Transform parent, Mats mats, List<string> log)
    {
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uv2 = new List<Vector2>(); var tris = new List<int>();
        int na = 110, nr = 8;
        float a0 = -100f * Mathf.Deg2Rad, a1 = 100f * Mathf.Deg2Rad, r0 = 880, r1 = 1550;
        var g = new Vector3[na + 1, nr + 1];
        var rnd = new Random(11);
        for (int i = 0; i <= na; i++)
            for (int j = 0; j <= nr; j++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)na) + (float)(rnd.NextDouble() - 0.5) * 0.01f;
                float r = Mathf.Lerp(r0, r1, j / (float)nr) + (float)(rnd.NextDouble() - 0.5) * 40f;
                float x = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r;
                float ridge = 1f - Mathf.Abs(Fbm(x * 0.0016f + 9, z * 0.0016f, 3) * 2 - 1);
                float h = (60f + 190f * ridge * ridge) * Mathf.Sin(Mathf.PI * j / nr);
                if (Mathf.Abs(a) < 12f * Mathf.Deg2Rad) h *= Mathf.Lerp(0.35f, 1f, Mathf.Abs(a) / (12f * Mathf.Deg2Rad)); // valley gap toward the peak
                g[i, j] = new Vector3(x, h - 20f, z);
            }
        for (int i = 0; i < na; i++)
            for (int j = 0; j < nr; j++)
            {
                var q = new[] { g[i, j], g[i + 1, j], g[i + 1, j + 1], g[i, j + 1] };
                var qu = new[] { new Vector2(i, j), new Vector2(i + 1, j), new Vector2(i + 1, j + 1), new Vector2(i, j + 1) };   // grid-index lightmap UVs
                foreach (var t in ((i + j) & 1) == 0 ? new[] { (0, 1, 2), (0, 2, 3) } : new[] { (0, 1, 3), (1, 2, 3) })
                {
                    Vector3 A = q[t.Item1], B = q[t.Item2], Cc = q[t.Item3];
                    Vector2 uA = qu[t.Item1], uB = qu[t.Item2], uC = qu[t.Item3];
                    var n = Vector3.Cross(B - A, Cc - A).normalized; if (n.y < 0) { (B, Cc) = (Cc, B); (uB, uC) = (uC, uB); n = -n; }
                    int s0 = verts.Count; verts.Add(A); verts.Add(B); verts.Add(Cc); norms.Add(n); norms.Add(n); norms.Add(n);
                    foreach (var u in new[] { uA, uB, uC }) uv2.Add(new Vector2(u.x / na, u.y / nr));
                    tris.Add(s0); tris.Add(s0 + 1); tris.Add(s0 + 2);
                }
            }
        var mesh = SaveMesh("SM_Gen_Ridges", m => { m.indexFormat = IndexFormat.UInt32; m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(1, uv2); m.SetTriangles(tris, 0); m.RecalculateBounds(); });
        var go = new GameObject("Ridges_Mesh", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mats["ridge"]; mr.shadowCastingMode = ShadowCastingMode.Off;
        log.Add($"ridges {tris.Count / 3} tris");
    }

    // ---------------------------------------------------------------- placement
    static readonly Dictionary<string, GameObject> cache = new();
    static GameObject Asset(string name)
    {
        if (cache.TryGetValue(name, out var g) && g) return g;
        string path = name.StartsWith("Assets/") ? name : ProjectPaths.Find("PF_" + name + ".prefab");
        g = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
        if (!g) throw new Exception("asset not found: " + name);
        return cache[name] = g;
    }

    static float Ground(float x, float z) =>
        Physics.Raycast(new Vector3(x, 3000, z), Vector3.down, out var hit, 6000f) ? hit.point.y : Height(x, z);

    static Material leafLight, leafDark, leafYellow, leafBase, rockMat;
    // CC0 pack material names -> our library (palette, foliage shader and wind)
    static readonly Dictionary<string, string> ThirdPartyMap = new()
    {
        ["Green"] = "MI_Leaf", ["DarkGreen"] = "MI_LeafDark", ["Leaves"] = "MI_LeafLight", ["Yellow"] = "MI_LeafYellow",
        ["Wood"] = "MI_Bark", ["grass"] = "MI_Grass", ["colorRed"] = "MI_Heliconia", ["Pink"] = "MI_Heliconia",
    };

    static GameObject Put(Transform parent, string asset, float x, float z, float yaw, float scale, float sink = 0.05f,
                          float? targetHeight = null, int leafVariant = -1, bool sway = false, float y = float.NaN, bool shadows = true)
    {
        var src = Asset(asset);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        float gy = float.IsNaN(y) ? Ground(x, z) : y;
        go.transform.SetPositionAndRotation(new Vector3(x, gy - sink, z), Quaternion.Euler(0, yaw, 0) * src.transform.localRotation);
        if (targetHeight.HasValue)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                scale = targetHeight.Value / Mathf.Max(0.01f, b.size.y) * go.transform.localScale.y;
            }
        }
        go.transform.localScale = Vector3.one * scale;
        bool isRock = asset.Contains("ock") || asset.Contains("stone");
        if (float.IsNaN(y) && (isRock || asset == "ButtressTree"))
        {
            // ground at the lowest point under the footprint so nothing hangs over a slope (pipeline placement rule)
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float k = asset == "ButtressTree" ? 0.75f : 0.6f, minG = float.MaxValue;
                for (int ix = -1; ix <= 1; ix++)
                    for (int iz = -1; iz <= 1; iz++)
                        minG = Mathf.Min(minG, Ground(b.center.x + ix * b.extents.x * k, b.center.z + iz * b.extents.z * k));
                go.transform.position += Vector3.up * (minG - sink - b.min.y);
            }
        }
        if (leafVariant >= 0)
        {
            var swap = leafVariant switch { 1 => leafLight, 2 => leafDark, 3 => leafYellow, _ => null };
            if (swap) foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++) if (ms[i] == leafBase) ms[i] = swap;
                    r.sharedMaterials = ms;
                }
        }
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            var ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++)
            {
                if (!ms[i]) continue;
                if (isRock && rockMat) { ms[i] = rockMat; continue; }
                if (asset.StartsWith("Assets/ThirdParty/") && ThirdPartyMap.TryGetValue(ms[i].name, out var lib3))
                {
                    var m3 = AssetDatabase.LoadAssetAtPath<Material>(ProjectPaths.Find(lib3 + ".mat") ?? "");
                    if (m3) { ms[i] = m3; continue; }
                }
                if (AssetDatabase.GetAssetPath(ms[i]).EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) && ms[i].name.StartsWith("MI_"))
                {
                    var lib = ProjectPaths.Find(ms[i].name + ".mat");
                    if (lib != null) ms[i] = AssetDatabase.LoadAssetAtPath<Material>(lib);
                }
            }
            r.sharedMaterials = ms;
        }
        if (sway && UseSwayScript) { var s = go.AddComponent<PlantSway>(); s.amplitude = 1.2f + 1.6f / Mathf.Max(0.6f, scale); }
        return go;
    }

    const bool UseSwayScript = false;   // wind now lives in the LowPolyFoliage shader (vertex sway by height)
    static float Rf(Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
    static T Pick<T>(Random r, IList<T> l) => l[r.Next(l.Count)];

    static bool Free(Vector2 p, List<(Vector2 p, float r)> taken, float rad)
    {
        foreach (var t in taken) if ((t.p - p).sqrMagnitude < (t.r + rad) * (t.r + rad)) return false;
        return true;
    }

    static void Place(Transform hero, Transform jungle, Transform streamRocks, Transform forest, Transform debris, Random rnd, List<string> log)
    {
        leafBase = AssetDatabase.LoadAssetAtPath<Material>(ProjectPaths.Find("MI_Leaf.mat"));
        rockMat = AssetDatabase.LoadAssetAtPath<Material>(ProjectPaths.Find("MI_Rock.mat"));
        leafLight = AssetDatabase.LoadAssetAtPath<Material>($"{FoliageMatFolder}/MI_LeafLight.mat");
        leafDark = AssetDatabase.LoadAssetAtPath<Material>($"{FoliageMatFolder}/MI_LeafDark.mat");
        leafYellow = AssetDatabase.LoadAssetAtPath<Material>($"{FoliageMatFolder}/MI_LeafYellow.mat");
        var taken = new List<(Vector2 p, float r)>();
        void Take(float x, float z, float r) => taken.Add((new Vector2(x, z), r));
        int n0 = 0;

        // ---- hero layout (hand placed, matches the reference composition)
        // buttress trees on the left, with canopy trees above them
        foreach (var (x, z, yaw, s) in new[] { (-8.6f, 11f, 25f, 1.15f), (-7.6f, 25f, 150f, 0.85f), (-10.5f, 28f, 260f, 1.05f), (-15f, 15f, 80f, 1.3f), (-12f, 42f, 200f, 1.0f), (-19f, 32f, 10f, 1.2f) })
        {
            Put(hero, "ButtressTree", x, z, yaw, s, sink: 0.3f);
            Put(hero, "Tree_B", x + 0.4f, z + 0.3f, yaw + 40, s * 1.5f, sink: 1.2f * s);   // crown over the cut trunk ends
            Take(x, z, 2.2f * s);
        }
        // dapple canopy: crowns placed up-sun of the floor in view, so their perforated shadows (MI_Canopy _ShadowHoles +
        // SoftShadows.hlsl) land as soft pools of light and shade on the path and banks (references: patterned floor)
        {
            var ld = Quaternion.Euler(SunEuler) * Vector3.forward;                    // direction the light travels
            var flat = new Vector2(ld.x, ld.z).normalized; float tanE = -ld.y / new Vector2(ld.x, ld.z).magnitude;
            int dap = 0;
            // 4 (was 8): with baked shadows the crowns' gaps multiply, 2-4 overlapping crowns left the path in solid shade
            foreach (var (tx, tz, s) in new[] { (-1f, 4f, 1.3f), (1.5f, 8.5f, 1.25f), (3.5f, 13f, 1.35f), (0.5f, 18f, 1.3f) })
            {
                float crownH = 9.5f * s;                                               // Tree_A crown centre height at this scale
                var c = new Vector2(tx, tz) - flat * (crownH / tanE);
                if (StreamDist(c, out _) < 3f || PolyDist(c, PathPts) < 3f || !Free(c, taken, 1.5f)) c += new Vector2(-2.5f, 1.5f);
                Put(hero, "Tree_A", c.x, c.y, (tx * 53f + tz * 31f) % 360f, s, sink: 0.25f);
                Take(c.x, c.y, 1.6f); dap++;
            }
            log.Add($"dapple canopy trees {dap}");
        }
        // canopy ceiling over the left foreground
        foreach (var (x, z, s) in new[] { (-5f, 3f, 1.6f), (-9f, 18f, 1.7f), (-3f, 12f, 1.3f), (-14f, 6f, 1.8f) })
            Put(hero, "Tree_A", x, z, x * 37, s, sink: 0.2f);
        // hanging vines under the canopy
        int vi = 0;
        foreach (var (x, z, top) in new[] { (-6.2f, 8f, 13f), (-5.4f, 10.5f, 11f), (-7.2f, 12f, 14f), (-3.9f, 17f, 12f), (-4.8f, 6.5f, 12.5f), (-8.6f, 7f, 15f), (-3.2f, 21f, 11f), (-9.4f, 24f, 13f) })
            Put(hero, vi++ % 2 == 0 ? "Vine_A" : "Vine_B", x, z, vi * 47f, Rf(rnd, 0.9f, 1.2f), y: Ground(x, z) + top, sink: 0, sway: true);
        // big foreground leaves, left
        Put(hero, "BigLeaf_Hero", -3.3f, 2.4f, 20, 1.05f, leafVariant: 0, sway: true); Take(-3.3f, 2.4f, 1.2f);
        Put(hero, "BigLeaf_Hero", -3.9f, 5.2f, 160, 1.15f, leafVariant: 1, sway: true); Take(-3.9f, 5.2f, 1.2f);
        Put(hero, "Banana_B", -4.6f, 6.0f, 70, 1.0f, leafVariant: 0, sway: true); Take(-4.6f, 6f, 1.2f);
        Put(hero, "BigLeaf_B", -3.0f, 8.0f, 300, 1.1f, leafVariant: 2, sway: true); Take(-3f, 8f, 1f);
        Put(hero, "Heliconia_A", -3.1f, 5.6f, 10, 1.1f, sway: true); Take(-3.1f, 5.6f, 0.5f);
        Put(hero, "Heliconia_B", -4.2f, 11.5f, 120, 1.2f, sway: true); Take(-4.2f, 11.5f, 0.5f);
        Put(hero, "Heliconia_A", -2.8f, 3.6f, 200, 0.9f, sway: true);
        Put(hero, "Banana_A", -6.0f, 2.5f, 10, 1.1f, leafVariant: 1, sway: true); Take(-6f, 2.5f, 1.2f);
        // between path and stream
        Put(hero, "BigLeaf_A", 2.9f, 4.0f, 40, 0.9f, leafVariant: 1, sway: true); Take(2.9f, 4.0f, 1f);
        Put(hero, "BigLeaf_B", 2.4f, 8.6f, 210, 1.2f, leafVariant: 0, sway: true); Take(2.4f, 8.6f, 1.1f);
        Put(hero, "Heliconia_B", 3.3f, 12.2f, 60, 1.0f, sway: true);
        Put(hero, "Banana_A", 2.8f, 15.5f, 200, 1.0f, leafVariant: 2, sway: true); Take(2.8f, 15.5f, 1f);
        // creek side kept sparse (user: too much foliage at the creek): a few accents, the bank stays readable
        foreach (var (a, x, z, yaw, sc, lv) in new[] { ("Fern_A", 0.9f, 3.6f, 20f, 0.7f, 0), ("Grass_A", 0.6f, 7.0f, 60f, 1f, -1), ("Fern_B", 2.4f, 13.5f, 30f, 0.8f, 0) })
            Put(hero, a, x, z, yaw, sc, leafVariant: lv, sway: a != "Grass_A", shadows: a != "Grass_A");
        // palms right-centre
        foreach (var (a, x, z, yaw, s) in new[] { ("Palm_B", 7.2f, 23f, 200f, 1.05f), ("Palm_A", 10f, 29f, 120f, 1.1f), ("Palm_C", 4.8f, 31f, 330f, 1.0f), ("Palm_B", 13.5f, 36f, 40f, 0.95f), ("Palm_A", 3.0f, 41f, 260f, 0.9f), ("Palm_C", 15.5f, 24f, 90f, 1.1f) })
        { Put(hero, a, x, z, yaw, s, leafVariant: rnd.Next(3), sink: 0.2f); Take(x, z, 1.5f); }
        // rock cliffs on the lake shores (the grey cliff left of the lake in the reference), grounded by footprint
        foreach (var (x, z, yaw, h) in new[] { (-24f, 78f, 40f, 16f), (-31f, 70f, 160f, 11f), (34f, 112f, 220f, 14f) })
            Put(hero, "Rock_C", x, z, yaw, 1, targetHeight: h, sink: 2f);
        // round-crowned tree across the shore, right of centre
        Put(hero, "Tree_B", 22f, 74f, 30, 1.1f); Take(22f, 74f, 5f);
        Put(hero, "Tree_A", -16f, 60f, 130, 1.5f); Take(-16f, 60f, 5f);
        // big boulders on the right
        foreach (var (x, z, yaw, s) in new[] { (10.2f, 8.5f, 30f, 1.45f), (9.4f, 14.0f, 120f, 1.2f), (11.5f, 3.0f, 250f, 1.5f), (12.5f, 18.5f, 300f, 1.7f) })
        { Put(hero, "Rock_C", x, z, yaw, s, sink: 0.4f * s); Take(x, z, 1.6f * s); }
        // more buttress giants all around the viewer (outside the lake view and the stream), each with a crown and vines
        int giants = 0;
        for (int tries = 0; tries < 400 && giants < 11; tries++)
        {
            float ang = Rf(rnd, 0, 360) * Mathf.Deg2Rad, rad = Rf(rnd, 12, 34);
            float x = Mathf.Sin(ang) * rad, z = Mathf.Cos(ang) * rad;
            var p = new Vector2(x, z);
            if (z > 0 && Mathf.Abs(x + 1) < 9 + 0.3f * z) continue;
            if (StreamDist(p, out _) < 4f || PolyDist(p, PathPts) < 3f) continue;
            if (Ground(x, z) < LakeLevel + 1f || !Free(p, taken, 4.5f)) continue;
            float sc = Rf(rnd, 0.9f, 1.35f), yaw = Rf(rnd, 0, 360);
            Put(hero, "ButtressTree", x, z, yaw, sc, sink: 0.3f);
            Put(hero, rnd.NextDouble() < 0.5 ? "Tree_B" : "Tree_A", x + 0.4f, z + 0.3f, yaw + 40, sc * 1.5f, sink: 1.2f * sc);
            for (int v = 0; v < 3; v++)
            {
                float vx = x + Rf(rnd, -3, 3), vz = z + Rf(rnd, -3, 3);
                Put(hero, v % 2 == 0 ? "Vine_A" : "Vine_B", vx, vz, Rf(rnd, 0, 360), Rf(rnd, 0.9f, 1.3f), y: Ground(vx, vz) + Rf(rnd, 10, 14) * sc, sink: 0);
            }
            Take(x, z, 4f * sc); giants++;
        }
        log.Add($"hero placements {hero.childCount} (giants {giants})");

        // ---- rocks along the stream and at each cascade lip
        var rockKinds = new[] { "Rock_A", "Rock_B", "Rock_C", Q + "Rock_1.fbx", Q + "Rock_3.fbx", Q + "Rock_5.fbx", K + "rock_largeA.fbx", K + "rock_largeC.fbx" };
        for (int i = 0; i < streamS.Count; i += 6)
        {
            var p = streamS[i]; if (p.y > 60) continue;
            var t = (streamS[Mathf.Min(i + 1, streamS.Count - 1)] - streamS[Mathf.Max(i - 1, 0)]).normalized;
            var side = new Vector2(t.y, -t.x);
            foreach (int sgn in new[] { -1, 1 })
            {
                if (rnd.NextDouble() < 0.55) continue;
                var q = p + side * sgn * Rf(rnd, 1.7f, 2.6f);
                if (PolyDist(q, PathPts) < 1.8f) continue;
                float sz = Rf(rnd, 0.5f, 1.2f) * (p.y < 15 ? 1.0f : 1.25f);
                var kind = Pick(rnd, rockKinds);
                Put(streamRocks, kind, q.x, q.y, Rf(rnd, 0, 360), 1, targetHeight: sz * Rf(rnd, 0.6f, 1.0f), sink: 0.25f * sz);
                Take(q.x, q.y, 0.6f * sz);
            }
        }
        foreach (int ci in cascades)
        {
            var p = streamS[ci];
            var t = (streamS[Mathf.Min(ci + 1, streamS.Count - 1)] - streamS[Mathf.Max(ci - 1, 0)]).normalized;
            var side = new Vector2(t.y, -t.x);
            foreach (float u in new[] { -1.15f, -0.45f, 0.5f, 1.2f })
            {
                if (Mathf.Abs(u) < 0.6f && rnd.NextDouble() < 0.4) continue;
                var q = p + side * u * 1.3f + t * Rf(rnd, -0.2f, 0.2f);
                float sz = Mathf.Abs(u) > 1 ? Rf(rnd, 0.9f, 1.4f) : Rf(rnd, 0.45f, 0.7f);
                Put(streamRocks, Pick(rnd, new[] { "Rock_A", "Rock_B", "Rock_A" }), q.x, q.y, Rf(rnd, 0, 360), 1,
                    targetHeight: sz * 1.2f, sink: 0.15f * sz);
            }
        }
        log.Add($"stream rocks {streamRocks.childCount}");

        // ---- jungle scatter near the viewer
        var groundKinds = new (string a, float w, float rad, float hmin, float hmax, bool leafy)[]
        {
            ("Grass_A", 5, 0.25f, 0.35f, 0.7f, false), ("Fern_A", 3, 0.6f, 0.7f, 1.1f, true), ("Fern_B", 3, 0.6f, 0.7f, 1.2f, true),
            ("BigLeaf_A", 2.5f, 0.9f, 0.8f, 1.3f, true), ("BigLeaf_B", 2.5f, 1f, 0.8f, 1.3f, true), ("Banana_A", 0.8f, 1.2f, 0.8f, 1.2f, true),
            ("Banana_B", 0.6f, 1.3f, 0.8f, 1.1f, true), ("Heliconia_A", 0.25f, 0.5f, 0.8f, 1.2f, false), ("Heliconia_B", 0.25f, 0.5f, 0.8f, 1.2f, false),
            (Q + "Plant_1.fbx", 1.2f, 0.7f, 0.7f, 1.3f, false), (Q + "Plant_2.fbx", 1.2f, 0.7f, 0.7f, 1.3f, false), (Q + "Plant_3.fbx", 1.2f, 0.7f, 0.7f, 1.3f, false),
            (K + "flower_redA.fbx", 0.3f, 0.2f, 0.3f, 0.5f, false),
        };
        float wsum = groundKinds.Sum(k => k.w);
        int placed = 0;
        for (int tries = 0; tries < 26000 && placed < 3400; tries++)
        {
            float x = Rf(rnd, -45, 45), z = Rf(rnd, -45, 60);
            if (new Vector2(x, z).magnitude < 3.4f) continue;                 // keep the viewer's face clear
            var p = new Vector2(x, z);
            float dp = PolyDist(p, PathPts);
            if (dp < 1.35f) continue;
            float dsr = StreamDist(p, out int si);
            if (dsr < 1.9f) continue;
            if (rnd.NextDouble() > Mathf.Lerp(0.2f, 1f, Mathf.SmoothStep(0, 1, (dsr - 2f) / 6f))) continue;   // thin out toward the creek
            float gy = Ground(x, z);
            if (gy < LakeLevel + 0.25f) continue;
            // density: denser away from the path, thinner toward the lake opening in the centre
            float dens = Mathf.Clamp01((dp - 1.2f) / 3f) * (1f - 0.75f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(30, 52, z)) * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(6, 18, Mathf.Abs(x - 0.5f)))));
            if (rnd.NextDouble() > dens) continue;
            float pick = Rf(rnd, 0, wsum); int ki = 0;
            for (; ki < groundKinds.Length - 1; ki++) { pick -= groundKinds[ki].w; if (pick <= 0) break; }
            var k = groundKinds[ki];
            if (k.a.Contains("flower") && p.magnitude < 14f) continue;            // their stalks read as pink sticks up close
            bool corridor = z > 3.5f && Mathf.Abs(x - Mathf.Lerp(0f, -6f, Mathf.InverseLerp(4, 55, z))) < 2.2f + 0.22f * z;
            if (corridor && k.hmax > 1.0f) continue;
            float distCam = p.magnitude;
            float hscale = 1f + Mathf.Clamp01((distCam - 10f) / 30f) * 0.6f;    // further plants a bit bigger: fills the view
            float th = Rf(rnd, k.hmin, k.hmax) * hscale;
            if (!Free(p, taken, k.rad * 0.6f * hscale)) continue;
            bool own = !k.a.StartsWith("Assets/");
            Put(jungle, k.a, x, z, Rf(rnd, 0, 360), own ? th : 1f, targetHeight: own ? null : th, leafVariant: k.leafy ? rnd.Next(4) : -1,
                sway: distCam < 14f && k.a != "Grass_A", shadows: k.a != "Grass_A" && distCam < 40 && th > 1.0f);   // knee-high plants made speckled shadows
            Take(x, z, k.rad * 0.6f * hscale);
            placed++;
        }
        log.Add($"jungle scatter {placed}");
        int carpet = 0;
        for (int tries = 0; tries < 2500 && carpet < 260; tries++)
        {
            float x = Rf(rnd, -9, 9), z = Rf(rnd, 0.5f, 22);
            var p = new Vector2(x, z);
            float dsc = StreamDist(p, out _);
            if (p.magnitude < 1.6f || PolyDist(p, PathPts) < 1.25f || dsc < 2.6f) continue;
            if (rnd.NextDouble() > Mathf.SmoothStep(0, 1, (dsc - 2.6f) / 4f)) continue;   // no grass carpet on the creek banks
            if (!Free(p, taken, 0.18f)) continue;
            string a = Pick(rnd, new[] { "Grass_A", "Grass_A", "Grass_A", "Fern_A", "Fern_B" });
            bool own = !a.StartsWith("Assets/");
            float th = a.StartsWith("Fern") ? Rf(rnd, 0.45f, 0.7f) : a.StartsWith("Grass") ? Rf(rnd, 1.0f, 1.6f) : Rf(rnd, 0.25f, 0.4f);
            Put(jungle, a, x, z, Rf(rnd, 0, 360), own ? th : 1f, targetHeight: own ? null : th, leafVariant: rnd.Next(4), shadows: false, sway: a.StartsWith("Fern"));
            Take(x, z, 0.18f); carpet++;
        }
        log.Add($"ground carpet {carpet}");

        // ---- jungle walls: palms, trees and banana groves on the valley sides
        int walls = 0;
        for (int tries = 0; tries < 12000 && walls < 650; tries++)
        {
            float x = Rf(rnd, -80, 80), z = Rf(rnd, -90, 120);
            var p = new Vector2(x, z);
            if (p.magnitude < 9f) continue;
            if (z > 0 && Mathf.Abs(x + 1) < 9 + 0.3f * z) continue;           // keep the central view to the lake open
            if (StreamDist(p, out _) < 2.5f || PolyDist(p, PathPts) < 3f) continue;
            float gy = Ground(x, z); if (gy < LakeLevel + 0.4f) continue;
            if (!Free(p, taken, 2.5f)) continue;
            string a = Pick(rnd, new[] { "Palm_A", "Palm_B", "Palm_C", "Tree_A", "Tree_B", Q + "PalmTree_1.fbx", Q + "PalmTree_3.fbx", Q + "CommonTree_1.fbx", Q + "CommonTree_3.fbx", "Banana_B" });
            bool own = !a.StartsWith("Assets/");
            float th = a.Contains("Palm") ? Rf(rnd, 9, 16) : a.Contains("Banana") ? Rf(rnd, 2.5f, 4) : Rf(rnd, 10, 18);
            Put(forest, a, x, z, Rf(rnd, 0, 360), own ? (a.StartsWith("Tree") ? th / 13f : a.StartsWith("Palm") ? th / 12f : 1.2f) : 1f,
                targetHeight: own ? null : th, sink: 0.3f, leafVariant: rnd.Next(4));
            Take(x, z, 2.5f); walls++;
        }
        // ---- forested hills around/behind the lake (cheap blob trees)
        int hills = 0;
        for (int tries = 0; tries < 60000 && hills < 4200; tries++)
        {
            float x = Rf(rnd, -650, 650), z = Rf(rnd, -650, 1150);
            float gy = Ground(x, z);
            if (gy < LakeLevel + 0.6f) continue;
            var p = new Vector2(x, z);
            float d = p.magnitude;
            if (d < 75) continue;                                              // near ones are the jungle walls' job
            float rad = Mathf.Clamp(d * 0.016f, 2.5f, 9f);
            if (!Free(p, taken, rad)) continue;
            float th = Rf(rnd, 12, 20) * Mathf.Lerp(1f, 2.0f, Mathf.Clamp01(d / 900f));
            bool palm = rnd.NextDouble() < 0.06;
            string a = palm ? "Palm_B" : rnd.NextDouble() < 0.5 ? "Tree_A" : "Tree_B";
            // sink the trunks so the crowns form a continuous canopy on the hills (no picket fence of trunks)
            Put(forest, a, x, z, Rf(rnd, 0, 360), th / 13f, sink: palm ? 0.5f : th * Rf(rnd, 0.35f, 0.5f), shadows: d < 140, leafVariant: rnd.Next(3));
            Take(x, z, rad); hills++;
        }
        log.Add($"jungle walls {walls}, hill forest {hills}");

        // ---- small blocky debris on the path (the faceted cubes in the reference)
        int deb = 0;
        for (int tries = 0; tries < 400 && deb < 45; tries++)
        {
            float z = Rf(rnd, 0.8f, 32), x = Rf(rnd, -4, 2);
            var p = new Vector2(x, z);
            if (PolyDist(p, PathPts) > 1.3f) continue;
            float s = Rf(rnd, 0.06f, 0.18f);
            var go = Put(debris, Pick(rnd, new[] { "Rock_A", "Rock_B", K + "stone_smallA.fbx", K + "stone_smallC.fbx" }), x, z, Rf(rnd, 0, 360), 1, targetHeight: s, sink: s * 0.25f, shadows: false);
            go.transform.rotation = Quaternion.Euler(Rf(rnd, -20, 20), Rf(rnd, 0, 360), Rf(rnd, -20, 20));
            deb++;
        }
        log.Add($"path debris {deb}");
        var floor = new GameObject("Floor").transform; floor.SetParent(debris.parent, false);
        PlaceFloor(floor, hero, taken, rnd, log);
        // keep the viewer's immediate surroundings clean: no big rocks, sticks or logs right at the camera's feet
        int cleared = 0;
        foreach (var grp in new[] { streamRocks, floor })
            foreach (var t in grp.Cast<Transform>().ToList())
            {
                var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float d = new Vector2(b.center.x, b.center.z).magnitude, size = Mathf.Max(b.size.x, b.size.z);
                bool stick = t.name.StartsWith("PF_Twig") || t.name.StartsWith("PF_Log");
                if ((stick && d < 5f) || (size > 1.6f && d < 3.6f)) { UnityEngine.Object.DestroyImmediate(t.gameObject); cleared++; }
            }
        log.Add($"viewer surroundings cleared {cleared}");
    }

    // ---------------------------------------------------------------- sky
    static void BuildSky(Transform parent, List<string> log)
    {
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/Lighting/M_Sky.mat");
        if (!sky) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, "Assets/Settings/Lighting/M_Sky.mat"); }
        sky.shader = Shader.Find("Skybox/Procedural");
        sky.SetFloat("_SunDisk", 2);
        sky.SetFloat("_SunSize", 0.035f); sky.SetFloat("_SunSizeConvergence", 6f);
        sky.SetFloat("_AtmosphereThickness", 1.0f);
        sky.SetColor("_SkyTint", new Color(0.52f, 0.56f, 0.64f));
        sky.SetColor("_GroundColor", new Color(0.55f, 0.62f, 0.62f));
        sky.SetFloat("_Exposure", 1.25f);
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/VFX/Textures/T_Cloud_BC.png");
        var cm = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/VFX/Materials/M_Cloud.mat");
        if (!cm) { cm = new Material(Shader.Find("TerraRemastered/CloudCard")); AssetDatabase.CreateAsset(cm, "Assets/Art/VFX/Materials/M_Cloud.mat"); }
        cm.shader = Shader.Find("TerraRemastered/CloudCard");
        cm.SetTexture("_MainTex", tex); cm.SetFloat("_AlphaGain", 1.7f);
        cm.SetColor("_HazeColor", new Color(0.78f, 0.86f, 0.95f)); cm.SetFloat("_Haze", 0.12f);
        EditorUtility.SetDirty(cm);
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        float aspect = tex ? tex.width / (float)tex.height : 16f / 9f;
        // (azimuth deg, elevation deg, distance, width, flipX)
        foreach (var (az, el, dist, w, flip) in new[] { (-3f, 17f, 4200f, 2600f, false), (-30f, 13f, 4600f, 1500f, true), (40f, 17f, 4800f, 1300f, false), (-62f, 8f, 4400f, 1600f, false), (68f, 10f, 4500f, 1500f, true), (-8f, 22f, 5000f, 1100f, true) })
        {
            var dir = Quaternion.Euler(-el, az, 0) * Vector3.forward;
            var go = new GameObject($"Cloud_{az:F0}", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = dir * dist;
            go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            go.transform.localScale = new Vector3(flip ? -w : w, w / aspect, 1);
            go.GetComponent<MeshFilter>().sharedMesh = quad;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = cm; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
        log.Add("sky: procedural + 6 cloud cards");
    }

    static void BuildLightShafts(Transform parent)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/VFX/Materials/M_LightShaft.mat");
        if (!mat) { mat = new Material(Shader.Find("TerraRemastered/LightShaft")); AssetDatabase.CreateAsset(mat, "Assets/Art/VFX/Materials/M_LightShaft.mat"); }
        mat.shader = Shader.Find("TerraRemastered/LightShaft");
        mat.SetColor("_Color", new Color(1f, 0.93f, 0.72f)); mat.SetFloat("_Intensity", 0.15f); mat.SetFloat("_EdgeFade", 1.2f);
        EditorUtility.SetDirty(mat);
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var toSun = -(Quaternion.Euler(SunEuler) * Vector3.forward);
        foreach (var (x, y, z, w, len) in new[] { (-5.5f, 6f, 13f, 2.2f, 22f), (-3f, 7f, 22f, 1.6f, 20f), (-8.5f, 8f, 18f, 3f, 26f), (-1.5f, 8f, 31f, 2.4f, 22f), (-10f, 6f, 9f, 1.8f, 18f), (-6.5f, 9f, 27f, 3.2f, 26f) })
        {
            var go = new GameObject("Shaft", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            var c = new Vector3(x, Ground(x, z) + y, z);
            var fwd = Vector3.ProjectOnPlane(-c + new Vector3(0, EyeHeight, 0), toSun).normalized;
            go.transform.SetPositionAndRotation(c, Quaternion.LookRotation(fwd, toSun));
            go.transform.localScale = new Vector3(w, len, 1);
            go.GetComponent<MeshFilter>().sharedMesh = quad;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
    }

    static void BuildMotes(Transform parent)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/VFX/Materials/M_Mote.mat");
        if (!mat) { mat = new Material(Shader.Find("TerraRemastered/Mote")); AssetDatabase.CreateAsset(mat, "Assets/Art/VFX/Materials/M_Mote.mat"); }
        mat.shader = Shader.Find("TerraRemastered/Mote"); mat.SetFloat("_Intensity", 1.4f); EditorUtility.SetDirty(mat);
        foreach (var (c, size, rate) in new[] { (new Vector3(-5.5f, 3.5f, 14f), new Vector3(9f, 6f, 20f), 40f) })
        {
            var go = new GameObject("Motes", typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            go.transform.position = c;
            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.7f, 0.5f), new Color(1f, 1f, 0.85f, 0.9f));
            main.maxParticles = 600; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = size;
            var no = ps.noise; no.enabled = true; no.strength = 0.08f; no.frequency = 0.25f; no.scrollSpeed = 0.1f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                                             new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(1, 0.75f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
    }

    static void BuildAudio(Transform parent, List<string> log)
    {
        var jungle = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/Audio/Ambience/A_JungleLoop.wav");
        var stream = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/Audio/Ambience/A_StreamLoop.wav");
        if (!jungle || !stream) { log.Add("WARN audio clips missing (run sources/audio/gen_audio.py)"); return; }
        var amb = new GameObject("Ambience_Jungle", typeof(AudioSource)).GetComponent<AudioSource>();
        amb.transform.SetParent(parent, false);
        amb.clip = jungle; amb.loop = true; amb.playOnAwake = true; amb.spatialBlend = 0f; amb.volume = 0.35f;
        int k = 0;
        foreach (var z in new[] { 3.5f, 11.5f, 19f, 30f })
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < streamS.Count; i++) { float d = Mathf.Abs(streamS[i].y - z); if (d < bd) { bd = d; best = i; } }
            var p = streamS[best];
            var src = new GameObject($"Stream_{k}", typeof(AudioSource)).GetComponent<AudioSource>();
            src.transform.SetParent(parent, false);
            src.transform.position = new Vector3(p.x, streamLevel[best] + 0.3f, p.y);
            src.clip = stream; src.loop = true; src.playOnAwake = true; src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic; src.minDistance = 2.5f; src.maxDistance = 40f;
            src.volume = k == 0 ? 0.55f : 0.45f; src.pitch = 0.92f + 0.05f * k; src.dopplerLevel = 0;
            k++;
        }
        log.Add($"audio: jungle ambience + {k} stream sources");
    }

    // ---------------------------------------------------------------- lighting and post
    // from upper left, slightly behind the left trees; 33 deg (was 38): longer, more graphic shadows like the references
    static readonly Vector3 SunEuler = new(33f, 112f, 0f);

    static Light BuildLighting(Transform root, List<string> log)
    {
        var go = new GameObject("Sun", typeof(Light));
        go.transform.SetParent(root, false);
        go.transform.rotation = Quaternion.Euler(SunEuler);
        var l = go.GetComponent<Light>();
        l.type = LightType.Directional;
        l.color = new Color(1f, 0.88f, 0.72f);      // warm key against a cool sky fill: facets separate by hue too
        l.intensity = 2.6f;
        l.shadows = LightShadows.Soft;
        l.shadowStrength = 0.75f;   // 0.85 made the canopy dapple read as hard dark spots
        l.shadowBias = 1.2f; l.shadowNormalBias = 0.0f;   // normal bias opens cracks between flat-shaded faces
        RenderSettings.sun = l;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.60f, 0.70f, 0.86f) * 1.05f;
        RenderSettings.ambientEquatorColor = new Color(0.52f, 0.62f, 0.54f);
        RenderSettings.ambientGroundColor = new Color(0.40f, 0.32f, 0.22f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.00058f;
        RenderSettings.fogColor = new Color(0.74f, 0.84f, 0.92f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        DynamicGI.UpdateEnvironment();
        log.Add("sun + trilight ambient + exponential fog 0.00055");
        return l;
    }

    static void BuildPost(Transform root)
    {
        const string path = "Assets/Settings/PostProcessing/PP_Rainforest.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
        T Get<T>() where T : VolumeComponent { if (!profile.TryGet<T>(out var c)) { c = profile.Add<T>(true); AssetDatabase.AddObjectToAsset(c, profile); } c.active = true; return c; }
        var tm = Get<Tonemapping>(); tm.mode.Override(TonemappingMode.Neutral);
        var ca = Get<ColorAdjustments>(); ca.postExposure.Override(0.15f); ca.contrast.Override(16f); ca.saturation.Override(6f);
        var wb = Get<WhiteBalance>(); wb.temperature.Override(4f); wb.tint.Override(-2f);
        // warm highlights / cool shadows (references): split toning instead of more saturation
        var st = Get<SplitToning>(); st.shadows.Override(new Color(0.42f, 0.50f, 0.72f)); st.highlights.Override(new Color(1f, 0.86f, 0.68f)); st.balance.Override(-15f);
        var bl = Get<Bloom>(); bl.intensity.Override(0.55f); bl.threshold.Override(0.95f); bl.scatter.Override(0.65f); bl.tint.Override(new Color(1f, 0.95f, 0.85f));
        var vg = Get<Vignette>(); vg.intensity.Override(0.22f); vg.smoothness.Override(0.45f);
        var lgg = Get<LiftGammaGain>(); lgg.lift.Override(new Vector4(0.98f, 1.0f, 1.04f, 0.0f));
        EditorUtility.SetDirty(profile);
        var go = new GameObject("Global Volume", typeof(Volume));
        go.transform.SetParent(root, false);
        var v = go.GetComponent<Volume>(); v.isGlobal = true; v.sharedProfile = profile; v.priority = 0;
    }

    static Camera BuildCamera()
    {
        var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(FixedLookCamera));
        go.tag = "MainCamera";
        go.transform.SetPositionAndRotation(new Vector3(0, Ground(0, 0) + EyeHeight, 0), Quaternion.Euler(5f, 5f, 0));
        var cam = go.GetComponent<Camera>();
        cam.fieldOfView = 64f; cam.nearClipPlane = 0.08f; cam.farClipPlane = 6500f;
        cam.clearFlags = CameraClearFlags.Skybox;
        var cd = go.AddComponent<UniversalAdditionalCameraData>();
        cd.renderPostProcessing = true;
        cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cd.antialiasingQuality = AntialiasingQuality.High;
        cd.renderShadows = true;
        return cam;
    }
}
