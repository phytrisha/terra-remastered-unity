// Bake lightmaps + light probes for the open Rainforest scene (settings come from RainforestBuilder.Lighting.cs).
// Asynchronous so the editor stays responsive; use tools/bake_lighting.sh, which runs the three modes in order:
//   start  -> Lightmapping.BakeAsync()      status -> "running" / "idle"      report -> save scene + lightmap summary
// Re-run after every scene rebuild (tools/rebuild_scene.sh does).
var mode = ARGS.Length > 0 ? ARGS[0].Trim() : "start";
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (!scene.path.EndsWith("Rainforest.unity")) return "FAIL open scene is " + scene.path;
if (UnityEditor.EditorApplication.isPlaying) return "FAIL editor is in Play Mode (stop it, then bake)";
if (mode == "status") return UnityEditor.Lightmapping.isRunning ? "running" : "idle";
if (mode == "start")
{
    if (UnityEditor.Lightmapping.isRunning) return "already running";
    return UnityEditor.Lightmapping.BakeAsync() ? "started" : "FAIL bake did not start";
}
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
var sb = new System.Text.StringBuilder();
sb.AppendLine($"bake done, lightmapper {UnityEditor.Lightmapping.lightingSettings.lightmapper}");
var maps = UnityEngine.LightmapSettings.lightmaps;
for (int i = 0; i < maps.Length; i++)
    sb.AppendLine($"  lightmap {i}: color {maps[i].lightmapColor?.width}px dir {(maps[i].lightmapDir ? "y" : "n")} shadowmask {(maps[i].shadowMask ? "y" : "n")}");
foreach (var r in UnityEngine.Object.FindObjectsByType<UnityEngine.MeshRenderer>(UnityEngine.FindObjectsSortMode.None))
    if (r.lightmapIndex >= 0 && r.lightmapIndex < 65534) sb.AppendLine($"  {r.name}: index {r.lightmapIndex} scaleOffset {r.lightmapScaleOffset}");
var lp = UnityEngine.LightmapSettings.lightProbes;
sb.AppendLine($"  light probes baked: {(lp ? lp.count : 0)}");
var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(scene.path), scene.name);
if (System.IO.Directory.Exists(dir))
    foreach (var f in System.IO.Directory.GetFiles(dir)) if (!f.EndsWith(".meta")) sb.AppendLine($"  {System.IO.Path.GetFileName(f)} {new System.IO.FileInfo(f).Length / 1048576.0:F1} MB");
return sb.ToString();
