// Clearance audit around the tiger's loop. Args (unity/args/clearance_audit.txt):
//   region x0 x1 z0 z1                 objects considered (children of the scene's top groups, by position)
//   radius r                           ... and within r m of the origin (the viewer): match the overlap pass's 58 m
//   points <file under ROOT>           "frame x y z r" body spheres (tools/tiger_points.py) -> objects the body touches
//   pairs                              plant/rock pairs whose surfaces interpenetrate (vertex + triangle-centre samples
//                                      of one inside / within 1.5 cm of the other's mesh)
//   skip <a,b,...>                     name parts left out (default Litter,Twig: flat ground cover, walked over)
// Temporary mesh colliders on every renderer of the considered objects; removed again. Returns one line per finding.
var reg = ARGS.First(l => l.StartsWith("region")).Split(' ').Skip(1).Select(float.Parse).ToArray();
var root = UnityEngine.GameObject.Find("Rainforest").transform;
string[] groups = { "Hero", "Jungle", "StreamRocks", "Forest", "PathDebris", "Floor", "Epiphytes", "Edges" };
var objs = new System.Collections.Generic.List<UnityEngine.Transform>();
var skip = (ARGS.FirstOrDefault(l => l.StartsWith("skip ")) ?? "skip Litter,Twig").Substring(5).Split(',');
var radLine = ARGS.FirstOrDefault(l => l.StartsWith("radius ")); float rad = radLine != null ? float.Parse(radLine.Substring(7), System.Globalization.CultureInfo.InvariantCulture) : 1e9f;
foreach (var g in groups) { var gt = root.Find(g); if (!gt) continue; foreach (UnityEngine.Transform t in gt) { var p = t.position; if (p.x >= reg[0] && p.x <= reg[1] && p.z >= reg[2] && p.z <= reg[3] && !skip.Any(t.name.Contains) && new UnityEngine.Vector2(p.x, p.z).magnitude <= rad) objs.Add(t); } }
var temp = new System.Collections.Generic.List<UnityEngine.MeshCollider>();
var owner = new System.Collections.Generic.Dictionary<UnityEngine.Collider, UnityEngine.Transform>();
foreach (var o in objs)
    foreach (var mf in o.GetComponentsInChildren<UnityEngine.MeshFilter>())
    {
        if (!mf.sharedMesh || mf.name.StartsWith("UCX_")) continue;
        var mr = mf.GetComponent<UnityEngine.MeshRenderer>(); if (!mr || !mr.enabled) continue;
        var lod = mf.GetComponentInParent<UnityEngine.LODGroup>();
        if (lod && lod.GetLODs().Length > 0 && !lod.GetLODs()[0].renderers.Contains(mr)) continue;   // LOD0 only
        var c = mf.gameObject.AddComponent<UnityEngine.MeshCollider>(); c.sharedMesh = mf.sharedMesh; temp.Add(c); owner[c] = o;
    }
UnityEngine.Physics.SyncTransforms();
var sb = new System.Text.StringBuilder();
string Desc(UnityEngine.Transform o) { var b = new UnityEngine.Bounds(o.position, UnityEngine.Vector3.zero); foreach (var r in o.GetComponentsInChildren<UnityEngine.Renderer>()) b.Encapsulate(r.bounds); return $"{o.parent.name}/{o.name} at ({o.position.x:F2}, {o.position.z:F2}) size ({b.size.x:F2}, {b.size.y:F2}, {b.size.z:F2})"; }
try
{
    var pl = ARGS.FirstOrDefault(l => l.StartsWith("points"));
    if (pl != null)
    {
        var hits = new System.Collections.Generic.Dictionary<UnityEngine.Transform, (int n, int f0, UnityEngine.Vector3 p, float depth)>();
        var buf = new UnityEngine.Collider[32];
        foreach (var line in System.IO.File.ReadLines(System.IO.Path.Combine(ROOT, pl.Split(' ')[1])))
        {
            var v = line.Split(' '); int f = int.Parse(v[0]); var p = new UnityEngine.Vector3(float.Parse(v[1]), float.Parse(v[2]), float.Parse(v[3])); float r = float.Parse(v[4]);
            int n = UnityEngine.Physics.OverlapSphereNonAlloc(p, r, buf, ~0, UnityEngine.QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (!owner.TryGetValue(buf[i], out var o)) continue;
                var h = hits.TryGetValue(o, out var e) ? e : (n: 0, f0: f, p: p, depth: 0f);
                hits[o] = (h.n + 1, h.f0, h.p, h.depth);
            }
        }
        sb.AppendLine($"body: {hits.Count} objects touched");
        foreach (var kv in hits.OrderByDescending(k => k.Value.n)) sb.AppendLine($"  TOUCH {kv.Value.n,5} spheres, first frame {kv.Value.f0,4} at ({kv.Value.p.x:F2}, {kv.Value.p.y:F2}, {kv.Value.p.z:F2}): {Desc(kv.Key)}");
    }
    if (ARGS.Any(l => l.StartsWith("pairs")))
    {
        var pairs = new System.Collections.Generic.Dictionary<(UnityEngine.Transform, UnityEngine.Transform), int>();
        var buf = new UnityEngine.Collider[16];
        foreach (var c in temp)
        {
            var a = owner[c]; var m = c.sharedMesh; var vs = m.vertices; var tr = c.transform; var tris = m.triangles;
            var samples = new System.Collections.Generic.List<UnityEngine.Vector3>(vs.Select(tr.TransformPoint));
            for (int i = 0; i < tris.Length; i += 3) samples.Add(tr.TransformPoint((vs[tris[i]] + vs[tris[i + 1]] + vs[tris[i + 2]]) / 3f));
            foreach (var s in samples)
            {
                int n = UnityEngine.Physics.OverlapSphereNonAlloc(s, 0.015f, buf, ~0, UnityEngine.QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    if (!owner.TryGetValue(buf[i], out var b) || b == a) continue;
                    if ((a.name.Contains("Roots") && b.name.Contains("ButtressTree")) || (b.name.Contains("Roots") && a.name.Contains("ButtressTree"))) continue;   // roots belong to the trunk
                    bool ea = a.parent.name == "Epiphytes", eb = b.parent.name == "Epiphytes";
                    bool Tree(UnityEngine.Transform x) => x.name.Contains("ButtressTree") || x.name.Contains("Tree_") || x.name.Contains("Palm");
                    if ((ea && (eb || Tree(b))) || (eb && Tree(a))) continue;                  // epiphytes sit on their tree
                    var key = string.CompareOrdinal(a.name + a.position.ToString("F3"), b.name + b.position.ToString("F3")) < 0 ? (a, b) : (b, a);
                    pairs[key] = pairs.TryGetValue(key, out var k) ? k + 1 : 1;
                }
            }
        }
        var real = pairs.Where(kv => kv.Value >= 3).OrderByDescending(kv => kv.Value).ToList();
        sb.AppendLine($"pairs: {real.Count} interpenetrating (>= 3 samples)");
        foreach (var kv in real) sb.AppendLine($"  PAIR {kv.Value,4}: {Desc(kv.Key.Item1)}  <->  {Desc(kv.Key.Item2)}");
    }
}
finally { foreach (var c in temp) UnityEngine.Object.DestroyImmediate(c); }
return sb.ToString();
