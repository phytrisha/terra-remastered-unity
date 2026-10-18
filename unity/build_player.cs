// Build the macOS player of the Rainforest scene into UnityProject/Builds/ (git-ignored).
var opts = new UnityEditor.BuildPlayerOptions {
    scenes = new[] { "Assets/Scenes/Rainforest.unity" },
    locationPathName = System.IO.Path.Combine(ROOT, "UnityProject", "Builds", "TerraRainforest.app"),
    target = UnityEditor.BuildTarget.StandaloneOSX,
    options = UnityEditor.BuildOptions.None };
UnityEditor.PlayerSettings.productName = "Terra Rainforest";
UnityEditor.PlayerSettings.fullScreenMode = UnityEngine.FullScreenMode.Windowed;
UnityEditor.PlayerSettings.defaultScreenWidth = 1920; UnityEditor.PlayerSettings.defaultScreenHeight = 1080;
UnityEditor.PlayerSettings.resizableWindow = true;
var report = UnityEditor.BuildPipeline.BuildPlayer(opts);
var s = report.summary;
return $"{s.result} {s.totalSize / 1048576} MB in {s.totalTime.TotalSeconds:F0}s errors={s.totalErrors} warnings={s.totalWarnings} -> {opts.locationPathName}";
