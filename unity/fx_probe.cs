// Play-mode probe: emit from each portal-FX particle system and report where particles spawn relative to the tiger mesh.
if (!UnityEditor.EditorApplication.isPlaying) return "FAIL not in Play Mode";
var fx = UnityEngine.Object.FindFirstObjectByType<TigerPortalFx>();
var sb = new System.Text.StringBuilder();
var b = fx.lowRenderer.bounds;
sb.AppendLine($"lo bounds c {b.center} ext {b.extents}; smr tf pos {fx.lowRenderer.transform.position} rot {fx.lowRenderer.transform.rotation.eulerAngles} lossy {fx.lowRenderer.transform.lossyScale}");
foreach (var ps in new[] { fx.charge, fx.burst, fx.shards, fx.trail })
{
    ps.Clear(); fx.EmitFrom(ps, ps == fx.burst ? fx.hdRenderer : fx.lowRenderer, 300, UnityEngine.Vector2.zero);
    var arr = new UnityEngine.ParticleSystem.Particle[ps.particleCount]; int n = ps.GetParticles(arr);
    float inside = 0, maxD = 0; var mn = UnityEngine.Vector3.one * 1e9f; var mx = -mn; float sz = 0;
    for (int i = 0; i < n; i++)
    {
        var p = arr[i].position; var bb = b; bb.Expand(0.1f); if (bb.Contains(p)) inside++;
        maxD = UnityEngine.Mathf.Max(maxD, UnityEngine.Vector3.Distance(p, b.center)); mn = UnityEngine.Vector3.Min(mn, p); mx = UnityEngine.Vector3.Max(mx, p); sz += arr[i].GetCurrentSize(ps);
    }
    sb.AppendLine($"{ps.name}: n {n}, inside bounds {inside / UnityEngine.Mathf.Max(1, n):P0}, max dist {maxD:F2} m, min {mn} max {mx}, mean size {sz / UnityEngine.Mathf.Max(1, n):F3} m, space {ps.main.simulationSpace}");
    ps.Clear();
}
return sb.ToString();
