// Play-mode render stats (editor Game view): triangles, SetPass calls, CPU / GPU frame time, fur shells in use.
if (!UnityEditor.EditorApplication.isPlaying) return "FAIL not in Play Mode";
var fur = UnityEngine.Object.FindAnyObjectByType<TigerFur>();
UnityEngine.FrameTimingManager.CaptureFrameTimings(); var ft = new UnityEngine.FrameTiming[4];
uint n = UnityEngine.FrameTimingManager.GetLatestTimings(4, ft);
double gpu = 0, cpu = 0; for (int i = 0; i < n; i++) { gpu += ft[i].gpuFrameTime; cpu += ft[i].cpuFrameTime; }
return $"tris {UnityEditor.UnityStats.triangles / 1000}k setpass {UnityEditor.UnityStats.setPassCalls} cpu {(n > 0 ? cpu / n : -1):F1} ms gpu {(n > 0 ? gpu / n : -1):F1} ms shells {(fur ? fur.Layers : -1)}";
