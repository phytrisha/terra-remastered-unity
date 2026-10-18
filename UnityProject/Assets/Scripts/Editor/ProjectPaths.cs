// Single source of truth for where project assets live (see Assets/README.md and the unity-project-structure skill).
// Editor tools, importers and the pipeline eval scripts in ../unity/ resolve paths through this class instead of
// hard-coding folders, so the layout can change in one place.
//
//   ProjectPaths.Mat("MI_Brass")                 existing material, or the canonical path for a new one (folder created)
//   ProjectPaths.Tex("T_Brass_BC")               same for textures (.png)
//   ProjectPaths.Model("SM_Crate")               existing model; new ones need a category: Model(name, Category.Prop)
//   ProjectPaths.Prefab("PF_Crate")              existing prefab, or next to its SM_ model
//   ProjectPaths.AssetFolder(Category.Prop, "Crate", create: true)
using System;
using System.IO;
using System.Linq;
using UnityEditor;

public static class ProjectPaths
{
    public const string Root = "Assets";                    // first-party content; vendor content goes to ThirdParty/ or Packages
    public const string Art = Root + "/Art";
    public const string Environment = Art + "/Environment";
    public const string Buildings = Environment + "/Buildings";
    public const string Props = Environment + "/Props";
    public const string Nature = Environment + "/Nature";
    public const string Terrain = Environment + "/Terrain";
    public const string TerrainLayers = Terrain + "/Layers";
    public const string TerrainTextures = Terrain + "/Textures";
    public const string TerrainSource = Terrain + "/Source";   // read-only originals that terrain scripts rebuild from
    public const string Characters = Art + "/Characters";
    public const string Materials = Art + "/Materials";
    public const string Textures = Art + "/Textures";
    public const string VFX = Art + "/VFX";
    public const string VFXMaterials = VFX + "/Materials";
    public const string VFXTextures = VFX + "/Textures";
    public const string UI = Art + "/UI";                   // UXML/USS per screen: UI/<Screen>/
    public const string Shaders = Root + "/Shaders";
    public const string Scripts = Root + "/Scripts";
    public const string Scenes = Root + "/Scenes";
    public const string Sandbox = Scenes + "/Sandbox";
    public const string Settings = Root + "/Settings";
    public const string RenderingSettings = Settings + "/Rendering";
    public const string PostProcessing = Settings + "/PostProcessing";
    public const string LightingSettings = Settings + "/Lighting";
    public const string UISettings = Settings + "/UI";       // PanelSettings, runtime theme
    public const string Data = Settings + "/Data";           // ScriptableObject data assets

    /// <summary>Export folder names of blender/export/&lt;Category&gt;/ map 1:1 onto these.</summary>
    public enum Category { Building, Prop, Nature, Character }

    public static Category ParseCategory(string s) => s.TrimEnd('s').ToLowerInvariant() switch
    {
        "building" => Category.Building, "nature" => Category.Nature, "character" => Category.Character, _ => Category.Prop,
    };

    /// <summary>Folder of one asset (model + LODs + prefab + anything unique to it).</summary>
    public static string AssetFolder(Category c, string assetName, bool create = false)
    {
        var parent = c switch { Category.Building => Buildings, Category.Nature => Nature, Category.Character => Characters, _ => Props };
        var path = $"{parent}/{BaseName(assetName)}";
        if (create) EnsureFolder(path);
        return path;
    }

    /// <summary>"SM_Crate_LOD1" / "PF_Crate" / "Crate" -> "Crate".</summary>
    public static string BaseName(string name)
    {
        var n = Path.GetFileNameWithoutExtension(name);
        foreach (var p in new[] { "SM_", "SK_", "PF_" }) if (n.StartsWith(p)) n = n.Substring(p.Length);
        var lod = n.LastIndexOf("_LOD", StringComparison.Ordinal);
        return lod > 0 ? n.Substring(0, lod) : n;
    }

    /// <summary>Path of an existing asset with this exact file name under Assets/, or null.</summary>
    public static string Find(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var hits = AssetDatabase.FindAssets(stem).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => Path.GetFileName(p) == fileName).ToArray();
        return hits.FirstOrDefault(p => p.StartsWith(Root + "/")) ?? hits.FirstOrDefault();
    }

    public static string Mat(string name) => Find(name + ".mat") ?? Ensure($"{MaterialFolder(name)}/{name}.mat");
    public static string Tex(string name, string ext = ".png") => Find(name + ext) ?? Ensure($"{TextureFolder(name)}/{name}{ext}");
    public static string TerrainLayer(string name) => Find(name + ".terrainlayer") ?? Ensure($"{TerrainLayers}/{name}.terrainlayer");
    public static string Model(string name) => Find(name + ".fbx") ?? throw new FileNotFoundException($"{name}.fbx not found; new models need Model(name, category)");
    public static string Model(string name, Category c) => Find(name + ".fbx") ?? Ensure($"{AssetFolder(c, name)}/{name}.fbx");
    public static string Prefab(string name)
    {
        var p = Find(name + ".prefab"); if (p != null) return p;
        var b = BaseName(name);
        var model = Find("SM_" + b + ".fbx") ?? Find("SK_" + b + ".fbx");
        return model != null ? $"{Path.GetDirectoryName(model).Replace('\\', '/')}/{name}.prefab"
                             : throw new FileNotFoundException($"{name}.prefab: no prefab and no SM_/SK_{b}.fbx to sit next to");
    }

    /// <summary>Shared material library family, derived from the name (new materials land here). Extend per project.</summary>
    public static string MaterialFolder(string name)
    {
        var n = name.ToLowerInvariant();
        if (Has(n, "smoke", "mist", "dust", "spark", "fog", "beam")) return VFXMaterials;
        if (Has(n, "glow", "emissive", "neon", "screen", "lamp")) return $"{Materials}/Emissive";
        if (Has(n, "leaf", "leaves", "grass", "bark", "foliage", "flower", "moss")) return $"{Materials}/Foliage";
        if (Has(n, "cloth", "fabric", "leather")) return $"{Materials}/Cloth";
        return $"{Materials}/Surfaces";
    }

    /// <summary>Texture location, derived from the name (new textures land here).</summary>
    public static string TextureFolder(string name)
    {
        var n = name.ToLowerInvariant();
        if (Has(n, "smoke", "mist", "dust", "spark")) return VFXTextures;
        if (Has(n, "noise", "cookie", "gradient", "ramp", "lut")) return $"{Textures}/Utility";
        if (Has(n, "leaf", "leaves", "grass", "bark", "foliage", "flower")) return $"{Textures}/Foliage";
        return $"{Textures}/Surfaces";
    }

    static bool Has(string n, params string[] keys) => keys.Any(n.Contains);

    static string Ensure(string assetPath) { EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/')); return assetPath; }

    /// <summary>Create a folder (and its parents) through the AssetDatabase.</summary>
    public static string EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return folder;
        var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        return folder;
    }
}
