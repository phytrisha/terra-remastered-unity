// Terrain height profile: args "x0 z0 x1 z1 n" -> every hit (collider name, y) at n points along the segment
var a = ARGS[0].Split(" ", System.StringSplitOptions.RemoveEmptyEntries);
float x0 = float.Parse(a[0]), z0 = float.Parse(a[1]), x1 = float.Parse(a[2]), z1 = float.Parse(a[3]); int n = int.Parse(a[4]);
var sb = new System.Text.StringBuilder();
for (int i = 0; i <= n; i++)
{
    float t = i / (float)n; var p = new UnityEngine.Vector3(UnityEngine.Mathf.Lerp(x0, x1, t), 20f, UnityEngine.Mathf.Lerp(z0, z1, t));
    sb.Append($"({p.x:F2},{p.z:F2}):");
    foreach (var h in UnityEngine.Physics.RaycastAll(p, UnityEngine.Vector3.down, 40f)) sb.Append($" {h.collider.name}@{h.point.y:F3}");
    sb.AppendLine();
}
return sb.ToString();
