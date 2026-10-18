// Scene audit after placing or moving things. Args (unity/args/audit_scene.txt), optional: names of root GameObjects
// whose children are the placed objects (default: every root). Writes nothing; returns FAIL/WARN lines. Target: no FAIL.
// Checks: missing scripts/meshes/materials/pink shaders, objects floating above or buried in the ground (down rays
// from the bounds' footprint corners), placed objects whose colliders interpenetrate, and an optional walk test:
// a player-sized capsule (r 0.33, h 1.8) along waypoint lines "walk x z x z ..." in the args.
var args = ARGS;
var log = new System.Text.StringBuilder();
var problems = AssetPipeline.SceneProblems();
foreach (var l in problems.Split('\n', System.StringSplitOptions.RemoveEmptyEntries)) log.AppendLine("FAIL " + l);
UnityEngine.Physics.SyncTransforms();
var roots = args.Where(a => !a.StartsWith("walk")).Select(a => UnityEngine.GameObject.Find(a)).Where(g => g).ToList();
if (roots.Count == 0) roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().ToList();
var placed = roots.SelectMany(r => r.transform.Cast<UnityEngine.Transform>()).Where(t => t.GetComponentInChildren<UnityEngine.Renderer>()).ToList();
float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
// "ground" = every collider that is not part of a placed object (terrain, floors, roads)
var placedCols = new System.Collections.Generic.HashSet<UnityEngine.Collider>(placed.SelectMany(t => t.GetComponentsInChildren<UnityEngine.Collider>()));
int floating = 0, buried = 0, overlaps = 0;
foreach (var t in placed)
{
    var rs = t.GetComponentsInChildren<UnityEngine.Renderer>().Where(r => r.enabled && !r.name.StartsWith("UCX_")).ToArray();
    if (rs.Length == 0 || t.GetComponent<UnityEngine.Terrain>()) continue;
    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
    var own = new System.Collections.Generic.HashSet<UnityEngine.Collider>(t.GetComponentsInChildren<UnityEngine.Collider>());
    float worstGap = float.MinValue, worstSink = float.MinValue;
    foreach (var (dx, dz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f), (0f, 0f) })
    {
        var o = new UnityEngine.Vector3(b.center.x + dx * b.extents.x * 0.8f, b.max.y + 50f, b.center.z + dz * b.extents.z * 0.8f);
        var hits = UnityEngine.Physics.RaycastAll(o, UnityEngine.Vector3.down, 200f).Where(h => !own.Contains(h.collider)).OrderBy(h => h.distance).ToArray();
        // ground = first hit below the object's bottom, or the highest hit if everything is above it (buried)
        var below = hits.Where(h => h.point.y <= b.min.y + 0.05f).ToArray();
        if (below.Length > 0) worstGap = UnityEngine.Mathf.Max(worstGap, b.min.y - below[0].point.y);
        var above = hits.Where(h => !placedCols.Contains(h.collider) && h.point.y > b.min.y + 0.05f && h.point.y < b.max.y).ToArray();
        if (above.Length > 0) worstSink = UnityEngine.Mathf.Max(worstSink, above.Max(h => h.point.y) - b.min.y);
    }
    if (worstGap > 0.03f) { log.AppendLine($"FAIL floating {t.name} gap {worstGap:F2} m at {t.position}"); floating++; }
    if (worstSink > 0.45f) { log.AppendLine($"WARN buried {t.name} {worstSink:F2} m at {t.position}"); buried++; }
    foreach (var c in own)
    {
        if (c.isTrigger) continue;
        foreach (var other in UnityEngine.Physics.OverlapBox(c.bounds.center, c.bounds.extents * 0.95f))
        {
            if (own.Contains(other) || other.isTrigger || other is UnityEngine.TerrainCollider) continue;
            if (UnityEngine.Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, other, other.transform.position, other.transform.rotation, out _, out float d) && d > 0.05f)
            { log.AppendLine($"WARN overlap {t.name} x {other.transform.root.name}/{other.name} {d:F2} m"); overlaps++; }
        }
    }
}
int blocked = 0;
foreach (var w in args.Where(a => a.StartsWith("walk")))
{
    var v = w.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(F).ToArray();
    for (int i = 0; i + 3 < v.Length; i += 2)
    {
        var a = new UnityEngine.Vector2(v[i], v[i + 1]); var c2 = new UnityEngine.Vector2(v[i + 2], v[i + 3]);
        for (float s = 0; s <= (c2 - a).magnitude; s += 1f)
        {
            var p2 = UnityEngine.Vector2.MoveTowards(a, c2, s);
            var gh = UnityEngine.Physics.RaycastAll(new UnityEngine.Vector3(p2.x, 500, p2.y), UnityEngine.Vector3.down, 1000f)
                .Where(h => !placedCols.Contains(h.collider) && !h.collider.isTrigger).OrderBy(h => h.distance).ToArray();
            if (gh.Length == 0) continue;
            var g = gh[0];
            var bot = g.point + UnityEngine.Vector3.up * (0.33f + 0.3f); var top = g.point + UnityEngine.Vector3.up * (1.8f - 0.33f);
            if (UnityEngine.Physics.CheckCapsule(bot, top, 0.33f, ~0, UnityEngine.QueryTriggerInteraction.Ignore))
            { log.AppendLine($"FAIL walk blocked at ({p2.x:F1}, {p2.y:F1})"); blocked++; }
        }
    }
}
return $"objects {placed.Count}: floating {floating}, buried {buried}, overlaps {overlaps}, walk blocked {blocked}\n" + log;
