// Play-mode helper for unity/transition_test.sh: ARGS[0] = record <frames> <fps> | busy | stop
if (!UnityEditor.EditorApplication.isPlaying) return "FAIL not in Play Mode";
var a = ARGS.Length > 0 ? ARGS[0].Split(' ') : new[] { "busy" };
if (a[0] == "record") { PlayFrameRecorder.Start(System.IO.Path.Combine(ROOT, "renders", "unity", "transition"), int.Parse(a[1]), int.Parse(a[2])); return "recording"; }
if (a[0] == "stop") { PlayFrameRecorder.Stop(); return "stopped " + PlayFrameRecorder.Written; }
return (PlayFrameRecorder.Busy ? "busy " : "done ") + PlayFrameRecorder.Written;
