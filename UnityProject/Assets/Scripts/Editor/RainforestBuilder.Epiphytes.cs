using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

/// Epiphytes and lianas on the big trunks near the viewer (blender/lib/veg.py: bromeliad, orchid, moss_cushion, liana):
/// rays from random directions onto each trunk's bark (temporary mesh colliders) give the spots; moss lies on the bark
/// (local up = surface normal) from the root flare up to ~3 m, bromeliads and orchids perch higher (upright, leaning
/// out from the trunk); some trees get a liana from crown height down to the ground beside them. Seeded, re-runnable.
/// The overlap pass treats them as part of their tree (they sit on its bark on purpose).
public static partial class RainforestBuilder
{
    static readonly string[] EpiphyteHosts = { "ButtressTree", "Tree_A", "Tree_B" };

    static void BuildEpiphytes(Transform parent, Transform root, Vector3 viewer, List<string> log)
    {
        var rnd = new Random(909);
        var hosts = root.GetComponentsInChildren<Transform>()
            .Where(t => t.parent && t.parent.parent == root && t.parent.name != parent.name && EpiphyteHosts.Any(t.name.Contains))
            .Where(t => new Vector2(t.position.x - viewer.x, t.position.z - viewer.z).magnitude < 35f).ToList();
        int moss = 0, bro = 0, orc = 0, lia = 0;
        float R(double a, double b) => (float)(a + (b - a) * rnd.NextDouble());
        foreach (var h in hosts)
        {
            var cols = new List<MeshCollider>();
            foreach (var mf in h.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>(); if (!mf.sharedMesh || !mr || mf.name.StartsWith("UCX_")) continue;
                var lod = mf.GetComponentInParent<LODGroup>();
                if (lod && lod.GetLODs().Length > 0 && !lod.GetLODs()[0].renderers.Contains(mr)) continue;
                var c = mf.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh; cols.Add(c);
            }
            Physics.SyncTransforms();
            var b = h.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
            float trunkTop = h.position.y + b.size.y * (h.name.Contains("Buttress") ? 0.75f : 0.45f);
            bool Hit(float y, float az, out RaycastHit hit)
            {   // from outside toward the trunk axis, only this tree's colliders count
                var dir = new Vector3(Mathf.Cos(az), 0, Mathf.Sin(az));
                var o = new Vector3(h.position.x, y, h.position.z) + dir * (Mathf.Max(b.extents.x, b.extents.z) + 1f);
                foreach (var x in Physics.RaycastAll(o, -dir, 30f).OrderBy(x => x.distance))
                    if (cols.Contains(x.collider as MeshCollider)) { hit = x; return true; }
                hit = default; return false;
            }
            int Count(int lo, int hi) => lo + rnd.Next(hi - lo + 1);
            for (int i = 0, n = Count(4, 8); i < n; i++)
                if (Hit(h.position.y + R(0.2, 3.0), R(0, 6.283), out var hit))
                {
                    var go = Put(parent, rnd.NextDouble() < 0.6 ? "Moss_A" : "Moss_B", hit.point.x, hit.point.z, 0, R(0.7, 1.3), 0f, y: hit.point.y, shadows: false);
                    go.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.AngleAxis(R(0, 360), Vector3.up) * go.transform.rotation;
                    go.transform.position = hit.point - hit.normal * 0.01f; moss++;
                }
            for (int i = 0, n = Count(2, 4); i < n; i++)
                if (Hit(R(h.position.y + 2.0, trunkTop), R(0, 6.283), out var hit))
                {
                    var flat = new Vector3(hit.normal.x, 0, hit.normal.z).normalized;
                    bool orchid = rnd.NextDouble() < 0.35;
                    var go = Put(parent, orchid ? "Orchid_A" : (rnd.NextDouble() < 0.5 ? "Bromeliad_A" : "Bromeliad_B"), hit.point.x, hit.point.z, R(0, 360), R(0.8, 1.3), 0f, y: hit.point.y, shadows: false);
                    go.transform.rotation = Quaternion.AngleAxis(R(20, 35), Vector3.Cross(Vector3.up, flat)) * go.transform.rotation;   // lean out
                    go.transform.position = hit.point - flat * 0.04f;
                    if (orchid) orc++; else bro++;
                }
            foreach (var c in cols) Object.DestroyImmediate(c);            // before the liana: its ground point must not hit the crown
            Physics.SyncTransforms();
            if (rnd.NextDouble() < 0.45)
            {   // liana: ground end 1.5-3 m out, top end at crown height against the trunk
                float az = R(0, 6.283); var dir = new Vector3(Mathf.Cos(az), 0, Mathf.Sin(az));
                var foot = new Vector3(h.position.x, 0, h.position.z) + dir * (Mathf.Max(b.extents.x, b.extents.z) * 0.4f + R(1.5, 3.0));
                var go = Put(parent, rnd.NextDouble() < 0.5 ? "Liana_A" : "Liana_B", foot.x, foot.z, 0, 1f, 0.05f);
                var top = new Vector3(h.position.x, trunkTop, h.position.z) + dir * 0.3f;
                var rs = go.GetComponentsInChildren<Renderer>(); var lb = rs[0].bounds; foreach (var r in rs) lb.Encapsulate(r.bounds);
                float s = (top.y - go.transform.position.y) / Mathf.Max(0.5f, lb.size.y);
                go.transform.localScale = Vector3.one * s;
                // the liana reaches +X in its own frame: turn it so it reaches back toward the trunk
                var toward = new Vector3(top.x - foot.x, 0, top.z - foot.z);
                go.transform.rotation = Quaternion.FromToRotation(Vector3.right, toward.normalized) * Quaternion.Euler(0, 0, 0) * Asset(go.name.Contains("Liana_A") ? "Liana_A" : "Liana_B").transform.localRotation;
                lia++;
            }
        }
        Physics.SyncTransforms();
        log.Add($"epiphytes: {hosts.Count} trunks, {moss} moss cushions, {bro} bromeliads, {orc} orchids, {lia} lianas");
    }
}
