// Curves on the tiger root bone inside the imported Walk / WalkTurnL clips: start / end values.
var path = ProjectPaths.Find("SK_WhiteTiger.fbx"); var sb = new System.Text.StringBuilder();
foreach (var c in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path).OfType<UnityEngine.AnimationClip>().Where(c => c.name == "Walk" || c.name == "WalkTurnL"))
{
    sb.AppendLine(c.name);
    foreach (var b in UnityEditor.AnimationUtility.GetCurveBindings(c).Where(b => b.path.EndsWith("/root") || b.path == "WhiteTiger_Rig" || b.path == "" ))
    {
        var cv = UnityEditor.AnimationUtility.GetEditorCurve(c, b);
        sb.AppendLine($"  '{b.path}' {b.propertyName}: {cv.Evaluate(0):F4} -> {cv.Evaluate(c.length):F4} ({cv.length} keys)");
    }
}
var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
var rig = model.transform.Find("WhiteTiger_Rig"); var root = rig.Find("root");
sb.AppendLine($"rig local pos {rig.localPosition} rot {rig.localEulerAngles} scale {rig.localScale}; root local pos {root.localPosition} rot {root.localEulerAngles} scale {root.localScale}");
return sb.ToString();
