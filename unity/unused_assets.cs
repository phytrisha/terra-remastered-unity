// Unused-asset report: everything under Assets/ that no root depends on. Roots: every scene under Scenes/ and the
// assets referenced by project settings (render pipelines, quality levels, URP global settings, preloaded assets).
// Always kept: code/shaders/UI, Settings/, Terrain/Source (loaded only by scripts), README.md.
// Writes <root>/unity/reports/unused_assets.tsv. Before deleting anything: note the "used" count, delete through
// AssetDatabase.DeleteAsset, rerun, and check that "used" did not drop and the scene has no missing references.
var roots = new System.Collections.Generic.List<string>(UnityEditor.AssetDatabase.FindAssets("t:Scene", new[] { ProjectPaths.Scenes }).Select(UnityEditor.AssetDatabase.GUIDToAssetPath));
void AddObj(UnityEngine.Object o) { if (o) { var p = UnityEditor.AssetDatabase.GetAssetPath(o); if (!string.IsNullOrEmpty(p)) roots.Add(p); } }
AddObj(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline);
for (int i = 0; i < UnityEngine.QualitySettings.names.Length; i++) AddObj(UnityEngine.QualitySettings.GetRenderPipelineAssetAt(i));
foreach (var o in UnityEditor.PlayerSettings.GetPreloadedAssets()) AddObj(o);
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:UniversalRenderPipelineGlobalSettings")) roots.Add(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
var used = new System.Collections.Generic.HashSet<string>(UnityEditor.AssetDatabase.GetDependencies(roots.ToArray(), true));
string[] keepExt = { ".cs", ".shader", ".hlsl", ".cginc", ".shadergraph", ".compute", ".asmdef", ".asmref", ".inputactions", ".uss", ".uxml", ".md" };
var sb = new System.Text.StringBuilder(); long total = 0; int n = 0;
foreach (var p in UnityEditor.AssetDatabase.GetAllAssetPaths())
{
    if (!p.StartsWith("Assets/") || UnityEditor.AssetDatabase.IsValidFolder(p) || p.StartsWith("Assets/ThirdParty/")) continue;
    if (p.StartsWith(ProjectPaths.Settings + "/") || p.StartsWith(ProjectPaths.TerrainSource + "/")) continue;
    if (System.Array.Exists(keepExt, e => p.EndsWith(e)) || used.Contains(p)) continue;
    long sz = new System.IO.FileInfo(p).Length; total += sz; n++;
    sb.AppendLine($"{sz}\t{p}");
}
var dir = System.IO.Path.Combine(ROOT, "unity", "reports"); System.IO.Directory.CreateDirectory(dir);
System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "unused_assets.tsv"), sb.ToString());
return $"roots {roots.Count}, used {used.Count}, unused {n} files, {total / 1048576.0:F1} MB";
