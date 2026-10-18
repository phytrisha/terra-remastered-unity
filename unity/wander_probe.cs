// Play-mode probe: every AnimalWander's position, heading, walking / look weight and Animator state.
if (!UnityEditor.EditorApplication.isPlaying) return "FAIL not in Play Mode";
var sb = new System.Text.StringBuilder();
foreach (var w in UnityEngine.Object.FindObjectsByType<AnimalWander>(UnityEngine.FindObjectsSortMode.None))
{
    var a = w.GetComponentInChildren<UnityEngine.Animator>(); var st = a.GetCurrentAnimatorStateInfo(0);
    string s = st.IsName("Walk") ? "Walk" : st.IsName("Idle") ? "Idle" : st.IsName("Run") ? "Run" : "?";
    var cam = UnityEngine.Camera.main.transform.position; var d = w.transform.position - cam;
    sb.AppendLine($"{w.name}: pos {w.transform.position.x:F2},{w.transform.position.y:F2},{w.transform.position.z:F2} yaw {w.transform.eulerAngles.y:F0} walking {w.Walking} tgt {w.Target.x:F1},{w.Target.z:F1} spd {w.Speed:F2} turn {w.TurnBlend:F2} look {w.LookWeight:F2} anim {s} x{a.speed:F2} dist {d.magnitude:F1} enabled {w.enabled}");
}
return sb.ToString();
