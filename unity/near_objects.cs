// Trees (trunk base = transform position), palms, rocks and big plants within R m of the viewer, for route planning.
var cam = UnityEngine.Camera.main.transform.position; float R = 8f;
var sb = new System.Text.StringBuilder();
foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.MeshRenderer>(UnityEngine.FindObjectsSortMode.None))
{
    var n = t.name;
    if (!(n.Contains("Tree") || n.Contains("Palm") || n.Contains("Rock") || n.Contains("Boulder") || n.Contains("Banana") || n.Contains("BigLeaf") || n.Contains("Heliconia") || n.Contains("Log"))) continue;
    var p = t.transform.position; var d = new UnityEngine.Vector2(p.x - cam.x, p.z - cam.z).magnitude;
    if (d > R) continue;
    var e = t.bounds.extents;
    sb.AppendLine($"{n,-22} pos ({p.x:F2},{p.z:F2}) d {d:F1} scale {t.transform.lossyScale.x:F2} ext ({e.x:F1},{e.y:F1},{e.z:F1}) parent {t.transform.parent?.name}");
}
return sb.ToString();
