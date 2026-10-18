// Delete project assets through the AssetDatabase (never rm in Assets/): ARGS[0] = comma-separated asset paths.
var res = new System.Text.StringBuilder();
foreach (var p in (ARGS.Length > 0 ? ARGS[0] : "").Split(',').Where(x => x.Length > 0))
    res.AppendLine($"{p}: {(UnityEditor.AssetDatabase.DeleteAsset(p.Trim()) ? "deleted" : "not found")}");
return res.ToString();
