// Light probe report: brightness distribution of the baked probes (black = inside geometry) and what the giant
// buttress trees sample. Run after a bake.
var lp = UnityEngine.LightmapSettings.lightProbes;
var pos = lp.positions; var sh = lp.bakedProbes;
float L(UnityEngine.Rendering.SphericalHarmonicsL2 s) => (s[0,0]*0.3f + s[1,0]*0.59f + s[2,0]*0.11f);
var vals = Enumerable.Range(0, pos.Length).Select(i => (i, l: L(sh[i]))).OrderBy(t => t.l).ToList();
var sb = new System.Text.StringBuilder();
var arr = vals.Select(v => v.l).ToArray();
sb.AppendLine($"probes {pos.Length} min {arr[0]:F3} p5 {arr[arr.Length/20]:F3} p50 {arr[arr.Length/2]:F3} p95 {arr[arr.Length*19/20]:F3}");
sb.AppendLine($"below 0.05: {arr.Count(a => a < 0.05f)}  below 0.15: {arr.Count(a => a < 0.15f)}");
var amb = UnityEngine.RenderSettings.ambientProbe; sb.AppendLine($"ambient probe L {L(amb):F3}");
foreach (var (i, l) in vals.Take(12)) sb.AppendLine($"  {pos[i]} L {l:F3}");
// big probe-lit renderers and their sampled ambient
foreach (var r in UnityEngine.Object.FindObjectsByType<UnityEngine.MeshRenderer>(UnityEngine.FindObjectsSortMode.None).Where(r => r.lightmapIndex < 0 || r.lightmapIndex >= 65534).OrderByDescending(r => r.bounds.size.magnitude).Take(0)) {}
var giants = UnityEngine.GameObject.Find("Rainforest/Hero");
int n = 0;
foreach (UnityEngine.Transform t in giants.transform)
{
    if (!t.name.Contains("Buttress")) continue;
    var r = t.GetComponentInChildren<UnityEngine.Renderer>();
    UnityEngine.LightProbes.GetInterpolatedProbe(r.bounds.center, r, out var s);
    sb.AppendLine($"  {t.name} at {t.position} bounds {r.bounds.size} sample L {L(s):F3}  colliders {t.GetComponentsInChildren<UnityEngine.Collider>().Length}");
    if (++n > 5) break;
}
return sb.ToString();
