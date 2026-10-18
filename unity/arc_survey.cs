// Survey a circling arc around the viewer: per angle (deg from +Z, + = right) and radius, ground height and the
// renderers whose bounds intersect a 0.45 m sphere at 0.5 m above ground (excluding the terrain and tiny litter).
var cam = UnityEngine.Camera.main.transform.position; var c = new UnityEngine.Vector3(cam.x, 0, cam.z);
var rs = UnityEngine.Object.FindObjectsByType<UnityEngine.Renderer>(UnityEngine.FindObjectsSortMode.None)
    .Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is UnityEngine.ParticleSystemRenderer) && r.bounds.size.magnitude > 0.35f && r.bounds.size.magnitude < 40f && !r.name.Contains("Terrain") && !r.name.Contains("Water") && !r.name.Contains("Stream") && !r.name.Contains("Lake") && !r.name.Contains("Tiger") && !r.name.Contains("Tapir") && !r.name.Contains("Chital") && !r.name.Contains("Shaft") && !r.name.Contains("Floor") && !r.name.Contains("Ring")).ToArray();
var sb = new System.Text.StringBuilder();
foreach (var rad in new[] { 4.5f, 5f, 5.5f })
{
    sb.AppendLine($"r {rad}");
    for (int a = -90; a <= 90; a += 10)
    {
        var p = c + new UnityEngine.Vector3(UnityEngine.Mathf.Sin(a * UnityEngine.Mathf.Deg2Rad), 0, UnityEngine.Mathf.Cos(a * UnityEngine.Mathf.Deg2Rad)) * rad;
        float gy = -99; string gname = "";
        foreach (var h in UnityEngine.Physics.RaycastAll(p + UnityEngine.Vector3.up * 20, UnityEngine.Vector3.down, 40)) if (h.point.y > gy) { gy = h.point.y; gname = h.collider.name; }
        var s = new UnityEngine.Vector3(p.x, gy + 0.5f, p.z);
        var hits = rs.Where(r => r.bounds.SqrDistance(s) < 0.45f * 0.45f).Select(r => r.name.Replace("PF_", "").Replace("(Clone)", "")).Distinct().Take(4);
        sb.AppendLine($"  {a,4} ({p.x:F1},{p.z:F1}) y {gy:F2} [{gname}] {string.Join(", ", hits)}");
    }
}
return sb.ToString();
