// Terrain_Mesh height grid: args "x0 x1 z0 z1 step" -> rows of heights (cm), z descending, plus obstacles (rock/tree/leaf bounds) marked
var a = ARGS[0].Split(" ", System.StringSplitOptions.RemoveEmptyEntries);
float x0 = float.Parse(a[0]), x1 = float.Parse(a[1]), z0 = float.Parse(a[2]), z1 = float.Parse(a[3]), st = float.Parse(a[4]);
var col = UnityEngine.GameObject.Find("Rainforest/Terrain/Terrain_Mesh").GetComponent<UnityEngine.MeshCollider>();
var sb = new System.Text.StringBuilder("z\\x ");
for (float x = x0; x <= x1 + 1e-3f; x += st) sb.Append($"{x,6:F1}");
sb.AppendLine();
for (float z = z1; z >= z0 - 1e-3f; z -= st)
{
    sb.Append($"{z,5:F1} ");
    for (float x = x0; x <= x1 + 1e-3f; x += st)
    {
        var p = new UnityEngine.Vector3(x, 20f, z);
        float y = col.Raycast(new UnityEngine.Ray(p, UnityEngine.Vector3.down), out var h, 40f) ? h.point.y : float.NaN;
        bool blocked = false;
        foreach (var c in UnityEngine.Physics.OverlapSphere(new UnityEngine.Vector3(x, y + 0.4f, z), 0.15f)) if (c != col && !c.isTrigger && !c.transform.root.name.Contains("WhiteTiger") && c.attachedRigidbody == null) blocked = true;
        sb.Append(blocked ? $"{(int)(y * 100),5}#" : $"{(int)(y * 100),6}");
    }
    sb.AppendLine();
}
return sb.ToString();
