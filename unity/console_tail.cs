// Last console entries (errors / warnings) via the internal LogEntries API, for unattended checks.
var t = System.Type.GetType("UnityEditor.LogEntries, UnityEditor");
var e = System.Type.GetType("UnityEditor.LogEntry, UnityEditor");
int n = (int)t.GetMethod("StartGettingEntries").Invoke(null, null);
var sb = new System.Text.StringBuilder(); var entry = System.Activator.CreateInstance(e);
for (int i = System.Math.Max(0, n - 12); i < n; i++)
{
    t.GetMethod("GetEntryInternal").Invoke(null, new object[] { i, entry });
    var msg = (string)e.GetField("message").GetValue(entry);
    sb.AppendLine(msg.Split('\n')[0]);
}
t.GetMethod("EndGettingEntries").Invoke(null, null);
var hd = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(ProjectPaths.Find("SK_WhiteTigerHD.fbx")).GetComponentInChildren<UnityEngine.SkinnedMeshRenderer>().sharedMesh;
var l1 = new System.Collections.Generic.List<UnityEngine.Vector2>(); var l2 = new System.Collections.Generic.List<UnityEngine.Vector2>();
hd.GetUVs(1, l1); hd.GetUVs(2, l2);
float flowLen = 0, dmin = 1e9f, dmax = 0, lmean = 0; foreach (var v in l1) flowLen += v.magnitude; foreach (var v in l2) { dmin = UnityEngine.Mathf.Min(dmin, v.x); dmax = UnityEngine.Mathf.Max(dmax, v.x); lmean += v.y; }
sb.AppendLine($"HD uv1 {l1.Count} (mean |flow| {flowLen / UnityEngine.Mathf.Max(1, l1.Count):F2}), uv2 {l2.Count} (density {dmin:F2}-{dmax:F2}, mean length {lmean / UnityEngine.Mathf.Max(1, l2.Count):F2}), tangents {hd.tangents.Length}");
return sb.ToString();
