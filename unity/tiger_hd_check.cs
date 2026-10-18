// Report the SK_WhiteTigerHD import: rig type, avatar, textures, material, readability, and whether its bind poses match
// SK_WhiteTiger bone for bone (required: the HD mesh is skinned to the low-poly tiger's bones at runtime; the HD-only
// ear bones earHD_L/R are reported as missing and created by RainforestBuilder.AttachHdTiger).
//   tools/unity_run.sh unity/tiger_hd_check.cs [reimport]
var args = string.Join(" ", ARGS);
var sb = new System.Text.StringBuilder();
string hdPath = ProjectPaths.Find("SK_WhiteTigerHD.fbx"), loPath = ProjectPaths.Find("SK_WhiteTiger.fbx");
if (args.Contains("reimport"))
{
    foreach (var t in new[] { "BC", "N", "MS" }) UnityEditor.AssetDatabase.ImportAsset(ProjectPaths.Find($"T_WhiteTigerHD_{t}.png"), UnityEditor.ImportAssetOptions.ForceUpdate);
    UnityEditor.AssetDatabase.ImportAsset(hdPath, UnityEditor.ImportAssetOptions.ForceUpdate);
    UnityEditor.AssetDatabase.ImportAsset(loPath, UnityEditor.ImportAssetOptions.ForceUpdate);
}
var mi = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(hdPath);
sb.AppendLine($"rig {mi.animationType}, clips {string.Join(",", UnityEditor.AssetDatabase.LoadAllAssetsAtPath(hdPath).OfType<UnityEngine.AnimationClip>().Where(c => !c.name.StartsWith("__")).Select(c => c.name))}");
var av = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(hdPath).OfType<UnityEngine.Avatar>().FirstOrDefault();
sb.AppendLine($"avatar {(av ? (av.isValid ? "valid" : "INVALID") : "none")}");
foreach (var t in new[] { "BC", "N", "MS" })
{
    var p = ProjectPaths.Find($"T_WhiteTigerHD_{t}.png"); var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(p);
    sb.AppendLine($"T_{t}: max {ti.maxTextureSize}, type {ti.textureType}, sRGB {ti.sRGBTexture}");
}
var m = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(ProjectPaths.Find("M_WhiteTigerHD.mat"));
if (m) sb.AppendLine($"material {m.shader.name}: base {m.GetTexture("_BaseMap")?.name}, normal {m.GetTexture("_BumpMap")?.name}, ms {m.GetTexture("_MetallicGlossMap")?.name}, color {m.GetColor("_BaseColor")}");
UnityEngine.SkinnedMeshRenderer Smr(string p) => UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p).GetComponentInChildren<UnityEngine.SkinnedMeshRenderer>();
var hd = Smr(hdPath); var lo = Smr(loPath);
sb.AppendLine($"hd: {hd.sharedMesh.vertexCount} verts, {hd.sharedMesh.triangles.Length / 3} tris, {hd.bones.Length} bones, root {hd.rootBone?.name}, smr path {UnityEditor.AnimationUtility.CalculateTransformPath(hd.transform, hd.transform.root)}");
sb.AppendLine($"readable (particle shapes): hd {hd.sharedMesh.isReadable}, lo {lo.sharedMesh.isReadable}");
sb.AppendLine($"lo: {lo.sharedMesh.vertexCount} verts, {lo.bones.Length} bones, root {lo.rootBone?.name}, smr path {UnityEditor.AnimationUtility.CalculateTransformPath(lo.transform, lo.transform.root)}");
var loIdx = new System.Collections.Generic.Dictionary<string, int>();
for (int i = 0; i < lo.bones.Length; i++) loIdx[lo.bones[i].name] = i;
float worst = 0; int missing = 0; string worstBone = "";
// compare bone matrices in model space (bindpose relative to each model root) and the SMR local transforms
for (int i = 0; i < hd.bones.Length; i++)
{
    if (!loIdx.TryGetValue(hd.bones[i].name, out var j)) { missing++; continue; }
    var a = hd.sharedMesh.bindposes[i] * (hd.transform.root.worldToLocalMatrix * hd.transform.localToWorldMatrix).inverse;
    var b = lo.sharedMesh.bindposes[j] * (lo.transform.root.worldToLocalMatrix * lo.transform.localToWorldMatrix).inverse;
    float d = 0; for (int k = 0; k < 16; k++) d = UnityEngine.Mathf.Max(d, UnityEngine.Mathf.Abs(a[k] - b[k]));
    if (d > worst) { worst = d; worstBone = hd.bones[i].name; }
}
sb.AppendLine($"bindpose match: missing {missing}, worst |diff| {worst:E2} ({worstBone})");
return sb.ToString();
