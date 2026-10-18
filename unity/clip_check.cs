// Root-motion facts of the tiger clips (low-poly + HD): average speed (m/s), angular speed (deg/s), root curves.
var sb = new System.Text.StringBuilder();
foreach (var f in new[] { "SK_WhiteTiger.fbx", "SK_WhiteTigerHD.fbx" })
{
    var path = ProjectPaths.Find(f); var mi = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(path);
    sb.AppendLine($"{f}: motion node '{mi.motionNodeName}'");
    foreach (var c in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path).OfType<UnityEngine.AnimationClip>().Where(c => !c.name.StartsWith("__")))
        sb.AppendLine($"  {c.name,-10} len {c.length:F2}s loop {c.isLooping} avgSpeed {c.averageSpeed.magnitude:F3} ({c.averageSpeed.x:F2},{c.averageSpeed.y:F2},{c.averageSpeed.z:F2}) angular {c.averageAngularSpeed:F1} rootT {c.hasGenericRootTransform} motionCurves {c.hasMotionCurves}");
}
return sb.ToString();
