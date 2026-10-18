using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// Baked lighting for the Rainforest scene (Progressive GPU, Shadowmask / Distance Shadowmask quality setting):
///   - sun Mixed: realtime direct light and shadows inside the URP shadow distance, baked sun shadows beyond it
///   - lightmapped (ReceiveGI.Lightmaps): the generated terrain, ridges and mountains (grid-index UV2 from the builder)
///   - everything else probe-lit; plants, trees and rocks contribute occlusion + bounce (ContributeGI on LOD0)
///   - light probes: dense rings around the fixed viewer, coarser out to the hills
/// Bake afterwards with unity/bake_lighting.cs (tools/rebuild_scene.sh does both). Any rebuild needs a rebake.
public static partial class RainforestBuilder
{
    const string LightingSettingsPath = "Assets/Settings/Lighting/LS_Rainforest.lighting";
    const float TexelsPerUnit = 1f;          // base resolution; each lightmapped mesh is scaled to its own atlas size
    const int MaxAtlas = 2048;               // > 2048 EXRs get too big for git (LESSONS)

    // lightmapped mesh -> target atlas edge in texels (terrain: ~13 texels/m at the viewer thanks to grid UVs)
    static readonly Dictionary<string, int> LightmapTarget = new() { ["Terrain_Mesh"] = 2048, ["Ridges_Mesh"] = 1024, ["Mountains_Mesh"] = 1024 };

    // probe rings around the viewer: (outer radius, spacing, heights above ground)
    static readonly (float r, float step, float[] h)[] ProbeRings =
    {
        (45f, 2.5f, new[] { 0.4f, 1.6f, 3.5f, 8f, 16f }),
        (160f, 7f, new[] { 0.8f, 4f, 12f, 25f }),
        (700f, 30f, new[] { 2f, 15f, 40f }),
    };

