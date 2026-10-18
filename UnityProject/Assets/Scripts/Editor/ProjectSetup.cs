// One-time project setup for a new project made from the template (Tools > Asset Pipeline > Setup Project, or
// `ProjectSetup.Run()` from an eval script). Safe to run again: it only creates what is missing.
//   - URP asset + renderer in Settings/Rendering, assigned to Graphics and every Quality level (Forward+)
//   - linear colour space, Force Text serialization, visible meta files
//   - Run In Background (CLI-driven Play Mode freezes without it), Frame Timing Stats (PerfBench needs it)
//   - Sandbox scene folder
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ProjectSetup
{
    [MenuItem("Tools/Asset Pipeline/Setup Project")]
    public static string Run()
    {
        var log = new System.Text.StringBuilder();
        ProjectPaths.EnsureFolder(ProjectPaths.RenderingSettings);
        ProjectPaths.EnsureFolder(ProjectPaths.Sandbox);
        var assetPath = ProjectPaths.RenderingSettings + "/URP_Default.asset";
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
        if (!urp)
        {
            var rendererPath = ProjectPaths.RenderingSettings + "/URP_Default_Renderer.asset";
            var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
            rd.renderingMode = RenderingMode.ForwardPlus;
            AssetDatabase.CreateAsset(rd, rendererPath);
            urp = UniversalRenderPipelineAsset.Create(rd);
            urp.shadowDistance = 70f; urp.shadowCascadeCount = 2;
            AssetDatabase.CreateAsset(urp, assetPath);
            log.AppendLine("created " + assetPath);
        }
        if (GraphicsSettings.defaultRenderPipeline != urp) { GraphicsSettings.defaultRenderPipeline = urp; log.AppendLine("graphics pipeline -> URP"); }
        var cur = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            if (QualitySettings.renderPipeline != urp) QualitySettings.renderPipeline = urp;
        }
        QualitySettings.SetQualityLevel(cur, false);
        if (PlayerSettings.colorSpace != ColorSpace.Linear) { PlayerSettings.colorSpace = ColorSpace.Linear; log.AppendLine("color space -> Linear"); }
        EditorSettings.serializationMode = SerializationMode.ForceText;
        VersionControlSettings.mode = "Visible Meta Files";
        PlayerSettings.runInBackground = true;
        PlayerSettings.enableFrameTimingStats = true;
        AssetDatabase.SaveAssets();
        log.AppendLine("force text, visible meta, run in background, frame timing stats: on");
        Debug.Log(log.ToString());
        return log.ToString();
    }
}
