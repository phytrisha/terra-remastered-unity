// Mesh points of named objects near the tiger's route (AnimalWander.route on PF_WhiteTiger): args "name x z half"
// -> how many vertices lie within <half> m of the route, their height above the ground and where.
var a = ARGS[0].Split(' '); string nm = a[0]; float ox = float.Parse(a[1]), oz = float.Parse(a[2]), half = float.Parse(a[3]);
var w = UnityEngine.GameObject.Find("PF_WhiteTiger").GetComponent<AnimalWander>(); var R = w.route;
var col = UnityEngine.GameObject.Find("Rainforest/Terrain/Terrain_Mesh").GetComponent<UnityEngine.MeshCollider>();
var t = UnityEngine.Object.FindObjectsByType<UnityEngine.Transform>(UnityEngine.FindObjectsSortMode.None).Where(x => x.name == nm).OrderBy(x => (new UnityEngine.Vector2(x.position.x - ox, x.position.z - oz)).magnitude).First();
var sb = new System.Text.StringBuilder($"{t.name} at {t.position} rot {t.eulerAngles.y:F0} scale {t.localScale.x:F2}\n");
foreach (var mf in t.GetComponentsInChildren<UnityEngine.MeshFilter>())
{
    var lod = mf.GetComponentInParent<UnityEngine.LODGroup>(); var mr = mf.GetComponent<UnityEngine.MeshRenderer>();
    if (lod && !lod.GetLODs()[0].renderers.Contains(mr)) continue;
    foreach (var v in mf.sharedMesh.vertices)
    {
        var p = mf.transform.TransformPoint(v); var q = new UnityEngine.Vector2(p.x, p.z); float best = 1e9f;
        for (int i = 0; i < R.Length; i++) { var a0 = new UnityEngine.Vector2(R[i].x, R[i].z); var a1 = new UnityEngine.Vector2(R[(i + 1) % R.Length].x, R[(i + 1) % R.Length].z); var ab = a1 - a0; float tt = UnityEngine.Mathf.Clamp01(UnityEngine.Vector2.Dot(q - a0, ab) / ab.sqrMagnitude); best = UnityEngine.Mathf.Min(best, (q - (a0 + ab * tt)).magnitude); }
        if (best > half) continue;
        col.Raycast(new UnityEngine.Ray(new UnityEngine.Vector3(p.x, 50, p.z), UnityEngine.Vector3.down), out var hit, 100f);
        sb.AppendLine($"  ({p.x:F2}, {p.z:F2}) h {p.y - hit.point.y:F2} d {best:F2}");
    }
}
return sb.ToString();
