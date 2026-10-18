using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// Records an animal's motion in Play Mode at a fixed game-time step (Time.captureFramerate) for animation QA: root pose,
/// every bone's world position, the paw contact points (toe bone heads), the terrain height under each paw, the
/// Animator state and its normalized time. Writes JSON for tools/motion_report.py (paw slip in stance, ground
/// penetration / floating, frame-to-frame pops). Driven from unity/motion_sample.cs.
public static class PlayMotionSampler
{
    static Transform root; static Transform[] bones; static Animator anim; static Collider ground; static QuadrupedLocomotion loco;
    static readonly StringBuilder sb = new(); static int left, lastFrame = -1, count; static string outPath;
    static readonly string[] Paws = { "toe_FL", "toe_FR", "toe_BL", "toe_BR" };

    public static bool Busy => left > 0;
    public static int Count => count;

    public static void Start(string animalName, string path, int frames, int fps)
    {
        var go = GameObject.Find(animalName);
        root = go.transform; anim = go.GetComponentInChildren<Animator>(); loco = go.GetComponent<QuadrupedLocomotion>();
        var smr = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.bones.Length > 0);
        bones = smr.bones.Where(b => b).Distinct().ToArray();
        ground = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).FirstOrDefault(c => c.name.Contains("Terrain"));
        outPath = path; left = frames; count = 0; lastFrame = -1;
        sb.Clear(); sb.Append("{\"fps\":").Append(fps).Append(",\"bones\":[").Append(string.Join(",", bones.Select(b => "\"" + b.name + "\""))).Append("],\"frames\":[");
        Time.captureFramerate = fps;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    static string V(Vector3 v) => $"[{v.x:F4},{v.y:F4},{v.z:F4}]";

    static void Tick()
    {
        if (!EditorApplication.isPlaying || !root) { Stop(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        var st = anim.GetCurrentAnimatorStateInfo(0);
        string state = st.IsName("Walk") ? "Walk" : st.IsName("Idle") ? "Idle" : st.IsName("Run") ? "Run" : "Other";
        float nt = st.normalizedTime, trans = anim.IsInTransition(0) ? anim.GetAnimatorTransitionInfo(0).normalizedTime : -1f;
        if (count > 0) sb.Append(',');
        sb.Append("{\"t\":").Append(Time.time.ToString("F4")).Append(",\"state\":\"").Append(state).Append("\",\"nt\":").Append(nt.ToString("F4"))
          .Append(",\"trans\":").Append(trans.ToString("F3")).Append(",\"pos\":").Append(V(root.position)).Append(",\"yaw\":").Append(root.eulerAngles.y.ToString("F3"))
          .Append(",\"b\":[").Append(string.Join(",", bones.Select(b => V(b.position)))).Append("],\"paws\":{");
        for (int i = 0; i < Paws.Length; i++)
        {
            var p = bones.FirstOrDefault(b => b.name == Paws[i]); if (!p) continue;
            float gy = p.position.y;
            if (ground && ground.Raycast(new Ray(p.position + Vector3.up * 3f, Vector3.down), out var hit, 10f)) gy = hit.point.y;
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(Paws[i]).Append("\":{\"p\":").Append(V(p.position)).Append(",\"g\":").Append(gy.ToString("F4")).Append('}');
        }
        sb.Append('}');
        if (loco && loco.isActiveAndEnabled)                                   // the per-joint layer's leg states
        {
            sb.Append(",\"legs\":{");
            for (int i = 0; i < loco.legs.Length; i++)
            {
                var l = loco.legs[i]; if (i > 0) sb.Append(',');
                sb.Append('"').Append(l.name).Append("\":{\"pl\":").Append(l.planted ? 1 : 0).Append(",\"u\":").Append(l.swingU.ToString("F3"))
                  .Append(",\"sk\":").Append(l.skip ? 1 : 0).Append(",\"tm\":").Append(l.timed ? 1 : 0).Append(",\"tg\":").Append(V(l.target)).Append('}');
            }
            sb.Append("},\"walking\":").Append(loco.Walking ? 1 : 0);
        }
        sb.Append('}'); count++;
        if (--left <= 0) Stop();
    }

    public static void Stop()
    {
        EditorApplication.update -= Tick;
        if (outPath != null && count > 0) { sb.Append("]}"); Directory.CreateDirectory(Path.GetDirectoryName(outPath)); File.WriteAllText(outPath, sb.ToString()); }
        left = 0; outPath = null; Time.captureFramerate = 0;
    }
}
