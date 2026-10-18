using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// Clearance around the tiger's pacing loop (run after the loop is laid out; re-runnable, every move is logged):
///   corridor: nothing may reach into the tiger's swept body: the RECORDED sweep (sources/tiger/corridor.txt, every body
///             sphere of a 60 s motion recording, + 10 cm) when it matches the loop, else a model (tail base to nose along
///             the heading, swept along the route, +- CorridorHalfWidth), 4 cm above the ground up to the body's top;
///             leaf litter and twigs are walked over and stay
///   pairs:    plants and rocks around the loop must not grow through each other (a rock inside a fern, a fern in a
///             trunk, grass through a fern); big pieces layered over each other at the jungle edge (hero leaves, banana,
///             heliconia, trees) are composed that way and stay
/// Resolution: the smaller object of a pair (or the one in the corridor) is turned about its root and / or slid away
/// (8 cm steps, up to MaxShift; big composition pieces 0.8 m and turned first) and re-grounded; the cheapest change that
/// is clear of the corridor AND of every neighbour wins. Big pieces turn at most 60 deg and keep their composed spot
/// when only a neighbour overlap is left; small pieces that find no clear spot are removed; anything left is reported.
/// Audit: unity/clearance_audit.cs (the tiger's recorded body against the same meshes).
public static partial class RainforestBuilder
{
    // corridor: the tiger's body line (BodyBack m behind its root = tail base, BodyFront ahead = nose, scale 1.3) swept
    // along the loop, +- CorridorHalfWidth (half body width + leg swing + route tracking error)
    const float CorridorHalfWidth = 0.36f, CorridorLow = 0.04f, CorridorHigh = 1.2f, BodyBack = 0.75f, BodyFront = 1.35f;
    const float PairGap = 0.03f, MaxShift = 1.6f;
    static readonly string[] ClearanceGroups = { "Hero", "Jungle", "StreamRocks", "PathDebris", "Floor" };
    static readonly string[] WalkedOver = { "Litter", "Twig" };               // flat ground pieces: paws step on them, plants
                                                                              // grow out of them (no corridor or pair checks)
    static readonly string[] BigPieces = { "BigLeaf", "Tree", "Banana", "Palm", "Heliconia" };   // composition: rotate first, shift <= 0.8 m
    static readonly string[] Removable = { "Grass", "Fern", "stone", "Pebble", "Twig", "Litter", "Cover", "Mushroom", "Seedling", "Rock_A", "Rock_B" };

