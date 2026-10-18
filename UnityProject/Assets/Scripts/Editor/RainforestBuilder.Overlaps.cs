using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// Scene-wide overlap pass (run last, after every placement step): no plant or rock in the detailed zone around the
/// viewer may grow through another, and none may stand in an animal's way.
///   pieces:    Hero, Jungle, StreamRocks, PathDebris, Floor, Forest, Epiphytes, Edges within OverlapRadius of the viewer; leaf litter and
///              twigs are flat ground cover (walked over, plants grow out of them) and are left out
///   overlap:   >= 3 surface samples (vertices + triangle centres, subsampled) of one piece within PairGap of the other's
///              mesh, tested both ways; surface roots at a buttress trunk belong to that tree
///   corridors: the tiger's recorded body sweep and the prey animals' route bands (route +- drift + half body width,
///              up to body height): a piece may never be moved into one
/// Resolution, smallest first: trees and palms never move; the other piece turns about its root and / or slides away
/// (cheapest candidate first, big composition pieces <= 0.8 m and <= 60 deg), small ones may shrink to 70 %, small
/// removable ones are removed when nothing is clear; anything left is reported. Neighbours come from physics queries,
/// so the pass scales with the number of pieces, not their square. Audit: unity/clearance_audit.cs "pairs".
public static partial class RainforestBuilder
{
    const float OverlapRadius = 58f;                               // beyond: the sunk hill canopy (crowns merge by design)
    static readonly string[] FixedPieces = { "ButtressTree", "Tree_", "Palm" };
    static readonly string[] OverlapGroups = { "Hero", "Jungle", "StreamRocks", "PathDebris", "Floor", "Forest", "Epiphytes", "Edges" };
    const int MaxSamples = 700, MaxTries = 100;

    class OvObj
    {
        public Transform t; public Vector3[] local; public Transform[] space; public float volume; public bool fixedPiece, big, removable, epiphyte;
        public readonly List<MeshCollider> cols = new();
        public Bounds Bounds() { var b = cols[0].bounds; foreach (var c in cols) b.Encapsulate(c.bounds); return b; }
        public IEnumerable<Vector3> Samples() { for (int i = 0; i < local.Length; i++) yield return space[i].TransformPoint(local[i]); }
    }

