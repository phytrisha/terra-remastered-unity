// Play-mode A/B helper: switch the HD fur to the old setup (32 shells on the full 89k mesh) for perf comparison.
if (!UnityEditor.EditorApplication.isPlaying) return "FAIL not in Play Mode";
var fur = UnityEngine.Object.FindAnyObjectByType<TigerFur>();
var full = UnityEngine.Object.FindObjectsByType<UnityEngine.SkinnedMeshRenderer>(UnityEngine.FindObjectsSortMode.None).First(r => r.name == "SK_WhiteTigerHD");
fur.TestLegacy(full.sharedMesh, 32);
return "legacy: 32 shells on " + full.sharedMesh.triangles.Length / 3 + " tris";
