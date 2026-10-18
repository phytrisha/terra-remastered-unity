// Play-mode helper for unity/motion_test.sh: ARGS[0] = "start <animal> <frames> <fps> [noloco]" | busy
// (noloco: QuadrupedLocomotion off for the recording, to see the clips alone)
if (!UnityEditor.EditorApplication.isPlaying) return "FAIL not in Play Mode";
var a = ARGS.Length > 0 ? ARGS[0].Split(' ') : new[] { "busy" };
if (a[0] == "start" && a.Length > 4 && a[4] == "noloco") { var q = UnityEngine.GameObject.Find(a[1])?.GetComponent<QuadrupedLocomotion>(); if (q) q.enabled = false; }
if (a[0] == "start") { PlayMotionSampler.Start(a[1], System.IO.Path.Combine(ROOT, "renders", "unity", "motion_" + a[1] + ".json"), int.Parse(a[2]), int.Parse(a[3])); return "sampling"; }
return (PlayMotionSampler.Busy ? "busy " : "done ") + PlayMotionSampler.Count;
