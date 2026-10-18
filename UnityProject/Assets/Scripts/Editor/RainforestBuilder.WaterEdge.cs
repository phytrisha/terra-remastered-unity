using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = System.Random;

/// Edges near the viewer (blender/lib/veg.py reeds, lily_pads; Grass_A): reed clumps along both stream banks, lily-pad
/// rafts in the lake shallows on the shore facing the viewer, grass tufts along the path edges and moss cushions on the
/// open grass near the viewer (the big flat ground facets next to the path read empty). Seeded; placed before the overlap pass, which clears anything they collide with.
public static partial class RainforestBuilder
{
    static void BuildWaterEdges(Transform parent, Vector3 viewer, List<string> log)
    {
        var rnd = new Random(4242);
        float R(double a, double b) => (float)(a + (b - a) * rnd.NextDouble());
        var cascadeSet = new HashSet<int>(cascades);
        int reeds = 0, lilies = 0, tufts = 0;
        // reeds: every ~2.4 m along the stream within 32 m, a clump on one bank or both, clear of the cascades
        for (int i = 2; i < streamS.Count - 2; i += 8)
        {
            var p = streamS[i];
            if (new Vector2(p.x - viewer.x, p.y - viewer.z).magnitude > 32f) continue;
            if (Enumerable.Range(-6, 13).Any(k => cascadeSet.Contains(i + k))) continue;
            var t = (streamS[i + 1] - streamS[i - 1]).normalized; var side = new Vector2(t.y, -t.x);
            foreach (int s in new[] { -1, 1 })
            {
                if (rnd.NextDouble() < 0.45) continue;
                var q = p + side * s * R(1.55, 2.0) + t * R(-0.8, 0.8);
                Put(parent, rnd.NextDouble() < 0.5 ? "Reeds_A" : "Reeds_B", q.x, q.y, R(0, 360), R(0.8, 1.2), 0.03f);
                reeds++;
            }
        }
        // lily pads: lake shallows 0.3-1.4 m deep, z 35-80 (the shore in the main view), spaced 3 m apart
        var taken = new List<Vector2>();
        for (int k = 0; k < 400 && lilies < 18; k++)
        {
            var q = new Vector2(R(-30, 30), R(35, 80));
            float depth = LakeLevel - Height(q.x, q.y);
            if (depth < 0.3f || depth > 1.4f || taken.Any(o => (o - q).magnitude < 3f)) continue;
            Put(parent, rnd.NextDouble() < 0.5 ? "LilyPads_A" : "LilyPads_B", q.x, q.y, R(0, 360), R(0.8, 1.3), 0f, y: LakeLevel + 0.015f, shadows: false);
            taken.Add(q); lilies++;
        }
        // grass tufts along both path edges within 22 m (0.95-1.5 m from the centreline), ~0.8 m apart
        for (float d = 0; d < 1f; d += 0.004f)
        {
            int seg = Mathf.Min((int)(d * (PathPts.Length - 1)), PathPts.Length - 2); float u = d * (PathPts.Length - 1) - seg;
            var p = Vector2.Lerp(PathPts[seg], PathPts[seg + 1], u);
            if (new Vector2(p.x - viewer.x, p.y - viewer.z).magnitude > 22f || p.magnitude < 1.6f) continue;
            var t = (PathPts[seg + 1] - PathPts[seg]).normalized; var side = new Vector2(t.y, -t.x);
            foreach (int s in new[] { -1, 1 })
            {
                if (rnd.NextDouble() < 0.5) continue;
                var q = p + side * s * R(0.95, 1.5);
                if (StreamDist(q, out _) < 2.2f) continue;
                Put(parent, "Grass_A", q.x, q.y, R(0, 360), R(0.6, 1.1), 0.02f, shadows: false);
                tufts++;
            }
        }
        // ground moss: flat cushions on the open grass 3-14 m from the viewer (the big plain ground facets beside the path)
        int mossN = 0;
        for (int k = 0; k < 600 && mossN < 45; k++)
        {
            float ang = R(0, 6.283), dist = R(3, 14);
            var q = new Vector2(viewer.x + Mathf.Cos(ang) * dist, viewer.z + Mathf.Sin(ang) * dist);
            if (PolyDist(q, PathPts) < 1.2f || StreamDist(q, out _) < 1.9f) continue;
            Put(parent, rnd.NextDouble() < 0.5 ? "Moss_A" : "Moss_B", q.x, q.y, R(0, 360), R(0.8, 1.6), 0.02f, shadows: false);
            if (rnd.NextDouble() < 0.6) { var g2 = q + new Vector2(R(-0.6, 0.6), R(-0.6, 0.6)); Put(parent, "Grass_A", g2.x, g2.y, R(0, 360), R(0.5, 0.9), 0.02f, shadows: false); }
            mossN++;
        }
        log.Add($"edges: {reeds} reed clumps, {lilies} lily-pad rafts, {tufts} path-edge grass tufts, {mossN} ground moss cushions");
    }
}