    static void ResolveOverlaps(Transform root, Vector3 viewer, System.Func<Vector3, bool> inCorridor, List<string> log)
    {
        var objs = new List<OvObj>(); var owner = new Dictionary<Collider, OvObj>();
        foreach (var g in OverlapGroups)
        {
            var gt = root.Find(g); if (!gt) continue;
            foreach (Transform t in gt)
            {
                if (new Vector2(t.position.x - viewer.x, t.position.z - viewer.z).magnitude > OverlapRadius || WalkedOver.Any(t.name.Contains)) continue;
                var o = new OvObj { t = t, fixedPiece = (FixedPieces.Any(t.name.Contains) && g != "Forest") || g == "Epiphytes", big = BigPieces.Any(t.name.Contains) || FixedPieces.Any(t.name.Contains) };
                o.epiphyte = g == "Epiphytes";
                o.removable = !o.fixedPiece && !o.big && g != "StreamRocks";       // any understorey plant / stone (size checked when removing)
                var loc = new List<Vector3>(); var sp = new List<Transform>();
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
                {
                    var mr = mf.GetComponent<MeshRenderer>(); if (!mf.sharedMesh || !mr || !mr.enabled || mf.name.StartsWith("UCX_")) continue;
                    var lod = mf.GetComponentInParent<LODGroup>();
                    if (lod && lod.GetLODs().Length > 0 && !lod.GetLODs()[0].renderers.Contains(mr)) continue;
                    var m = mf.sharedMesh; var vs = m.vertices; var tris = m.triangles;
                    foreach (var v in vs) { loc.Add(v); sp.Add(mf.transform); }
                    for (int i = 0; i < tris.Length; i += 3) { loc.Add((vs[tris[i]] + vs[tris[i + 1]] + vs[tris[i + 2]]) / 3f); sp.Add(mf.transform); }
                    var c = mf.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = m; o.cols.Add(c); owner[c] = o;
                }
                if (o.cols.Count == 0) continue;
                int stride = Mathf.Max(1, loc.Count / MaxSamples);           // evenly spread subset
                o.local = loc.Where((_, i) => i % stride == 0).ToArray(); o.space = sp.Where((_, i) => i % stride == 0).ToArray();
                var bb = o.Bounds(); o.volume = bb.size.x * bb.size.y * bb.size.z;
                objs.Add(o);
            }
        }
        Physics.SyncTransforms();
        var buf = new Collider[64];
        // surface roots and epiphytes (moss, bromeliads, orchids, lianas) sit on their tree by design
        bool Exempt(OvObj a, OvObj b) => (a.t.name.Contains("Roots") && b.t.name.Contains("ButtressTree")) || (b.t.name.Contains("Roots") && a.t.name.Contains("ButtressTree"))
                                      || (a.epiphyte && (FixedPieces.Any(b.t.name.Contains) || b.epiphyte)) || (b.epiphyte && (FixedPieces.Any(a.t.name.Contains) || a.epiphyte));
        Dictionary<OvObj, int> Contacts(OvObj o)
        {
            var res = new Dictionary<OvObj, int>();
            var ob = o.Bounds();
            int nb = Physics.OverlapBoxNonAlloc(ob.center, ob.extents + Vector3.one * PairGap, buf, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            var near = new HashSet<OvObj>();
            for (int i = 0; i < nb; i++) if (owner.TryGetValue(buf[i], out var x) && x != o && !Exempt(o, x)) near.Add(x);
            if (near.Count == 0) return res;
            var sbuf = new Collider[16];
            foreach (var s in o.Samples())
            {
                int n = Physics.OverlapSphereNonAlloc(s, PairGap, sbuf, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) if (owner.TryGetValue(sbuf[i], out var x) && near.Contains(x)) res[x] = res.TryGetValue(x, out var k) ? k + 1 : 1;
            }
            foreach (var x in near)
            {   // the other way: a big triangle of x can cross o with none of o's samples near it
                int k = 0;
                foreach (var s in x.Samples())
                {
                    if (!ob.Contains(s)) continue;
                    int n = Physics.OverlapSphereNonAlloc(s, PairGap, sbuf, ~0, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++) if (owner.TryGetValue(sbuf[i], out var y) && y == o) { k++; break; }
                }
                if (k > 0) res[x] = res.TryGetValue(x, out var c) ? c + k : k;
            }
            return res;
        }
        int CorridorSamples(OvObj o)
        {
            int n = 0;
            foreach (var s in o.Samples()) if (inCorridor(s)) n++;
            return n;
        }
        int Score(OvObj o) => CorridorSamples(o) * 10 + Contacts(o).Values.Where(v => v >= 3).Sum();
        void Place(OvObj o, Vector3 xz, Quaternion rot, Vector3 scale)
        {
            var p = o.t.position; OnTerrain(p.x, p.z, out var h0); OnTerrain(xz.x, xz.z, out var h1);
            o.t.SetPositionAndRotation(new Vector3(xz.x, p.y + h1.point.y - h0.point.y, xz.z), rot); o.t.localScale = scale;
            Physics.SyncTransforms();
        }

        int before = objs.Count(o => Contacts(o).Values.Any(v => v >= 3));
        int moved = 0, turned = 0, shrunk = 0, removed = 0, kept = 0;
        for (int round = 0; round < 6; round++)
        {
            int changes = 0;
            foreach (var o in objs.OrderBy(o => o.volume).ToList())
            {
                if (!objs.Contains(o)) continue;
                var contacts = Contacts(o).Where(kv => kv.Value >= 3).ToList();
                // trees never move: a moved buttress tree's root flare reached into the tiger's walk (its 20 m mesh is
                // sampled too coarsely for the corridor test); touching trees are reported instead
                if (o.fixedPiece) continue;
                bool inWay = CorridorSamples(o) > 0;
                // o moves when it is the smaller of a pair, or the other one can't move
                var against = contacts.Where(kv => kv.Key.fixedPiece || kv.Key.volume >= o.volume).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
                if (against == null && !inWay) continue;
                var start = o.t.position; var rot0 = o.t.rotation; var sc0 = o.t.localScale;
                var away = against != null ? start - against.Bounds().center : Vector3.right; away.y = 0;
                if (away.sqrMagnitude < 1e-6f) away = Vector3.right; away.Normalize();
                float maxShift = o.big ? 0.8f : MaxShift;
                // 1) slide: outward from the contact, 8 cm steps over 7 directions, nearest first (the whole range gets
                //    tried: sorting turns and slides by one cost spent every try on turns within 16 cm)
                // 2) turn about the root at the best spot found
                int bestScore = Score(o); var best = (cost: 0f, pos: start, yaw: 0f); int tries = 0;
                for (float r = 0.08f; r <= maxShift + 1e-3f && bestScore > 0 && tries < MaxTries; r += 0.08f)
                    foreach (float ang in new[] { 0f, 35f, -35f, 70f, -70f, 110f, -110f })
                    {
                        if (bestScore == 0 || ++tries > MaxTries) break;
                        var pos = start + Quaternion.AngleAxis(ang, Vector3.up) * away * r;
                        Place(o, pos, rot0, sc0);
                        int s = Score(o); if (s < bestScore) { bestScore = s; best = (r, pos, 0f); }
                    }
                if (bestScore > 0)
                    for (float yaw = o.big ? -60f : -150f; yaw <= (o.big ? 60.1f : 180f) && bestScore > 0; yaw += 30f)
                    {
                        if (yaw == 0f) continue;
                        Place(o, best.pos, Quaternion.AngleAxis(yaw, Vector3.up) * rot0, sc0);
                        int s = Score(o); if (s < bestScore) { bestScore = s; best = (best.cost, best.pos, yaw); }
                    }
                Place(o, best.pos, Quaternion.AngleAxis(best.yaw, Vector3.up) * rot0, sc0);
                float scale = 1f;
                if (bestScore > 0 && !o.big)                                    // small pieces may shrink a little
                    foreach (float k in new[] { 0.85f, 0.7f })
                    {
                        Place(o, best.pos, o.t.rotation, sc0 * k); int s = Score(o);
                        if (s < bestScore) { bestScore = s; scale = k; }
                        if (bestScore == 0) break;
                    }
                Place(o, best.pos, o.t.rotation, sc0 * scale);
                var d = new Vector2(best.pos.x - start.x, best.pos.z - start.z).magnitude;
                string what = $"{o.t.parent.name}/{o.t.name} {d:F2} m, turned {best.yaw:F0} deg" + (scale < 1f ? $", scaled {scale:F2}" : "") + (against != null ? $" (from {against.t.name})" : " (animal corridor)");
                if (bestScore == 0)
                {
                    if (d > 1e-3f) moved++; else turned++;
                    if (scale < 1f) shrunk++;
                    log.Add("overlaps: " + what); changes++; continue;
                }
                var ext = o.Bounds().size;
                if (o.removable && ext.y < 1.5f && Mathf.Max(ext.x, ext.z) < 2.2f)
                {
                    log.Add($"overlaps: removed {o.t.parent.name}/{o.t.name} at ({start.x:F2}, {start.z:F2}) (no clear spot)");
                    foreach (var c in o.cols) owner.Remove(c);
                    objs.Remove(o); Object.DestroyImmediate(o.t.gameObject); Physics.SyncTransforms(); removed++; changes++; continue;
                }
                if (o.big && round >= 2)
                {   // a composition piece with no clear spot within 0.8 m / 60 deg: shrink it a little where it is best
                    foreach (float k in new[] { 0.9f, 0.8f })
                    {
                        Place(o, best.pos, o.t.rotation, sc0 * k); int sco = Score(o);
                        if (sco == 0) { log.Add($"overlaps: {what}, scaled {k:F2}"); shrunk++; changes++; bestScore = 0; break; }
                    }
                    if (bestScore == 0) continue;
                    if (o.t.parent.name == "Jungle" && !o.fixedPiece)
                    {   // scattered (not a composed hero piece): it goes rather than grow through its neighbour
                        log.Add($"overlaps: removed {o.t.parent.name}/{o.t.name} at ({start.x:F2}, {start.z:F2}) (big, no clear spot)");
                        foreach (var c in o.cols) owner.Remove(c);
                        objs.Remove(o); Object.DestroyImmediate(o.t.gameObject); Physics.SyncTransforms(); removed++; changes++; continue;
                    }
                }
                Place(o, start, rot0, sc0);                                    // try again next round (neighbours may move)
            }
            if (changes == 0) break;
        }
        // last resort for what is left: understorey and scattered pieces that still touch something are removed (the
        // composed hero pieces, trees, stream rocks and epiphytes stay and are reported below)
        foreach (var o in objs.OrderBy(o => o.volume).ToList())
        {
            if (!objs.Contains(o) || o.fixedPiece || o.t.parent.name == "Hero" || o.t.parent.name == "StreamRocks") continue;
            if (!Contacts(o).Values.Any(v => v >= 3) && CorridorSamples(o) == 0) continue;
            log.Add($"overlaps: removed {o.t.parent.name}/{o.t.name} at ({o.t.position.x:F2}, {o.t.position.z:F2}) (still touching after {6} rounds)");
            foreach (var c in o.cols) owner.Remove(c);
            objs.Remove(o); Object.DestroyImmediate(o.t.gameObject); Physics.SyncTransforms(); removed++;
        }
        foreach (var o in objs.Where(o => Contacts(o).Values.Any(v => v >= 3)).OrderByDescending(o => o.volume).Take(40))
        {
            var with = Contacts(o).Where(kv => kv.Value >= 3).Select(kv => kv.Key.t.name);
            log.Add($"WARN overlaps: {o.t.parent.name}/{o.t.name} at ({o.t.position.x:F1}, {o.t.position.z:F1}) still touches {string.Join(", ", with)}"); kept++;
        }
        int after = objs.Count(o => Contacts(o).Values.Any(v => v >= 3));
        foreach (var o in objs) foreach (var c in o.cols) Object.DestroyImmediate(c);
        Physics.SyncTransforms();
        log.Add($"overlaps: {objs.Count} pieces within {OverlapRadius} m; overlapping pieces {before} -> {after}; moved {moved}, turned {turned}, shrunk {shrunk}, removed {removed}, left {kept}");
    }

    /// Animal corridors as one test: the tiger's recorded sweep + the prey routes' bands (built after PlaceAnimals).
    static System.Func<Vector3, bool> AnimalCorridors(Transform root, List<string> log)
    {
        var tests = new List<System.Func<Vector3, bool>>();
        var loop2 = TigerLoop.ToArray();
        var sweep = LoadSweep(loop2, log);
        if (sweep != null)
            tests.Add(s =>
            {
                int cx = Mathf.FloorToInt(s.x / SweepCell), cz = Mathf.FloorToInt(s.z / SweepCell), m = Mathf.CeilToInt(SweepMargin / SweepCell);
                for (int i = -m; i <= m; i++) for (int k = -m; k <= m; k++) if (sweep.TryGetValue((cx + i, cz + k), out var top) && s.y < top + SweepMargin) { OnTerrain(s.x, s.z, out var h); return s.y - h.point.y > CorridorLow; }
                return false;
            });
        foreach (var w in root.GetComponentsInChildren<AnimalWander>())
        {
            if (w.loop || w.route == null || w.route.Length < 2) continue;              // the tiger's loop has its sweep
            var r2 = w.route.Select(p => new Vector2(p.x, p.z)).ToArray();
            float half = w.lateral + (w.name.Contains("Tapir") ? 0.3f : 0.22f) + 0.35f, top = w.name.Contains("Tapir") ? 1.15f : 1.4f;   // + arcs: they swing out in turns
            tests.Add(s =>
            {
                var q = new Vector2(s.x, s.z); float d = float.MaxValue;
                for (int i = 0; i + 1 < r2.Length; i++) d = Mathf.Min(d, SegDist(q, r2[i], r2[i + 1], out _));
                if (d > half) return false;
                OnTerrain(s.x, s.z, out var h); float y = s.y - h.point.y; return y > CorridorLow && y < top;
            });
        }
        return s => tests.Any(f => f(s));
    }
}
