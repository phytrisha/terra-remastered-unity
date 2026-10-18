using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

/// Forest-floor detail (blender/assets/GroundDetail) and the white tiger on the path.
///   roots at the buttress trees, mossy logs + mushrooms, leaf litter, twigs, seedling cover, pebbles on the path edges.
/// Small pieces cast no shadows and are not GI contributors; logs and roots are (see RainforestBuilder.Lighting).
public static partial class RainforestBuilder
{
    // tiger: on the path ~7 m ahead, facing the viewer (encounter: TigerEncounter runs/pounces from here).
    // Scaled 1.3x: the model is 2.1 m nose-tail, a Bengal tiger ~2.7 m (it read too small in the jungle at 1x)
    static readonly Vector3 TigerPos = new(-1.0f, 0, 7.0f);
    const float TigerScale = 1.3f;
    static float TigerYaw => Mathf.Atan2(-TigerPos.x, -TigerPos.z) * Mathf.Rad2Deg;
    const string TigerController = "Assets/Art/Characters/WhiteTiger/AC_WhiteTiger.controller";

    static MeshCollider terrainCol;
    static bool OnTerrain(float x, float z, out RaycastHit hit)
    {
        if (!terrainCol) terrainCol = GameObject.Find("Rainforest/Terrain/Terrain_Mesh")?.GetComponent<MeshCollider>();
        hit = default;
        return terrainCol && terrainCol.Raycast(new Ray(new Vector3(x, 500, z), Vector3.down), out hit, 1000f);
    }

