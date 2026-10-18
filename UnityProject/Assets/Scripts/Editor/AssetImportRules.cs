// Import settings for everything the Blender/Tripo pipeline drops into Assets/Art, applied automatically on import,
// so no asset needs hand-tuning in the Inspector and re-exports keep the same settings.
//
//   SM_*.fbx   static meshes: file scale, normals + tangents imported (authored in Blender), no generated lightmap UVs
//              (UV1 "UVLightmap" comes from Blender), materials remapped by name onto existing project materials
//   SK_*.fbx   skinned characters: same mesh rules, Humanoid avatar from the model (Mixamo-style bone names map
//              automatically), clips named after their take, Idle*/Walk*/Run* looped. Animals and other non-human
//              rigs (GenericRigs) get a Generic avatar instead.
//   T_*        textures: *_N normal map, *_BC / *_E sRGB, everything else linear (ORM, MS, AO, H, Mask)
// Rules for new asset types belong here, not in one-off scripts.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class AssetImportRules : AssetPostprocessor
{
    static bool InArt(string path) => path.StartsWith(ProjectPaths.Art + "/");
    // non-humanoid skeletons (quadrupeds etc.): Generic avatar, no Humanoid mapping
    static readonly string[] GenericRigs = { "SK_WhiteTiger", "SK_WhiteTigerHD", "SK_Tapir", "SK_TapirHD", "SK_Chital", "SK_ChitalHD" };
    // hero close-up texture sets imported at full 4K (everything else keeps the 2048 default)
    static readonly string[] Textures4K = { "T_WhiteTigerHD_", "T_TapirHD_", "T_ChitalHD_" };
    // meshes particle systems emit from (skinned mesh shapes need a CPU copy): the tiger portal sparkles / shards
    static readonly string[] ReadableMeshes = { "SK_WhiteTiger", "SK_WhiteTigerHD" };

    void OnPreprocessModel()
    {
        if (!InArt(assetPath)) return;
        var name = Path.GetFileNameWithoutExtension(assetPath);
        if (!name.StartsWith("SM_") && !name.StartsWith("SK_")) return;
        var mi = (ModelImporter)assetImporter;
        mi.globalScale = 1f; mi.useFileScale = true;
        mi.bakeAxisConversion = false;                 // keep the root rotation: prefab code must not reset it either
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.Import;
        mi.generateSecondaryUV = false;
        mi.meshCompression = ModelImporterMeshCompression.Off;
        mi.isReadable = ReadableMeshes.Contains(name);
        mi.importCameras = false; mi.importLights = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        mi.indexFormat = ModelImporterIndexFormat.Auto;
        if (name.StartsWith("SM_"))
        {
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
        }
        else
        {
            mi.animationType = GenericRigs.Contains(name) ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true;
            mi.motionNodeName = "";              // in-place clips; arc walks are driven by AnimalWander with the clip's rates
            // no keyframe reduction: it moved planted paws by millimetres-centimetres (sliding); the clips are small
            mi.animationCompression = ModelImporterAnimationCompression.Off; mi.resampleCurves = true;   // quaternion resampling: no Euler flips
        }
    }

    void OnPostprocessModel(GameObject root)
    {
        if (!InArt(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        // remap embedded materials onto project materials of the same name (the Blender material name IS the Unity name)
        bool changed = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (!m) continue;
                var path = ProjectPaths.Find(m.name + ".mat");
                if (path == null) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name);
                var lib = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (lib && (!mi.GetExternalObjectMap().TryGetValue(id, out var cur) || cur != lib))
                { mi.AddRemap(id, lib); changed = true; }
            }
        if (changed) EditorApplication.delayCall += () => { mi.SaveAndReimport(); };
        // UCX_ hulls are collision only
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            if (r.name.StartsWith("UCX_")) r.enabled = false;
    }

    void OnPreprocessAnimation()
    {
        if (!InArt(assetPath) || !Path.GetFileName(assetPath).StartsWith("SK_")) return;
        var mi = (ModelImporter)assetImporter;
        var clips = mi.defaultClipAnimations;
        foreach (var c in clips)
        {
            var n = c.takeName.Contains("|") ? c.takeName.Split('|').Last() : c.takeName;
            c.name = n;
            c.loopTime = n.StartsWith("Idle") || n.StartsWith("Walk") || n.StartsWith("Run");
            c.lockRootRotation = true; c.lockRootHeightY = true;
            c.lockRootPositionXZ = !(n.StartsWith("Walk") || n.StartsWith("Run"));
            c.keepOriginalOrientation = true; c.keepOriginalPositionY = true;
        }
        mi.clipAnimations = clips;
    }

    void OnPreprocessTexture()
    {
        if (!InArt(assetPath)) return;
        var name = Path.GetFileNameWithoutExtension(assetPath);
        if (!name.StartsWith("T_")) return;
        var ti = (TextureImporter)assetImporter;
        ti.mipmapEnabled = true; ti.anisoLevel = 4; ti.filterMode = FilterMode.Trilinear;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        if (Textures4K.Any(name.StartsWith)) ti.maxTextureSize = 4096;
        if (name.EndsWith("_N")) { ti.textureType = TextureImporterType.NormalMap; return; }
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = name.EndsWith("_BC") || name.EndsWith("_E");
        if (ti.DoesSourceTextureHaveAlpha() && name.EndsWith("_BC"))
        {   // alpha-cut sprites (leaves, decals): keep coverage in the mips so they don't thin out with distance
            ti.alphaIsTransparency = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f;
        }
    }
}
