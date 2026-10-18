using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// Ambient life (runtime: Butterflies, BirdFlock): butterflies around the heliconias near the viewer, a flock crossing
/// the sky now and then, splash spray at the foot of each cascade, gust fronts through the foliage. Faceted meshes generated here, flat LowPolyLit colours.
public static partial class RainforestBuilder
{
    static void BuildLife(Transform parent, Transform root, Transform viewer, List<string> log)
    {
        // ---- butterflies: anchors = the heliconias within 3-16 m of the viewer (nearest first)
        var vp = viewer.position;
        var anchors = root.GetComponentsInChildren<Transform>()
            .Where(t => t.name.StartsWith("PF_Heliconia") && t.parent && t.parent.parent == root)
            .Select(t => (t, d: new Vector2(t.position.x - vp.x, t.position.z - vp.z).magnitude))
            .Where(x => x.d > 3f && x.d < 16f).OrderBy(x => x.d).Take(10).Select(x => x.t).ToArray();
        var wing = SaveMesh("SM_Gen_ButterflyWing", m =>
        {   // one wing, hinge along +Z at x = 0, lying flat (flapping = rotation about Z): fore + hind lobe
            m.SetVertices(new List<Vector3> { new(0, 0, 0.012f), new(0.030f, 0.002f, 0.034f), new(0.052f, 0, 0.018f), new(0.040f, 0, -0.004f),
                                              new(0, 0, -0.008f), new(0.036f, 0, -0.030f), new(0.014f, 0, -0.040f) });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 4, 3, 5, 4, 5, 6 }, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
        });
        var body = SaveMesh("SM_Gen_ButterflyBody", m =>
        {
            m.SetVertices(new List<Vector3> { new(0, 0, 0.022f), new(0.004f, 0, 0), new(0, 0.004f, 0), new(-0.004f, 0, 0), new(0, -0.003f, 0), new(0, 0, -0.026f) });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1, 5, 2, 1, 5, 3, 2, 5, 4, 3, 5, 1, 4 }, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
        });
        var bf = new GameObject("Butterflies").AddComponent<Butterflies>(); bf.transform.SetParent(parent, false);
        bf.anchors = anchors; bf.bodyMesh = body; bf.wingMesh = wing;
        bf.bodyMat = LitMat(ProjectPaths.VFXMaterials, "M_ButterflyBody", new Color(0.12f, 0.1f, 0.09f));
        bf.wingMats = new[]
        {
            LitMat(ProjectPaths.VFXMaterials, "M_ButterflyBlue", new Color(0.10f, 0.45f, 0.95f), smooth: 0.5f, twoSided: true),    // morpho
            LitMat(ProjectPaths.VFXMaterials, "M_ButterflyOrange", new Color(0.98f, 0.52f, 0.10f), twoSided: true),
            LitMat(ProjectPaths.VFXMaterials, "M_ButterflyYellow", new Color(0.98f, 0.86f, 0.25f), twoSided: true),
        };
        bf.count = Mathf.Clamp(anchors.Length * 2, 0, 14);

        // ---- birds
        var bwing = SaveMesh("SM_Gen_BirdWing", m =>
        {   // tapered, slightly swept wing, hinge along +Z at x = 0 (span 0.5 at scale 1: wingspan = 2 x 0.5 x scale)
            m.SetVertices(new List<Vector3> { new(0, 0, 0.10f), new(0, 0, -0.10f), new(0.28f, 0.02f, 0.04f), new(0.5f, 0, -0.08f), new(0.24f, 0, -0.12f) });
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 4, 2, 3, 4 }, 0); m.RecalculateNormals(); m.RecalculateBounds();
        });
        var bbody = SaveMesh("SM_Gen_BirdBody", m =>
        {
            m.SetVertices(new List<Vector3> { new(0, 0, 0.30f), new(0.05f, 0, 0), new(0, 0.05f, 0.02f), new(-0.05f, 0, 0), new(0, -0.04f, 0), new(0, 0.01f, -0.32f) });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1, 5, 2, 1, 5, 3, 2, 5, 4, 3, 5, 1, 4 }, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
        });
        var fl = new GameObject("BirdFlock").AddComponent<BirdFlock>(); fl.transform.SetParent(parent, false);
        fl.bodyMesh = bbody; fl.wingMesh = bwing; fl.viewer = viewer; fl.wingspan = 1.7f;
        fl.mat = LitMat(ProjectPaths.VFXMaterials, "M_Bird", new Color(0.10f, 0.10f, 0.11f), twoSided: true);

        // ---- splash at the foot of each cascade within 45 m (white flecks thrown up over the dark pool, falling back)
        var spray = AssetDatabase.LoadAssetAtPath<Material>($"{ProjectPaths.VFXMaterials}/M_Spray.mat");
        if (!spray) { spray = new Material(Shader.Find("TerraRemastered/Mote")); AssetDatabase.CreateAsset(spray, $"{ProjectPaths.VFXMaterials}/M_Spray.mat"); }
        spray.SetFloat("_Intensity", 1.3f); EditorUtility.SetDirty(spray);
        int sprays = 0;
        foreach (int ci in cascades)
        {
            int fi = ci + 4;                                  // the foot of the drop (the cascade face spans ~5 samples)
            if (fi + 2 >= streamS.Count) continue;
            var p = new Vector3(streamS[fi].x, streamLevel[fi], streamS[fi].y);
            if (new Vector2(p.x - vp.x, p.z - vp.z).magnitude > 45f) continue;
            var down = new Vector3(streamS[fi + 2].x - p.x, 0, streamS[fi + 2].y - p.z).normalized;
            var go = new GameObject($"Spray_{sprays}", typeof(ParticleSystem)); go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(p + down * 0.35f + Vector3.up * 0.05f, Quaternion.LookRotation(down * 0.5f + Vector3.up));
            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.prewarm = true; main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.18f); main.gravityModifier = 0.7f; main.maxParticles = 200;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1, 1, 1, 0.55f), new Color(0.85f, 0.95f, 1f, 0.9f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 70f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 28f; sh.radius = 0.35f; sh.scale = new Vector3(1.6f, 0.3f, 1f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                                             new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = spray; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            sprays++;
        }
        // drifting cloud shadows on the far landscape (the clouds in the sky drift the same way)
        new GameObject("CloudShadows").AddComponent<CloudShadows>().transform.SetParent(parent, false);
        // gust fronts through the foliage (LowPolyFoliage reads the globals), roughly from the lake side
        var wg = new GameObject("WindGusts").AddComponent<WindGusts>(); wg.transform.SetParent(parent, false);
        wg.direction = new Vector2(-0.35f, -0.94f);
        log.Add($"life: {bf.count} butterflies on {anchors.Length} heliconias, bird flock, spray at {sprays} cascades");
    }
}
