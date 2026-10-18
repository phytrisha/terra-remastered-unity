// Play-mode helper for unity/life_test.sh: ARGS[0] = face <butterfly|spray|birds> | state | pass (flock over the lake now). Turns the viewer toward the
// nearest butterfly / the nearest cascade spray / the flying flock (if any), returns what it aimed at.
if (!UnityEditor.EditorApplication.isPlaying) return "FAIL not in Play Mode";
var look = UnityEngine.Object.FindFirstObjectByType<FixedLookCamera>();
var cam = UnityEngine.Camera.main.transform.position;
var a = ARGS.Length > 0 ? ARGS[0].Split(' ') : new[] { "state" };
UnityEngine.Transform best = null; float bd = 1e9f;
string prefix = a.Length > 1 ? (a[1] == "butterfly" ? "Butterfly_" : a[1] == "spray" ? "Spray_" : "Bird_") : "";
foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.Transform>(UnityEngine.FindObjectsSortMode.None))
{
    if (prefix == "" || !t.name.StartsWith(prefix) || !t.gameObject.activeInHierarchy) continue;
    float d = (t.position - cam).magnitude; if (d < bd) { bd = d; best = t; }
}
if (a[0] == "face" && best && look) look.LookAt(best.position);
if (a[0] == "pass") { var f = UnityEngine.Object.FindFirstObjectByType<BirdFlock>(); if (f) f.TestPassAhead(); return "pass started"; }
return best ? $"{a[0]} {best.name} at {best.position} d {bd:F1}" : "none active";
