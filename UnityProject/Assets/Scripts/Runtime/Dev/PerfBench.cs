using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Performance benchmark for PLAYER BUILDS (the editor is noisy and measured ~2x faster than the build in the source
/// project, and it missed pixel cost). Inactive unless the build starts with `-perfbench`, so it costs nothing in play.
///
///   MyGame.app/Contents/MacOS/MyGame -perfbench -perfbench-out bench.txt
///       [-perfbench-views a,b] [-perfbench-samples 240] [-perfbench-modes base,noshadow,nossao+scale:0.75]
///
/// Views: the children of a scene GameObject named "PerfViews" (position + rotation; name = view name). Add markers at
/// the long sight lines and the busiest spots, and keep them fixed so runs stay comparable.
/// Per view and mode it records the median CPU main, CPU frame and GPU frame time (FrameTimingManager: turn on
/// Player Settings > Frame Timing Stats). Modes are interleaved per view, so thermal drift (fanless laptops throttle
/// +-20 % over a long run) hits every mode alike: compare modes within one run, not across runs.
/// Mode tokens (join with '+'): base, noshadow, hard, nossao, nopost, noparticles, noterrain, nodetails,
/// feature:<RendererFeatureType>, hide:<text in root/child group name>, scale:<f>, sd:<m>, shres:<px>, lodbias:<f>,
/// off:<group name> (deactivate a top-level group: scripts too), noscript:<MonoBehaviour type> (disable every instance).
public class PerfBench : MonoBehaviour
{
    const int Warmup = 90;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-perfbench") < 0) return;
        new GameObject("PerfBench").AddComponent<PerfBench>();
    }

    static string Arg(string key, string fallback)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
        return fallback;
    }

    static float Median(List<float> v) { v.Sort(); return v.Count == 0 ? 0 : v[v.Count / 2]; }

    UniversalRenderPipelineAsset urp; float scale0, shadowDist0, lodBias0; int shadowRes0;
    readonly List<ScriptableRendererFeature> features = new(); readonly List<bool> featureOn0 = new();
    Light sun; LightShadows sunShadows0; UniversalAdditionalCameraData camData; bool post0;
    ParticleSystemRenderer[] particles; Terrain[] terrains; MeshRenderer[] all; string[] group;

    void CaptureState(Camera cam)
    {
        urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        scale0 = urp.renderScale; shadowDist0 = urp.shadowDistance; shadowRes0 = urp.mainLightShadowmapResolution; lodBias0 = QualitySettings.lodBias;
        var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(urp) as ScriptableRendererData[];
        if (list != null) foreach (var d in list) if (d) foreach (var f in d.rendererFeatures) if (f) { features.Add(f); featureOn0.Add(f.isActive); }
        foreach (var l in FindObjectsByType<Light>()) if (l.type == LightType.Directional && l.shadows != LightShadows.None) sun = l;
        if (sun) sunShadows0 = sun.shadows;
        camData = cam.GetComponent<UniversalAdditionalCameraData>(); post0 = camData && camData.renderPostProcessing;
        particles = FindObjectsByType<ParticleSystemRenderer>();
        terrains = Terrain.activeTerrains;
        all = FindObjectsByType<MeshRenderer>(); group = new string[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i].transform; while (t.parent && t.parent.parent) t = t.parent;
            group[i] = t.parent ? t.parent.name + "/" + t.name : t.name;
        }
    }

    void Apply(string mode)
    {
        urp.renderScale = scale0; urp.shadowDistance = shadowDist0; urp.mainLightShadowmapResolution = shadowRes0; QualitySettings.lodBias = lodBias0;
        for (int i = 0; i < features.Count; i++) features[i].SetActive(featureOn0[i]);
        if (sun) sun.shadows = sunShadows0;
        if (camData) camData.renderPostProcessing = post0;
        foreach (var p in particles) p.enabled = true;
        foreach (var r in all) r.forceRenderingOff = false;
        foreach (var t in terrains) { t.drawHeightmap = true; t.drawTreesAndFoliage = true; }
        foreach (var g in offGroups) if (g) g.SetActive(true);
        offGroups.Clear();
        foreach (var b in offScripts) if (b) b.enabled = true;
        offScripts.Clear();
        foreach (var m in mode.Split('+')) ApplyOne(m);
    }

    static float Num(string m, string key, float fallback) =>
        m.StartsWith(key + ":") ? float.Parse(m.Substring(key.Length + 1), CultureInfo.InvariantCulture) : fallback;

    void ApplyOne(string m)
    {
        urp.renderScale = Num(m, "scale", urp.renderScale);
        urp.shadowDistance = Num(m, "sd", urp.shadowDistance);
        urp.mainLightShadowmapResolution = (int)Num(m, "shres", urp.mainLightShadowmapResolution);
        QualitySettings.lodBias = Num(m, "lodbias", QualitySettings.lodBias);
        if (sun && m == "noshadow") sun.shadows = LightShadows.None;
        if (sun && m == "hard" && sun.shadows != LightShadows.None) sun.shadows = LightShadows.Hard;
        if (camData && m == "nopost") camData.renderPostProcessing = false;
        foreach (var f in features)
        {
            var n = f.GetType().Name;
            if ((m == "nossao" && n == "ScreenSpaceAmbientOcclusion") || m == "feature:" + n) f.SetActive(false);
        }
        if (m == "noparticles") foreach (var p in particles) p.enabled = false;
        var hide = m.StartsWith("hide:") ? m.Substring(5) : null;
        if (hide != null) for (int i = 0; i < all.Length; i++) if (group[i].Contains(hide)) all[i].forceRenderingOff = true;
        foreach (var t in terrains) { if (m == "noterrain") t.drawHeightmap = false; if (m == "nodetails") t.drawTreesAndFoliage = false; }
        if (m.StartsWith("off:"))
            foreach (var r in FindObjectsByType<Transform>()) if (r.parent && r.name == m.Substring(4) && r.parent.parent == null) { r.gameObject.SetActive(false); offGroups.Add(r.gameObject); }
        if (m.StartsWith("noscript:"))
            foreach (var b in FindObjectsByType<MonoBehaviour>()) if (b.enabled && b.GetType().Name == m.Substring(9)) { b.enabled = false; offScripts.Add(b); }
    }
    readonly List<GameObject> offGroups = new(); readonly List<MonoBehaviour> offScripts = new();

    IEnumerator Start()
    {
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        yield return null; yield return null;                       // let player controllers take the camera first, then free it
        var cam = Camera.main;
        foreach (var b in FindObjectsByType<MonoBehaviour>())
        {
            var n = b.GetType().Name;
            if (b != this && (n.EndsWith("Controller") || n == "FixedLookCamera" || n == "CinemachineBrain" || n.StartsWith("Cinemachine"))) b.enabled = false;
        }
        foreach (var c in FindObjectsByType<CharacterController>()) c.enabled = false;
        cam.transform.SetParent(null, false);
        var markers = GameObject.Find("PerfViews");
        if (!markers) { Debug.LogError("PerfBench: no 'PerfViews' GameObject with view markers in the scene"); Application.Quit(1); yield break; }
        CaptureState(cam);
        var modes = Arg("-perfbench-modes", "base").Split(',');
        var only = Arg("-perfbench-views", "");
        int samples = int.Parse(Arg("-perfbench-samples", "240"), CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.AppendLine($"# {System.DateTime.Now:yyyy-MM-dd HH:mm} {SystemInfo.graphicsDeviceName} {Screen.width}x{Screen.height} " +
                      $"quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} (median of {samples} frames, ms)");
        sb.AppendLine("view             mode                 cpu_main  cpu_frame    gpu   fps   wall  gpu_n");
        var sumGpu = new float[modes.Length]; var sumCpu = new float[modes.Length]; int nViews = 0;
        foreach (Transform v in markers.transform)
        {
            if (only.Length > 0 && System.Array.IndexOf(only.Split(','), v.name) < 0) continue;
            nViews++;
            cam.transform.SetPositionAndRotation(v.position, v.rotation);
            for (int k = 0; k < modes.Length; k++)
            {
                Apply(modes[k]);
                for (int i = 0; i < (k == 0 ? Warmup : Warmup / 2); i++) yield return null;
                var r = new float[5];
                yield return Sample(samples, r);
                if (k == 0) Shot(v.name);
                float mm = r[0], f = r[1], g = r[2], w = r[3]; int gpuN = (int)r[4];
                sumGpu[k] += g; sumCpu[k] += f;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16} {1,-20} {2,8:F2} {3,10:F2} {4,6:F2} {5,5:F0} {6,6:F2} {7,6}", v.name, modes[k], mm, f, g, f > 0 ? 1000f / f : 0, w, gpuN));
            }
            Apply("base");
        }
        // Showcase rooms (the encounter drives the camera): skip with -perfbench-nowhiteroom.
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-perfbench-nowhiteroom") < 0)
            foreach (var enc in FindObjectsByType<TigerEncounter>())
            {
                if (enc.SpeciesId != Arg("-perfbench-animal", "tiger")) continue;
                Apply("base");
                enc.enabled = true; enc.TestStart();
                for (float t = 0; t < 20f && enc.StateName != "Showcase"; t += Time.unscaledDeltaTime) yield return null;
                if (enc.StateName != "Showcase") { sb.AppendLine($"{enc.name}: showcase not reached ({enc.StateName})"); continue; }
                for (int shot = 0; shot < 2; shot++)
                {
                    if (shot == 1) enc.TestFocus(0);                      // first hotspot (eyes): the fur close-up
                    for (int i = 0; i < 150; i++) yield return null;      // camera flight + warm-up
                    var r = new float[5];
                    yield return Sample(samples, r);
                    Shot(shot == 0 ? "room" : "closeup");
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16} {1,-20} {2,8:F2} {3,10:F2} {4,6:F2} {5,5:F0} {6,6:F2} {7,6}",
                        shot == 0 ? "room" : "closeup",
                        "base", r[0], r[1], r[2], r[1] > 0 ? 1000f / r[1] : 0, r[3], (int)r[4]));
                }
            }
        for (int k = 0; k < modes.Length; k++)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "mean             {0,-20} {1,8} {2,10:F2} {3,6:F2}", modes[k], "", sumCpu[k] / Mathf.Max(nViews, 1), sumGpu[k] / Mathf.Max(nViews, 1)));
        var outPath = Arg("-perfbench-out", Application.persistentDataPath + "/perfbench.txt");
        System.IO.File.AppendAllText(outPath, sb.ToString());
        Debug.Log("PerfBench\n" + sb);
        Application.Quit();
    }

    // -perfbench-shots <dir>: one screenshot per view (base mode), to check what was measured.
    static void Shot(string view)
    {
        var dir = Arg("-perfbench-shots", "");
        if (dir.Length > 0) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, view + ".png"));
    }

    static IEnumerator Sample(int samples, float[] res)
    {
        var timings = new FrameTiming[8];
        var main = new List<float>(); var frame = new List<float>(); var gpu = new List<float>(); var wall = new List<float>();
        for (int i = 0; i < samples; i++)
        {
            yield return null;
            wall.Add(Time.unscaledDeltaTime * 1000f);
            FrameTimingManager.CaptureFrameTimings();
            uint n = FrameTimingManager.GetLatestTimings((uint)timings.Length, timings);
            if (n == 0) continue;
            main.Add((float)timings[0].cpuMainThreadFrameTime); frame.Add((float)timings[0].cpuFrameTime);
            // Metal delivers GPU times a few frames late (0 on the newest frames): take the newest frame that has one.
            for (int j = 0; j < n; j++) if (timings[j].gpuFrameTime > 0) { gpu.Add((float)timings[j].gpuFrameTime); break; }
        }
        res[0] = Median(main); res[1] = Median(frame); res[2] = Median(gpu); res[3] = Median(wall); res[4] = gpu.Count;
    }
}