    static void ConfigureBakedLighting(Transform root, Light sun, List<string> log)
    {
        // settings asset (re-created values every build, so the bake is reproducible)
        var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
        if (!ls) { ls = new LightingSettings { name = "LS_Rainforest" }; AssetDatabase.CreateAsset(ls, LightingSettingsPath); }
        ls.bakedGI = true; ls.realtimeGI = false; ls.autoGenerate = false;
        ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        ls.mixedBakeMode = MixedLightingMode.Shadowmask;
        ls.lightmapResolution = TexelsPerUnit; ls.lightmapPadding = 2; ls.lightmapMaxSize = MaxAtlas;
        ls.directionalityMode = LightmapsMode.CombinedDirectional;   // facet normals still shape the baked bounce
        ls.lightmapCompression = LightmapCompression.HighQuality;
        ls.ao = true; ls.aoMaxDistance = 2.5f; ls.aoExponentIndirect = 1.2f; ls.aoExponentDirect = 0f;
        ls.directSampleCount = 64; ls.indirectSampleCount = 512; ls.environmentSampleCount = 512;
        ls.maxBounces = 3; ls.lightProbeSampleCountMultiplier = 2f;
        ls.filteringMode = LightingSettings.FilterMode.Auto;
        ls.albedoBoost = 1f; ls.indirectScale = 1f;
        EditorUtility.SetDirty(ls);
        Lightmapping.lightingSettings = ls;

        // leaves and crowns are solid blobs: let part of the light through them in the bake, or the jungle floor and
        // the trunks under the canopy go nearly black (probes 0.05 vs 0.35 sky ambient with opaque crowns)
        var trans = LeafTransmissionTexture();
        int leafMats = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { FoliageMatFolder, MatFolder }))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (!m || m.shader.name != "TerraRemastered/LowPolyFoliage") continue;
            bool wood = m.name is "MI_Bark" or "MI_PalmTrunk";
            // crowns: big soft gaps (T_Canopy_LMTransmission, crown UVs 6 m/unit) -> dappled baked light on the floor
            var gaps = AssetDatabase.LoadAssetAtPath<Texture2D>(ProjectPaths.Find("T_Canopy_LMTransmission.png") ?? "");
            m.SetTexture("_TransparencyLM", wood ? null : m.name == "MI_Canopy" && gaps ? gaps : trans); EditorUtility.SetDirty(m);
            if (!wood) leafMats++;
        }
        log.Add($"  bake transmission {LeafTransmission:P0} on {leafMats} leaf/crown materials");

        sun.lightmapBakeType = LightmapBakeType.Mixed;
        sun.shadowAngle = 2.0f;                                      // wide baked penumbra (beyond the realtime distance), matches SoftShadows.hlsl

        int lm = 0, contrib = 0, castOn = 0;
        // lightmapped generated meshes
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!LightmapTarget.TryGetValue(mr.name, out int target)) continue;
            var mesh = mr.GetComponent<MeshFilter>().sharedMesh;
            float area = 0; var v = mesh.vertices;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (int k = 0; k < t.Length; k += 3) area += Vector3.Cross(v[t[k + 1]] - v[t[k]], v[t[k + 2]] - v[t[k]]).magnitude * 0.5f;
            }
            Flag(mr.gameObject, StaticEditorFlags.ContributeGI);
            mr.receiveGI = ReceiveGI.Lightmaps;
            mr.scaleInLightmap = Mathf.Min(1f, target * 0.97f / (Mathf.Sqrt(area) * TexelsPerUnit));
            mr.stitchLightmapSeams = true;
            if (mr.shadowCastingMode == ShadowCastingMode.Off) { mr.shadowCastingMode = ShadowCastingMode.On; castOn++; }   // far: baked shadows only
            lm++;
            log.Add($"  lightmapped {mr.name}: area {area / 1e6f:F2} km2, scale {mr.scaleInLightmap:F3} -> ~{target}px");
        }

        // probe-lit contributors: LOD0 of plants, trees and rocks that cast shadows (no grass); far forest casts
        // too, it lies outside the realtime shadow cascades, so it only adds baked shadows on the hills
        foreach (var group in new[] { "Hero", "Jungle", "StreamRocks", "Forest", "PathDebris", "Floor" })
        {
            var g = root.Find(group); if (!g) continue;
            foreach (Transform inst in g)
            {
                if (inst.name.StartsWith("PF_Grass") || inst.name.StartsWith("Grass")) continue;
                var lodg = inst.GetComponentInChildren<LODGroup>();
                var rs = lodg ? lodg.GetLODs()[0].renderers.Where(r => r).ToArray() : inst.GetComponentsInChildren<Renderer>();
                foreach (var r in rs)
                {
                    if (group == "Forest" && r.shadowCastingMode == ShadowCastingMode.Off && Vector3.Distance(r.bounds.center, Vector3.zero) > 150f)
                    { r.shadowCastingMode = ShadowCastingMode.On; castOn++; }
                    if (r.shadowCastingMode == ShadowCastingMode.Off) continue;
                    Flag(r.gameObject, StaticEditorFlags.ContributeGI);
                    if (r is MeshRenderer m) m.receiveGI = ReceiveGI.LightProbes;
                    contrib++;
                }
            }
        }

        // water and sky cards keep the global ambient (one probe sample for a 600 m lake would tint it oddly)
        foreach (var group in new[] { "Lake", "Stream", "Sky", "LightShafts", "SunMotes" })
            if (root.Find(group) is { } g)
                foreach (var r in g.GetComponentsInChildren<Renderer>(true)) r.lightProbeUsage = LightProbeUsage.Off;

        int probes = BuildLightProbes(root);
        log.Add($"baked lighting: {lm} lightmapped, {contrib} probe-lit contributors, {castOn} renderers set to cast (far/bake), {probes} light probes ({probesDropped} dropped inside geometry)");
    }

    const float LeafTransmission = 0.5f;
    static Texture2D LeafTransmissionTexture()
    {
        var path = ProjectPaths.Tex("T_Foliage_LMTransmission");
        var t = new Texture2D(4, 4, TextureFormat.RGB24, false);
        var c = new Color(LeafTransmission, LeafTransmission, LeafTransmission);
        t.SetPixels(Enumerable.Repeat(c, 16).ToArray());
        var png = t.EncodeToPNG(); UnityEngine.Object.DestroyImmediate(t);
        var full = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path);
        if (!System.IO.File.Exists(full) || !System.IO.File.ReadAllBytes(full).SequenceEqual(png))
        {
            System.IO.File.WriteAllBytes(full, png);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti) { ti.sRGBTexture = false; ti.mipmapEnabled = false; ti.SaveAndReimport(); }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void Flag(GameObject go, StaticEditorFlags add) =>
        GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(go) | add);

    static int probesDropped;
    static int BuildLightProbes(Transform root)
    {
        var terrain = root.GetComponentsInChildren<MeshCollider>().FirstOrDefault(c => c.name == "Terrain_Mesh");
        float GroundY(float x, float z) =>
            terrain && terrain.Raycast(new Ray(new Vector3(x, 3000, z), Vector3.down), out var hit, 6000f) ? hit.point.y : Height(x, z);
        // probes inside closed meshes (trunks, crowns, rocks) bake black and darken every object that samples them:
        // temporary mesh colliders on the GI contributors + a backface ray test drop them
        var temp = new List<MeshCollider>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            if (!mf.sharedMesh || mf.GetComponent<Collider>()) continue;
            if ((GameObjectUtility.GetStaticEditorFlags(mf.gameObject) & StaticEditorFlags.ContributeGI) == 0) continue;
            var c = mf.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh; temp.Add(c);
        }
        bool oldBack = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
        Physics.SyncTransforms();
        var dirs = new List<Vector3>();
        for (int k = 0; k < 14; k++)   // 6 axes + 8 diagonals
            dirs.Add(k < 6 ? new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back }[k]
                           : new Vector3((k & 1) == 0 ? 1 : -1, (k & 2) == 0 ? 1 : -1, (k & 4) == 0 ? 1 : -1).normalized);
        bool Inside(Vector3 p)
        {
            int back = 0;
            foreach (var d in dirs)
                if (Physics.Raycast(p, d, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore) && Vector3.Dot(hit.normal, d) > 0) back++;
            return back >= 6;
        }
        int dropped = 0;
        var pts = new List<Vector3>();
        float inner = 0;
        foreach (var (r, step, hs) in ProbeRings)
        {
            for (float z = -r; z <= r; z += step)
            {
                float off = Mathf.Repeat(z / step, 2f) >= 1f ? step * 0.5f : 0f;   // staggered rows
                for (float x = -r + off; x <= r; x += step)
                {
                    float d = Mathf.Sqrt(x * x + z * z);
                    if (d > r || d < inner) continue;
                    float gy = GroundY(x, z);
                    float baseY = Mathf.Max(gy, LakeLevel);                           // over the lake: from the water surface
                    foreach (var h in hs)
                    {
                        var p = new Vector3(x, baseY + h, z);
                        if (Physics.CheckSphere(p, 0.15f, ~0, QueryTriggerInteraction.Ignore) || Inside(p)) { dropped++; continue; }
                        pts.Add(p);
                    }
                }
            }
            inner = r;
        }
        Physics.queriesHitBackfaces = oldBack;
        foreach (var c in temp) UnityEngine.Object.DestroyImmediate(c);
        Physics.SyncTransforms();
        probesDropped = dropped;
        var go = new GameObject("LightProbes", typeof(LightProbeGroup));
        go.transform.SetParent(root, false);
        go.GetComponent<LightProbeGroup>().probePositions = pts.ToArray();
        return pts.Count;
    }
}
