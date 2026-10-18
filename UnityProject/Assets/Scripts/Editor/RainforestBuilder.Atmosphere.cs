using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// Renderer features of the shared URP renderer (project-wide, note changes in the session log):
///   - SSAO (DepthNormals, downsampled): contact shadows between leaves, roots and rocks on top of the baked AO
///   - VolumetricFogFeature: raymarched height mist lit by the sun with its realtime shadows (shafts in the jungle)
public static partial class RainforestBuilder
{
    const string RendererPath = "Assets/Settings/Rendering/URP_Default_Renderer.asset";

    /// Re-apply only the renderer features (no scene rebuild, no rebake): unity/atmosphere.cs
    public static string ConfigureAtmosphere() { var log = new List<string>(); ConfigureRendererFeatures(log); AssetDatabase.SaveAssets(); return string.Join("\n", log); }

    static void ConfigureRendererFeatures(List<string> log)
    {
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (!data) { log.Add("WARN no renderer data at " + RendererPath); return; }

        var ssao = EnsureFeature<ScreenSpaceAmbientOcclusion>(data, "SSAO");
        var so = new SerializedObject(ssao);
        void S(string p, Action<SerializedProperty> a) { var sp = so.FindProperty("m_Settings." + p); if (sp != null) a(sp); else log.Add("WARN ssao prop " + p); }
        S("Source", p => p.enumValueIndex = 1);              // DepthNormals: our foliage writes facet normals
        S("NormalSamples", p => p.enumValueIndex = 1);
        S("Downsample", p => p.boolValue = true);
        S("AfterOpaque", p => p.boolValue = false);
        S("Intensity", p => p.floatValue = 0.9f);
        S("DirectLightingStrength", p => p.floatValue = 0.15f);
        S("Radius", p => p.floatValue = 0.25f);
        S("Samples", p => p.enumValueIndex = 2);             // Low (4): Medium cost ~2.6 ms on the M4 (perfbench_3)
        S("BlurQuality", p => p.enumValueIndex = 0);         // bilateral
        S("Falloff", p => p.floatValue = 60f);
        so.ApplyModifiedPropertiesWithoutUndo();
        ssao.SetActive(true);

        var fog = EnsureFeature<VolumetricFogFeature>(data, "VolumetricFog");
        var f = fog.settings;
        f.shader = Shader.Find("Hidden/TerraRemastered/VolumetricFog");   // serialized reference keeps it in player builds
        // round 1 (0.006 / g 0.6 / sun 1.3) washed out views toward the sun and made the lake view milky
        f.density = 0.0034f; f.minDensity = 0.0007f;   // round 3: thinner, the faceted forms must stay crisp (low-poly references) f.baseHeight = LakeLevel; f.heightFalloff = 8f;
        f.maxDistance = 110f; f.steps = 28; f.downsample = 2; f.anisotropy = 0.5f; f.sunScale = 0.6f;
        f.ambient = new Color(0.55f, 0.70f, 0.62f); f.ambientScale = 0.4f;
        fog.SetActive(true);
        fog.Create();

        EditorUtility.SetDirty(ssao); EditorUtility.SetDirty(fog); EditorUtility.SetDirty(data);
        data.SetDirty();
        log.Add($"renderer features: {string.Join(", ", data.rendererFeatures.Select(x => x.name))}; fog density {f.density} falloff {f.heightFalloff} m, {f.steps} steps to {f.maxDistance} m");
    }

    static T EnsureFeature<T>(ScriptableRendererData data, string name) where T : ScriptableRendererFeature
    {
        var f = data.rendererFeatures.OfType<T>().FirstOrDefault();
        if (f) return f;
        f = ScriptableObject.CreateInstance<T>(); f.name = name;
        AssetDatabase.AddObjectToAsset(f, data);
        data.rendererFeatures.Add(f);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out _, out long id);
        var so = new SerializedObject(data);
        var map = so.FindProperty("m_RendererFeatureMap");
        map.arraySize = data.rendererFeatures.Count;
        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
        so.ApplyModifiedPropertiesWithoutUndo();
        return f;
    }
}
