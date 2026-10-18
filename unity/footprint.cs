// xz footprint of named mesh renderers near a point: args "x z r nameFilter" -> per object: vertex xz extents, and a coarse occupancy row map
var a = ARGS[0].Split(" ", System.StringSplitOptions.RemoveEmptyEntries);
float px = float.Parse(a[0]), pz = float.Parse(a[1]), r = float.Parse(a[2]); string filt = a.Length > 3 ? a[3] : "";
var sb = new System.Text.StringBuilder();
foreach (var mf in UnityEngine.Object.FindObjectsByType<UnityEngine.MeshFilter>(UnityEngine.FindObjectsSortMode.None))
{
    var t = mf.transform; if (!mf.sharedMesh || (filt.Length > 0 && !t.name.Contains(filt))) continue;
    if (new UnityEngine.Vector2(t.position.x - px, t.position.z - pz).magnitude > r) continue;
    var v = mf.sharedMesh.vertices; float x0 = 1e9f, x1 = -1e9f, z0 = 1e9f, z1 = -1e9f, y1 = -1e9f;
    var cells = new System.Collections.Generic.HashSet<(int, int)>();
    foreach (var lv in v) { var w = t.TransformPoint(lv); x0 = UnityEngine.Mathf.Min(x0, w.x); x1 = UnityEngine.Mathf.Max(x1, w.x); z0 = UnityEngine.Mathf.Min(z0, w.z); z1 = UnityEngine.Mathf.Max(z1, w.z); y1 = UnityEngine.Mathf.Max(y1, w.y); cells.Add(((int)UnityEngine.Mathf.Floor(w.x * 10), (int)UnityEngine.Mathf.Floor(w.z * 10))); }
    sb.AppendLine($"{t.name} x {x0:F2}..{x1:F2} z {z0:F2}..{z1:F2} top {y1:F2}");
    for (int zi = (int)(z1 * 10); zi >= (int)UnityEngine.Mathf.Floor(z0 * 10); zi -= 1)
    {
        sb.Append($"  z{zi / 10f,5:F1} ");
        int xa = (int)UnityEngine.Mathf.Floor(x0 * 10), xb = (int)(x1 * 10);
        for (int xi = xa; xi <= xb; xi++) sb.Append(cells.Contains((xi, zi)) ? '#' : '.');
        sb.AppendLine($"  (x from {xa / 10f:F1})");
    }
}
return sb.ToString();
