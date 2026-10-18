// Objects per top group in a region (x0 x1 z0 z1): name prefix counts, for planning clearance passes.
var a = ARGS[0].Split(' '); float x0 = float.Parse(a[1]), x1 = float.Parse(a[2]), z0 = float.Parse(a[4]), z1 = float.Parse(a[5]);
var root = UnityEngine.GameObject.Find("Rainforest").transform; var sb = new System.Text.StringBuilder();
foreach (UnityEngine.Transform g in root)
{
    var c = new System.Collections.Generic.Dictionary<string, int>();
    foreach (UnityEngine.Transform t in g) { var p = t.position; if (p.x < x0 || p.x > x1 || p.z < z0 || p.z > z1) continue; var n = System.Text.RegularExpressions.Regex.Replace(t.name, @"[\s_]*\(?\d+\)?$", ""); c[n] = c.TryGetValue(n, out var k) ? k + 1 : 1; }
    if (c.Count > 0) sb.AppendLine(g.name + ": " + string.Join(", ", c.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value)));
}
return sb.ToString();