    /// Lay a flat piece on the terrain: yaw, then tilt to the ground normal.
    static GameObject Lay(Transform parent, string asset, float x, float z, float yaw, float scale, float sink, bool shadows)
    {
        if (!OnTerrain(x, z, out var hit)) return null;
        var go = Put(parent, asset, x, z, yaw, scale, sink: 0, y: hit.point.y, shadows: shadows);
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0, yaw, 0);
        go.transform.position = hit.point - hit.normal * sink;
        return go;
    }

    static void FloorMaterials()
    {
        // created by the importer as plain URP Lit (smoothness 0.5): make them matte like the rest of the palette
        foreach (var n in new[] { "MI_LeafDry", "MI_LeafDead", "MI_LeafLitterYellow", "MI_Moss", "MI_MushroomCap", "MI_MushroomPale", "MI_MushroomStem", "M_WhiteTiger" })
        {
            var p = ProjectPaths.Find(n + ".mat"); if (p == null) continue;
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (n != "M_WhiteTiger") { LowPolyLitSetup(m, m.GetColor("_BaseColor"), n.StartsWith("MI_Mushroom") ? 0.3f : 0.06f); if (n.StartsWith("MI_Leaf")) m.SetFloat("_Cull", 0f); continue; }
            m.SetFloat("_Smoothness", n.StartsWith("MI_Mushroom") ? 0.3f : n == "M_WhiteTiger" ? 0.18f : 0.08f);
            m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            if (!n.StartsWith("MI_Mushroom") && n != "M_WhiteTiger") { m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
            if (n.StartsWith("MI_Leaf")) { m.SetFloat("_Cull", 0f); m.doubleSidedGI = true; }   // thin leaves: both sides
            EditorUtility.SetDirty(m);
        }
    }

    static void PlaceFloor(Transform floor, Transform hero, List<(Vector2 p, float r)> taken, Random rnd, List<string> log)
    {
        FloorMaterials();
        var tiger2 = new Vector2(TigerPos.x, TigerPos.z);
        int roots = 0, logs = 0, mush = 0, litter = 0, twigs = 0, cover = 0, pebbles = 0;

        // surface roots fanning out from every buttress tree (none across the path)
        foreach (Transform t in hero)
        {
            if (!t.name.StartsWith("PF_ButtressTree")) continue;
            float s = t.localScale.x;
            for (int k = 0; k < 4; k++)
            {
                float yaw = t.eulerAngles.y + k * 90 + Rf(rnd, -30, 30);
                var dir = Quaternion.Euler(0, yaw, 0) * Vector3.right;       // roots run along local +X
                var tip = new Vector2(t.position.x, t.position.z) + new Vector2(dir.x, dir.z) * 4.5f * s;
                if (PolyDist(tip, PathPts) < 1.6f || StreamDist(tip, out _) < 1.5f) continue;
                var start = new Vector2(t.position.x, t.position.z) + new Vector2(dir.x, dir.z) * 1.1f * s;
                if (!OnTerrain(start.x, start.y, out var h)) continue;
                var go = Put(floor, k % 2 == 0 ? "Roots_A" : "Roots_B", start.x, start.y, yaw, s * Rf(rnd, 0.8f, 1.05f), y: h.point.y - 0.05f, sink: 0);
                go.transform.rotation = Quaternion.Euler(0, yaw + 180, 0);      // roots run along Blender +X = Unity local -X
                roots++;
                if (rnd.NextDouble() < 0.5)
                {
                    var m = start + new Vector2(dir.x, dir.z) * Rf(rnd, 0.5f, 1.5f) + new Vector2(Rf(rnd, -.4f, .4f), Rf(rnd, -.4f, .4f));
                    if (Lay(floor, rnd.NextDouble() < 0.6 ? "Mushroom_A" : "Mushroom_B", m.x, m.y, Rf(rnd, 0, 360), Rf(rnd, 0.9f, 1.3f), 0.01f, false)) mush++;
                }
            }
        }

        // mossy logs off the path, tilted to the slope, with mushrooms at their sides
        for (int tries = 0; tries < 2500 && logs < 6; tries++)
        {
            float x = Rf(rnd, -22, 18), z = Rf(rnd, -20, 32);
            var p = new Vector2(x, z);
            if (p.magnitude < 5f || PolyDist(p, PathPts) < 3.2f || StreamDist(p, out _) < 3f || !Free(p, taken, 0.8f)) continue;   // jungle plants may overlap a log
            if (z > 0 && Mathf.Abs(x + 1) < 3 + 0.3f * z && z > 14) continue;  // keep the lake view open
            float yaw = Rf(rnd, 0, 360);
            var ax = Quaternion.Euler(0, yaw, 0) * Vector3.right * 1.9f;
            if (!OnTerrain(x - ax.x, z - ax.z, out var a) || !OnTerrain(x + ax.x, z + ax.z, out var b)) continue;
            if (Mathf.Abs(a.point.y - b.point.y) > 1.4f) continue;
            var go = Put(floor, logs % 2 == 0 ? "Log_A" : "Log_B", x, z, yaw, Rf(rnd, 0.85f, 1.1f), y: Mathf.Min(a.point.y, b.point.y), sink: 0.12f);
            float pitch = Mathf.Atan2(b.point.y - a.point.y, 3.8f) * Mathf.Rad2Deg;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(0, 0, pitch);
            go.transform.position = new Vector3(x, (a.point.y + b.point.y) * 0.5f - 0.12f, z);
            Take(x, z, 2.2f); logs++;
            for (int m = 0; m < 2; m++)
            {
                var side = Quaternion.Euler(0, yaw, 0) * Vector3.forward * (m == 0 ? 0.55f : -0.5f) + ax * Rf(rnd, -0.6f, 0.6f);
                if (Lay(floor, m == 0 ? "Mushroom_A" : "Mushroom_B", x + side.x, z + side.z, Rf(rnd, 0, 360), Rf(rnd, 0.9f, 1.3f), 0.01f, false)) mush++;
            }
        }
        void Take(float x, float z, float r) => taken.Add((new Vector2(x, z), r));

        // Ground dressing in CLUSTERS with open ground between them (reference low-poly scenes read because detail is
        // grouped and the rest stays calm; the uniform 650 + 480 sprinkle of round 1 made the floor noisy).
        // litter: clusters along the path edges and around tree bases
        foreach (var c in Clusters(rnd, 34, -24, 20, -22, 34, 30, p => PolyDist(p, PathPts) is > 0.8f and < 4f || rnd.NextDouble() < 0.35))
            for (int k = 0, n = rnd.Next(5, 9); k < n; k++)
            {
                var p = c + new Vector2(Rf(rnd, -1.6f, 1.6f), Rf(rnd, -1.6f, 1.6f));
                if (p.magnitude < 1f || StreamDist(p, out _) < 1.5f || (p - tiger2).magnitude < 1.6f || PolyDist(p, PathPts) < 0.6f) continue;
                string a = Pick(rnd, new[] { "Litter_A", "Litter_B", "Litter_C" });
                if (Lay(floor, a, p.x, p.y, Rf(rnd, 0, 360), Rf(rnd, 1.0f, 1.7f) * Mathf.Lerp(1f, 1.6f, p.magnitude / 34f), 0.0f, false)) litter++;
            }
        // twigs: a few, near litter clusters / tree bases, never on the path
        for (int tries = 0; tries < 2000 && twigs < 36; tries++)
        {
            float x = Rf(rnd, -20, 18), z = Rf(rnd, -18, 30);
            var p = new Vector2(x, z);
            if (p.magnitude < 1.5f || StreamDist(p, out _) < 1.4f || PolyDist(p, PathPts) < 1.2f || (p - tiger2).magnitude < 2f) continue;
            if (Lay(floor, rnd.NextDouble() < 0.6 ? "Twig_A" : "Twig_B", x, z, Rf(rnd, 0, 360), Rf(rnd, 0.9f, 1.4f), 0.02f, false)) twigs++;
        }
        // seedling cover: clumps of a few patches off the path
        foreach (var c in Clusters(rnd, 5, -7, 7, -6, 9, 8, p => PolyDist(p, PathPts) > 2.2f).Concat(Clusters(rnd, 28, -22, 18, -20, 30, 28, p => PolyDist(p, PathPts) > 2f)))
            for (int k = 0, n = rnd.Next(4, 8); k < n; k++)
            {
                var p = c + new Vector2(Rf(rnd, -1.3f, 1.3f), Rf(rnd, -1.3f, 1.3f));
                if (p.magnitude < 1.2f || PolyDist(p, PathPts) < 1.45f || StreamDist(p, out _) < 3.2f) continue;
                if (!OnTerrain(p.x, p.y, out var h) || h.point.y < LakeLevel + 0.4f) continue;
                if (Lay(floor, rnd.NextDouble() < 0.5 ? "Cover_A" : "Cover_B", p.x, p.y, Rf(rnd, 0, 360), Rf(rnd, 1.5f, 2.5f), 0.01f, false)) cover++;
            }
        // pebbles along the path edges
        for (int tries = 0; tries < 1500 && pebbles < 40; tries++)
        {
            float z = Rf(rnd, -20, 34), x = Rf(rnd, -5, 3);
            var p = new Vector2(x, z);
            float dp = PolyDist(p, PathPts);
            if (dp < 0.9f || dp > 2.2f || StreamDist(p, out _) < 1.4f || p.magnitude < 1.2f) continue;
            float s = Rf(rnd, 0.05f, 0.14f);
            var go = Put(floor, Pick(rnd, new[] { K + "stone_smallA.fbx", K + "stone_smallB.fbx", K + "stone_smallC.fbx", "Rock_B" }), x, z, Rf(rnd, 0, 360), 1, targetHeight: s, sink: s * 0.3f, shadows: false);
            go.transform.rotation = Quaternion.Euler(Rf(rnd, -25, 25), Rf(rnd, 0, 360), Rf(rnd, -25, 25));
            pebbles++;
        }
        log.Add($"floor: roots {roots}, logs {logs}, mushrooms {mush}, litter {litter}, twigs {twigs}, cover {cover}, pebbles {pebbles}");
    }

    /// Cluster centres inside a box/radius that pass a filter, at least 3 m apart.
    static List<Vector2> Clusters(Random rnd, int count, float x0, float x1, float z0, float z1, float maxR, System.Func<Vector2, bool> ok)
    {
        var l = new List<Vector2>();
        for (int t = 0; t < count * 60 && l.Count < count; t++)
        {
            var p = new Vector2(Rf(rnd, x0, x1), Rf(rnd, z0, z1));
            if (p.magnitude > maxR || p.magnitude < 1.5f || StreamDist(p, out _) < 1.8f || !ok(p)) continue;
            if (l.Any(q => (q - p).sqrMagnitude < 9f)) continue;
            l.Add(p);
        }
        return l;
    }

    static void PlaceTiger(Transform parent, List<string> log)
    {
        var path = ProjectPaths.Find("PF_WhiteTiger.prefab");
        if (path == null) { log.Add("WARN no PF_WhiteTiger"); return; }
        var clip = AssetDatabase.LoadAllAssetsAtPath(ProjectPaths.Find("SK_WhiteTiger.fbx")).OfType<AnimationClip>().FirstOrDefault(c => c.name == "Idle");
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(TigerController);
        if (!ac && clip) ac = AnimatorController.CreateAnimatorControllerAtPathWithClip(TigerController, clip);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), parent);
        OnTerrain(TigerPos.x, TigerPos.z, out var hit);
        go.transform.SetPositionAndRotation(new Vector3(TigerPos.x, hit.point.y - 0.02f, TigerPos.z), Quaternion.Euler(0, TigerYaw, 0));
        go.transform.localScale = Vector3.one * TigerScale;
        var anim = go.GetComponentInChildren<Animator>() ?? go.AddComponent<Animator>();
        anim.runtimeAnimatorController = ac;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.On; r.updateWhenOffscreen = false;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
        }
        // keep a sightline from the viewer to the tiger: drop scatter plants / cover standing in the wedge between
        var cam2 = Vector2.zero; var t2 = new Vector2(TigerPos.x, TigerPos.z); int cleared = 0;
        foreach (var g in new[] { "Jungle", "Floor" })
        {
            var grp = parent.parent.Find(g); if (!grp) continue;
            foreach (var c in grp.Cast<Transform>().ToList())
            {
                if (g == "Floor" && !c.name.StartsWith("PF_Cover")) continue;
                var p = new Vector2(c.position.x, c.position.z);
                float d = SegDist(p, cam2, t2, out float t);
                if (t > 0.12f && t < 1.08f && d < Mathf.Lerp(0.25f, 1.3f, t)) { UnityEngine.Object.DestroyImmediate(c.gameObject); cleared++; }
            }
        }
        log.Add($"tiger at {go.transform.position}, yaw {TigerYaw}, idle clip {(clip ? clip.length.ToString("F1") + " s" : "MISSING")}, sightline cleared {cleared}");
    }
}