    // recorded body sweep (tools/tiger_corridor.py from a unity/motion_test.sh recording): 5 cm cells, highest body point
    const float SweepCell = 0.05f, SweepMargin = 0.1f;
    static Dictionary<(int, int), float> LoadSweep(Vector2[] loop2, List<string> log)
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "sources", "tiger", "corridor.txt"));
        if (!System.IO.File.Exists(path)) { log.Add("clearance: no recorded tiger sweep (sources/tiger/corridor.txt): modelled body"); return null; }
        var cells = new Dictionary<(int, int), float>();
        foreach (var line in System.IO.File.ReadLines(path))
        {
            if (line.StartsWith("#")) continue;
            var v = line.Split(' '); cells[(int.Parse(v[0]), int.Parse(v[1]))] = float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture);
        }
        // stale (the loop changed since the recording): route points with no recorded body within 25 cm
        int miss = loop2.Count(p => !Enumerable.Range(-5, 11).Any(i => Enumerable.Range(-5, 11).Any(k => cells.ContainsKey((Mathf.FloorToInt(p.x / SweepCell) + i, Mathf.FloorToInt(p.y / SweepCell) + k)))));
        if (miss > loop2.Length / 10)
        {
            log.Add($"WARN clearance: recorded tiger sweep is stale ({miss}/{loop2.Length} route points uncovered): modelled body; re-record (tools/tiger_corridor.py)");
            return null;
        }
        log.Add($"clearance: recorded tiger sweep, {cells.Count} cells ({miss} route points uncovered)");
        return cells;
    }

    class ClearObj
    {
        public Transform t; public Vector3[] localSamples; public Transform[] sampleSpace; public float volume;
        public List<MeshCollider> cols = new();
        public IEnumerable<Vector3> Samples() { for (int i = 0; i < localSamples.Length; i++) yield return sampleSpace[i].TransformPoint(localSamples[i]); }
    }

    static void ResolveClearance(Transform root, List<Vector3> loop, List<string> log)
    {
        float x0 = loop.Min(p => p.x) - 3.6f, x1 = loop.Max(p => p.x) + 3.6f, z0 = 0f, z1 = loop.Max(p => p.z) + 3.6f;
        var objs = new List<ClearObj>(); var owner = new Dictionary<Collider, ClearObj>();
        foreach (var g in ClearanceGroups)
        {
            var gt = root.Find(g); if (!gt) continue;
            foreach (Transform t in gt)
            {
                var p = t.position; if (p.x < x0 || p.x > x1 || p.z < z0 || p.z > z1) continue;
                if (WalkedOver.Any(t.name.Contains)) continue;
                var o = new ClearObj { t = t };
                var loc = new List<Vector3>(); var space = new List<Transform>(); var b = new Bounds(p, Vector3.zero);
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
                {
                    var mr = mf.GetComponent<MeshRenderer>(); if (!mf.sharedMesh || !mr || !mr.enabled || mf.name.StartsWith("UCX_")) continue;
                    var lod = mf.GetComponentInParent<LODGroup>();
                    if (lod && lod.GetLODs().Length > 0 && !lod.GetLODs()[0].renderers.Contains(mr)) continue;   // LOD0 only
                    var m = mf.sharedMesh; var vs = m.vertices; var tris = m.triangles;
                    foreach (var v in vs) { loc.Add(v); space.Add(mf.transform); }
                    for (int i = 0; i < tris.Length; i += 3) { loc.Add((vs[tris[i]] + vs[tris[i + 1]] + vs[tris[i + 2]]) / 3f); space.Add(mf.transform); }
                    var c = mf.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = m; o.cols.Add(c); owner[c] = o;
                    b.Encapsulate(mr.bounds);
                }
                if (loc.Count == 0) continue;
                o.localSamples = loc.ToArray(); o.sampleSpace = space.ToArray(); o.volume = b.size.x * b.size.y * b.size.z;
                objs.Add(o);
            }
        }
        Physics.SyncTransforms();
        var loop2 = loop.Select(p => new Vector2(p.x, p.z)).ToArray();
        float LoopDist(Vector2 p) { float d = float.MaxValue; for (int i = 0; i < loop2.Length; i++) d = Mathf.Min(d, SegDist(p, loop2[i], loop2[(i + 1) % loop2.Length], out _)); return d; }
        // the swept body: at every 10 cm of the loop, tail base -> nose along the heading (in the U-turns the head swings
        // ~0.75 m outside the route: the route band alone missed it brushing a leaf at head height)
        var body = new List<(Vector2 a, Vector2 b)>();
        for (int i = 0; i < loop2.Length; i++)
        {
            Vector2 p0 = loop2[i], p1 = loop2[(i + 1) % loop2.Length]; float len = (p1 - p0).magnitude; var tdir = (p1 - p0) / Mathf.Max(len, 1e-4f);
            for (float d = 0; d < len; d += 0.1f) { var q = p0 + tdir * d; body.Add((q - tdir * BodyBack, q + tdir * BodyFront)); }
        }
        float BodyDist(Vector2 p)
        {
            if (LoopDist(p) > BodyFront + 0.6f) return float.MaxValue;
            float d = float.MaxValue; foreach (var (a, b) in body) d = Mathf.Min(d, SegDist(p, a, b, out _)); return d;
        }
        var sweep = LoadSweep(loop2, log);
        int CorridorHits(ClearObj o)
        {
            int n = 0;
            foreach (var s in o.Samples())
            {
                if (sweep != null)
                {   // recorded body: any cell within SweepMargin whose body top is above this point
                    int cx = Mathf.FloorToInt(s.x / SweepCell), cz = Mathf.FloorToInt(s.z / SweepCell), m = Mathf.CeilToInt(SweepMargin / SweepCell);
                    bool inside = false;
                    for (int i = -m; i <= m && !inside; i++)
                        for (int k = -m; k <= m && !inside; k++)
                            inside = sweep.TryGetValue((cx + i, cz + k), out var top) && s.y < top + SweepMargin;
                    if (!inside) continue;
                }
                else if (BodyDist(new Vector2(s.x, s.z)) > CorridorHalfWidth) continue;
                OnTerrain(s.x, s.z, out var hit); float h = s.y - hit.point.y;
                if (h > CorridorLow && h < CorridorHigh) n++;
            }
            return n;
        }
        var buf = new Collider[24];
        // samples of o within PairGap of another object's surface, per neighbour (both directions: a big triangle of one
        // can cross the other with no vertex of its own near it)
        Dictionary<ClearObj, int> Contacts(ClearObj o)
        {
            var res = new Dictionary<ClearObj, int>();
            void Add(ClearObj b, int k = 1) { if (b != o) res[b] = res.TryGetValue(b, out var c) ? c + k : k; }
            foreach (var s in o.Samples())
            {
                int n = Physics.OverlapSphereNonAlloc(s, PairGap, buf, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) if (owner.TryGetValue(buf[i], out var b)) Add(b);
            }
            var ob = o.cols.Select(c => c.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
            foreach (var other in objs)
            {
                if (other == o || !other.cols.Select(c => c.bounds).Any(bb => bb.Intersects(ob))) continue;
                int k = 0;
                foreach (var s in other.Samples())
                {
                    if (!ob.Contains(s)) continue;
                    int n = Physics.OverlapSphereNonAlloc(s, PairGap, buf, ~0, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++) if (owner.TryGetValue(buf[i], out var b) && b == o) { k++; break; }
                }
                if (k > 0) Add(other, k);
            }
            return res;
        }
        int Overlap(ClearObj o) => Contacts(o).Values.Where(v => v >= 3).Sum();
        void Place(ClearObj o, Vector3 xz, Quaternion rot, Vector3 scale)
        {
            var p = o.t.position; OnTerrain(p.x, p.z, out var h0); OnTerrain(xz.x, xz.z, out var h1);
            o.t.SetPositionAndRotation(new Vector3(xz.x, p.y + h1.point.y - h0.point.y, xz.z), rot); o.t.localScale = scale;
            Physics.SyncTransforms();
        }
        int Score(ClearObj o, int bound = int.MaxValue) { int c = CorridorHits(o) * 10; return c >= bound ? c : c + Overlap(o); }   // bound: skip the
                                                                                               // costly pair test when the corridor alone is worse
        // candidates (turn about the root, slide, both), cheapest first; the first fully clear one wins
        bool Relocate(ClearObj o, Vector3 away, string why)
        {
            var start = o.t.position; var rot0 = o.t.rotation; var sc0 = o.t.localScale;
            away.y = 0; if (away.sqrMagnitude < 1e-6f) away = Vector3.right; away.Normalize();
            bool big = BigPieces.Any(o.t.name.Contains);
            float maxShift = big ? 0.8f : MaxShift;
            var cands = new List<(float cost, Vector3 pos, float yaw)>();
            for (float r = 0f; r <= maxShift + 1e-3f; r += 0.08f)
                foreach (float ang in r == 0f ? new[] { 0f } : new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f })
                    for (float yaw = big ? -60f : -180f; yaw < (big ? 60.1f : 180f); yaw += 20f)
                    {
                        float cost = r * (big ? 2f : 1f) + Mathf.Abs(yaw) / 180f * (big ? 0.4f : 0.8f) + Mathf.Abs(ang) / 900f;
                        if (r == 0f && yaw == 0f) continue;
                        cands.Add((cost, start + Quaternion.AngleAxis(ang, Vector3.up) * away * r, yaw));
                    }
            int startScore = Score(o), bestScore = startScore; var best = (cost: 0f, pos: start, yaw: 0f); float bestScale = 1f;
            var tried = new List<(int score, (float cost, Vector3 pos, float yaw) c)> { (startScore, best) };
            foreach (var c in cands.OrderBy(c => c.cost))
            {
                if (bestScore == 0) break;
                Place(o, c.pos, Quaternion.AngleAxis(c.yaw, Vector3.up) * rot0, sc0);
                int sco = Score(o, big ? int.MaxValue : bestScore);
                tried.Add((sco, c));
                if (sco < bestScore) { bestScore = sco; best = c; }
            }
            if (bestScore > 0 && big && why.Contains("corridor"))
            {   // a composition piece still in the animal's way at its least intrusive spot: a little smaller (leaf tips)
                Place(o, best.pos, Quaternion.AngleAxis(best.yaw, Vector3.up) * rot0, sc0);
                foreach (float k in new[] { 0.92f, 0.85f })
                {
                    Place(o, best.pos, Quaternion.AngleAxis(best.yaw, Vector3.up) * rot0, sc0 * k);
                    int s2 = Score(o); if (s2 < bestScore) { bestScore = s2; bestScale = k; }
                    if (bestScore == 0) break;
                }
            }
            if (bestScore > 0 && big && !why.Contains("corridor"))
            {   // no clear spot from a neighbour: a composition piece weighs the overlap left against how much it changes
                // (moving a hero leaf 0.8 m and turning it 60 deg for a few samples less is not worth it). In the tiger's
                // way it always takes the least intrusion.
                var pick = tried.OrderBy(t => t.score + 30f * t.c.cost).First(); best = pick.c; bestScore = pick.score;
            }
            if (bestScore > 0 && big && !why.Contains("corridor"))                    // a big piece only half-cleared from a
            {                                                                          // neighbour keeps its composed spot
                Place(o, start, rot0, sc0);
                log.Add($"WARN clearance: {o.t.parent.name}/{o.t.name} kept (still {why} at every spot within 0.8 m / 60 deg)");
                return false;
            }
            Place(o, best.pos, Quaternion.AngleAxis(best.yaw, Vector3.up) * rot0, sc0 * bestScale);
            var d = new Vector2(best.pos.x - start.x, best.pos.z - start.z).magnitude;
            string change = $"{d:F2} m, turned {best.yaw:F0} deg" + (bestScale < 1f ? $", scaled {bestScale:F2}" : "") + $", score {startScore} -> {bestScore}";
            if (bestScore == 0) { if (d > 1e-3f || best.yaw != 0f || bestScale < 1f) log.Add($"clearance: {o.t.parent.name}/{o.t.name} {change} ({why})"); return true; }
            var ext = o.cols.Select(c => c.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; }).size;
            if (Removable.Any(o.t.name.Contains) && Mathf.Max(ext.x, ext.z) < 1f)                  // small pieces only
            {
                log.Add($"clearance: removed {o.t.parent.name}/{o.t.name} at ({start.x:F2}, {start.z:F2}) ({why}, no clear spot within {maxShift} m)");
                foreach (var c in o.cols) owner.Remove(c);
                objs.Remove(o); Object.DestroyImmediate(o.t.gameObject); Physics.SyncTransforms(); return true;
            }
            log.Add($"WARN clearance: {o.t.parent.name}/{o.t.name} still {why} after {change} (score {bestScore})");
            return false;
        }
        Vector3 AwayFromLoop(Vector3 p)
        {
            var q = new Vector2(p.x, p.z); Vector2 best = q; float bd = float.MaxValue;
            for (int i = 0; i < loop2.Length; i++)
            {
                Vector2 a0 = loop2[i], a1 = loop2[(i + 1) % loop2.Length]; float d = SegDist(q, a0, a1, out var t);
                if (d < bd) { bd = d; best = Vector2.Lerp(a0, a1, t); }
            }
            var a = q - best; return new Vector3(a.x, 0, a.y);
        }
        Vector3 Center(ClearObj o) => o.cols.Select(c => c.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; }).center;

        int moved0 = log.Count;
        // 1) corridor: biggest pieces first (they decide where the small ones can go)
        foreach (var o in objs.OrderByDescending(o => o.volume).ToList())
            if (objs.Contains(o) && CorridorHits(o) > 0) Relocate(o, AwayFromLoop(Center(o)), "in the tiger's corridor");
        // 2) pairs: the smaller of each interpenetrating pair moves away from the bigger, a few rounds
        for (int round = 0; round < 4; round++)
        {
            int fixedThisRound = 0;
            foreach (var o in objs.OrderBy(o => o.volume).ToList())
            {
                if (!objs.Contains(o)) continue;
                bool oBig = BigPieces.Any(o.t.name.Contains);                           // big on big: the layered jungle edge
                var big = Contacts(o).Where(kv => kv.Value >= 3 && kv.Key.volume >= o.volume && !(oBig && BigPieces.Any(kv.Key.t.name.Contains)))
                                     .OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
                if (big == null) continue;
                if (Relocate(o, Center(o) - Center(big), $"through {big.t.name}")) fixedThisRound++;
            }
            if (fixedThisRound == 0) break;
        }
        foreach (var o in objs) foreach (var c in o.cols) Object.DestroyImmediate(c);
        Physics.SyncTransforms();
        log.Add($"clearance: {log.Count - moved0} changes around the tiger loop ({objs.Count} objects checked)");
    }

    /// Clearance along an open route (the prey animals' stretches of path): pieces whose footprint reaches into the band
    /// (route +- halfWidth, below body height) are removed if small (Removable, < 1 m) or slid sideways out of it
    /// (big ones at most 0.8 m, like the loop pass). Ground cover the hooves walk over stays. Every change is logged.
    static void ClearRouteBand(Transform root, IList<Vector3> route, float halfWidth, string who, List<string> log)
    {
        var r2 = route.Select(p => new Vector2(p.x, p.z)).ToArray();
        float Dist(Vector2 p, out Vector2 away)
        {
            float best = float.MaxValue; away = Vector2.right;
            for (int i = 0; i + 1 < r2.Length; i++)
            {
                float d = SegDist(p, r2[i], r2[i + 1], out var u); var c = Vector2.Lerp(r2[i], r2[i + 1], u);
                if (d < best) { best = d; away = (p - c).sqrMagnitude > 1e-6f ? (p - c).normalized : Vector2.Perpendicular(r2[i + 1] - r2[i]).normalized; }
            }
            return best;
        }
        int removed = 0, moved = 0;
        foreach (var g in ClearanceGroups)
        {
            var gt = root.Find(g); if (!gt) continue;
            foreach (var t in gt.Cast<Transform>().ToArray())
            {
                if (WalkedOver.Any(t.name.Contains)) continue;
                var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.min.y > t.position.y + CorridorHigh) continue;                          // canopy above the body
                float radius = Mathf.Min(b.extents.x, b.extents.z) * 0.7f;                    // footprint core, not the leaf tips
                float d = Dist(new Vector2(t.position.x, t.position.z), out var away);
                if (d - radius > halfWidth) continue;
                bool small = Removable.Any(t.name.Contains) && b.size.y < 1f;
                if (small) { log.Add($"route clearance ({who}): removed {g}/{t.name} ({d:F2} m from the route)"); Object.DestroyImmediate(t.gameObject); removed++; continue; }
                float shift = Mathf.Min(halfWidth + radius - d + 0.05f, BigPieces.Any(t.name.Contains) ? 0.8f : MaxShift);
                var p = t.position + new Vector3(away.x, 0, away.y) * shift;
                OnTerrain(p.x, p.z, out var hit); p.y = hit.point.y;
                log.Add($"route clearance ({who}): {g}/{t.name} slid {shift:F2} m out of the route");
                t.position = p; moved++;
            }
        }
        log.Add($"route clearance ({who}): {removed} removed, {moved} moved (band +-{halfWidth} m)");
    }
}
