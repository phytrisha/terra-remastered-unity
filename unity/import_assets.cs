// Import step after tools/sync_to_unity.sh copied Blender exports into Assets/Art/...
// Args (unity/args/import_assets.txt): one "Category Name" per line, e.g. "Props Crate".
// Refreshes, creates missing library materials (URP Lit + T_<Mat>_* maps), builds/rebuilds PF_<Name>, and reports
// tri counts and any slot still on an embedded (unmapped) material. Import settings come from AssetImportRules.cs.
var args = ARGS;
UnityEditor.AssetDatabase.Refresh();
var log = new System.Text.StringBuilder();
foreach (var line in args)
{
    var s = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
    if (s.Length < 2 || s[0].StartsWith("#")) continue;
    var cat = ProjectPaths.ParseCategory(s[0]);
    var model = (cat == ProjectPaths.Category.Character ? "SK_" : "SM_") + s[1];
    bool lightmapped = s.Length > 2 && s[2] == "lightmapped";
    var created = AssetPipeline.EnsureMaterials(model);
    if (created.Count > 0) log.AppendLine($"{model}: NEW materials {string.Join(", ", created)} (check them, then move textures/values into the library)");
    var lod0 = ProjectPaths.Model(model, cat);
    var unmapped = AssetPipeline.RemapMaterials(lod0);
    for (int l = 1; System.IO.File.Exists(lod0.Replace(".fbx", $"_LOD{l}.fbx")); l++)
        unmapped.AddRange(AssetPipeline.RemapMaterials(lod0.Replace(".fbx", $"_LOD{l}.fbx")));
    unmapped = unmapped.Distinct().ToList();
    log.AppendLine(AssetPipeline.MakePrefab(model, cat, lightmapped));
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(ProjectPaths.Model(model, cat));
    if (!go) continue;
    int tris = 0;
    foreach (var mf in go.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
        if (!mf.name.StartsWith("UCX_") && mf.sharedMesh)
            for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) tris += (int)(mf.sharedMesh.GetIndexCount(i) / 3);
    log.AppendLine($"  LOD0 tris {tris}" + (unmapped.Count > 0 ? $"  UNMAPPED {string.Join(", ", unmapped)}" : "  materials mapped"));
}
UnityEditor.AssetDatabase.SaveAssets();
return log.ToString();
