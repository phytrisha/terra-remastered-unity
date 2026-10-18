// Editor-side steps of the Blender -> Unity pipeline. Called from the eval scripts in ../unity/ (tools/unity_run.sh)
// and from the Tools/Asset Pipeline menu, so CLI runs and manual runs do exactly the same thing.
//
//   AssetPipeline.MakePrefab("SM_Crate", Category.Prop)   PF_Crate next to the model: LODGroup, UCX -> convex
//                                                          MeshColliders, static flags, GI mode, shadow rules
//   AssetPipeline.EnsureMaterials("SM_Crate")             a URP Lit material in the library for every material slot
//                                                          that has no project material yet (picks up T_<Mat>_* maps)
//   AssetPipeline.SceneProblems()                          missing scripts / meshes / materials in the open scenes
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class AssetPipeline
{
    // LOD switch heights (screen-relative). Tune per project and keep them here.
    public static readonly Dictionary<ProjectPaths.Category, float[]> LodThresholds = new()
    {
        { ProjectPaths.Category.Building, new[] { 0.3f, 0.12f, 0.025f } },
        { ProjectPaths.Category.Prop, new[] { 0.25f, 0.08f, 0.02f } },
        { ProjectPaths.Category.Nature, new[] { 0.14f, 0.04f, 0.012f } },
        { ProjectPaths.Category.Character, new[] { 0.2f, 0.06f, 0.01f } },
    };
    public const float NoShadowBelow = 1.5f;     // renderers with a smaller bounds diagonal cast no shadows

    const StaticEditorFlags Full = StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                                   StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                                   StaticEditorFlags.ReflectionProbeStatic;
    const StaticEditorFlags Far = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;

    /// <summary>Build or rebuild PF_&lt;Name&gt; next to SM_&lt;Name&gt;.fbx. lightmapped: LOD0 gets baked lightmaps
    /// (walk-in or hero assets); otherwise everything is light-probe lit, so LOD or model changes need no rebake.</summary>
    public static string MakePrefab(string modelName, ProjectPaths.Category cat, bool lightmapped = false)
    {
        var lod0Path = ProjectPaths.Model(modelName, cat);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(lod0Path) == null) return $"MISSING {lod0Path}";
        var baseName = ProjectPaths.BaseName(modelName);
        var root = new GameObject(baseName);
        var th = LodThresholds[cat];
        var lods = new List<LOD>();
        bool isStaticKind = cat != ProjectPaths.Category.Character;
        for (int l = 0; l < th.Length; l++)
        {
            var path = l == 0 ? lod0Path : lod0Path.Replace(".fbx", $"_LOD{l}.fbx");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!model) break;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            inst.transform.localPosition = Vector3.zero;  // keep the model root's own rotation: single-mesh FBX carry the axis conversion there
            var rs = new List<Renderer>();
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name.StartsWith("UCX_"))
                {
                    if (l == 0) { var mc = r.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh; mc.convex = true; }
                    Object.DestroyImmediate(r);
                    if (l != 0) Object.DestroyImmediate(r.GetComponent<MeshFilter>());
                    continue;
                }
                if (isStaticKind)
                {
                    var flags = cat == ProjectPaths.Category.Nature ? StaticEditorFlags.OccludeeStatic : (l == 0 ? Full : Far);
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
                }
                r.receiveGI = lightmapped && l == 0 ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
                if (r.bounds.size.magnitude < NoShadowBelow) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rs.Add(r);
            }
            lods.Add(new LOD(th[l], rs.ToArray()));
        }
        if (lods.Count > 1)
        {
            var lg = root.AddComponent<LODGroup>(); lg.SetLODs(lods.ToArray()); lg.RecalculateBounds();
            if (cat == ProjectPaths.Category.Nature) { lg.fadeMode = LODFadeMode.CrossFade; lg.animateCrossFading = true; }
        }
        var prefabPath = ProjectPaths.Prefab("PF_" + baseName);
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        return $"{prefabPath} lods={lods.Count}";
    }

    /// <summary>Create library materials for slots without one, wiring T_&lt;Base&gt;_{BC,N,ORM|MS,E} if they exist.
    /// "MI_Wood" looks for T_Wood_*; "M_Crate" looks for T_Crate_*. Returns the names it created.</summary>
    public static List<string> EnsureMaterials(string modelName)
    {
        var created = new List<string>();
        var modelPath = ProjectPaths.Model(modelName);
        var mi = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        // a remap to a deleted library material hides the embedded one (and its Blender colour): drop stale remaps first
        var stale = mi.GetExternalObjectMap().Where(kv => kv.Key.type == typeof(Material) && kv.Value == null).Select(kv => kv.Key).ToList();
        foreach (var id in stale) mi.RemoveRemap(id);
        if (stale.Count > 0) mi.SaveAndReimport();
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var embedded = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().GroupBy(m => m.name).ToDictionary(g => g.Key, g => g.First());
        var names = embedded.Keys.Concat(mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).Select(k => k.name))
                    .Where(n => n != "No Name").Distinct();
        foreach (var name in names)
        {
            if (ProjectPaths.Find(name + ".mat") != null) continue;
            var m = new Material(lit) { name = name, enableInstancing = true };
            if (embedded.TryGetValue(name, out var src))          // keep the colour / smoothness / metal authored in Blender
            {
                var c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
                m.SetColor("_BaseColor", c.gamma);          // Blender writes linear values; Unity reads FBX colours as sRGB
                if (src.HasProperty("_Metallic")) m.SetFloat("_Metallic", src.GetFloat("_Metallic"));
                if (src.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", src.GetFloat("_Smoothness"));
                else if (src.HasProperty("_Glossiness")) m.SetFloat("_Smoothness", src.GetFloat("_Glossiness"));
            }
            var stem = name.StartsWith("MI_") ? name.Substring(3) : name.StartsWith("M_") ? name.Substring(2) : name;
            Texture2D T(string s) => AssetDatabase.LoadAssetAtPath<Texture2D>(ProjectPaths.Find($"T_{stem}_{s}.png") ?? "");
            if (T("BC")) m.SetTexture("_BaseMap", T("BC"));
            if (T("N")) { m.SetTexture("_BumpMap", T("N")); m.EnableKeyword("_NORMALMAP"); }
            if (T("MS")) { m.SetTexture("_MetallicGlossMap", T("MS")); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", 1f); }
            if (T("AO")) { m.SetTexture("_OcclusionMap", T("AO")); m.EnableKeyword("_OCCLUSIONMAP"); }
            if (T("E")) { m.SetTexture("_EmissionMap", T("E")); m.SetColor("_EmissionColor", Color.white); m.EnableKeyword("_EMISSION"); }
            AssetDatabase.CreateAsset(m, ProjectPaths.Mat(name));
            created.Add(name);
        }
        if (created.Count > 0) AssetDatabase.SaveAssets();
        return created;
    }

    /// <summary>Remap every embedded material of a model onto the project material of the same name, synchronously
    /// (AssetImportRules does the same after drag-and-drop imports, deferred). Call it for every LOD file.
    /// Returns names still unmapped.</summary>
    public static List<string> RemapMaterials(string modelPath)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        var unmapped = new List<string>(); bool changed = false;
        foreach (var id in mi.GetExternalObjectMap().Where(kv => kv.Key.type == typeof(Material) && kv.Value == null).Select(kv => kv.Key).ToList())
        { mi.RemoveRemap(id); changed = true; }                   // remaps to deleted materials
        var map = mi.GetExternalObjectMap();
        foreach (var m in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().Select(m => m.name)
                     .Concat(map.Keys.Where(k => k.type == typeof(Material)).Select(k => k.name)).Distinct())
        {
            if (m == "No Name") continue;                         // UCX hulls carry no material
            var path = ProjectPaths.Find(m + ".mat");
            if (path == null) { unmapped.Add(m); continue; }
            var lib = AssetDatabase.LoadAssetAtPath<Material>(path);
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), m);
            if (!map.TryGetValue(id, out var cur) || cur != lib) { mi.AddRemap(id, lib); changed = true; }
        }
        if (changed) mi.SaveAndReimport();
        return unmapped;
    }

    /// <summary>Missing scripts, meshes and materials in every open scene. Target: empty.</summary>
    public static string SceneProblems()
    {
        var sb = new StringBuilder();
        bool srp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != null;
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) > 0) sb.AppendLine($"missing script: {HierPath(go)}");
            var mf = go.GetComponent<MeshFilter>(); if (mf && !mf.sharedMesh) sb.AppendLine($"missing mesh: {HierPath(go)}");
            var r = go.GetComponent<Renderer>(); if (r && r.sharedMaterials.Any(m => m == null)) sb.AppendLine($"missing material: {HierPath(go)}");
            if (r && r.sharedMaterials.Any(m => m && m.shader && (m.shader.name == "Hidden/InternalErrorShader" || !m.shader.isSupported
                    || (srp && (m.shader.name == "Standard" || m.shader.name.StartsWith("Legacy Shaders/"))))))
                sb.AppendLine($"pink shader (not URP): {HierPath(go)}");
        }
        return sb.ToString();
    }

    static string HierPath(GameObject go) => go.transform.parent ? HierPath(go.transform.parent.gameObject) + "/" + go.name : go.name;

    [MenuItem("Tools/Asset Pipeline/Make Prefab For Selected Model")]
    static void MakeSelected()
    {
        foreach (var o in Selection.objects)
        {
            var p = AssetDatabase.GetAssetPath(o);
            if (!p.EndsWith(".fbx")) continue;
            var cat = p.Contains("/Buildings/") ? ProjectPaths.Category.Building : p.Contains("/Nature/") ? ProjectPaths.Category.Nature
                    : p.Contains("/Characters/") ? ProjectPaths.Category.Character : ProjectPaths.Category.Prop;
            Debug.Log(MakePrefab(System.IO.Path.GetFileNameWithoutExtension(p), cat));
        }
    }

    [MenuItem("Tools/Asset Pipeline/Report Scene Problems")]
    static void ReportProblems() { var s = SceneProblems(); Debug.Log(string.IsNullOrEmpty(s) ? "No scene problems." : s); }
}
