// Positions of objects whose name contains ARGS[0] (comma-separated terms), within 15 m of the viewer.
var cam = UnityEngine.Camera.main.transform.position; var terms = (ARGS.Length > 0 ? ARGS[0] : "Buttress").Split(',');
var sb = new System.Text.StringBuilder();
foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.Transform>(UnityEngine.FindObjectsSortMode.None))
{
    if (!terms.Any(x => t.name.Contains(x))) continue;
    var p = t.position; var d = new UnityEngine.Vector2(p.x - cam.x, p.z - cam.z).magnitude; if (d > 15) continue;
    var r = t.GetComponent<UnityEngine.Renderer>();
    sb.AppendLine($"{t.name} pos ({p.x:F2},{p.y:F2},{p.z:F2}) d {d:F1} scale {t.lossyScale.x:F2} {(r ? $"bounds c ({r.bounds.center.x:F1},{r.bounds.center.z:F1}) ext ({r.bounds.extents.x:F1},{r.bounds.extents.y:F1},{r.bounds.extents.z:F1})" : "")}");
}
return sb.ToString();
