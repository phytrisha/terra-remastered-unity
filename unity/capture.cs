// Render review shots with a temporary camera; scene cameras are never moved. Output goes to <root>/renders/unity/,
// never inside Assets/. Args (unity/args/capture.txt), one shot per line:
//   name px py pz tx ty tz [fov] [rel]        rel=1: py/ty are heights above the ground under that point
//   name @Marker                               use the transform (and localScale.x as fov) of a GameObject path
// Keep a fixed shot list per area and re-run it every round, then compare rounds with tools/sheet.py.
var lines = ARGS;
var outDir = System.IO.Path.Combine(ROOT, "renders", "unity"); System.IO.Directory.CreateDirectory(outDir);
float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
float Ground(float x, float z) => UnityEngine.Physics.Raycast(new UnityEngine.Vector3(x, 1000, z), UnityEngine.Vector3.down, out var h, 3000f) ? h.point.y : 0f;
// Renders through the scene's main camera (a temporary camera missed the baked shadowmask), restoring its pose and
// FOV afterwards; nothing is saved.
var main = UnityEngine.Camera.main;
var cam = main; var go = main.gameObject;
var savePos = go.transform.position; var saveRot = go.transform.rotation; var saveFov = cam.fieldOfView; var saveFar = cam.farClipPlane;
cam.farClipPlane = UnityEngine.Mathf.Max(cam.farClipPlane, 1000);
int W = 1600, H = 1000, n = 0;
UnityEngine.Physics.SyncTransforms();
foreach (var ln in lines)
{
    var s = ln.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
    if (s.Length < 2 || s[0].StartsWith("#")) continue;
    if (s[1].StartsWith("@"))
    {
        var m = UnityEngine.GameObject.Find(s[1].Substring(1)); if (!m) continue;
        go.transform.SetPositionAndRotation(m.transform.position, m.transform.rotation); cam.fieldOfView = m.transform.localScale.x > 5 ? m.transform.localScale.x : 50;
    }
    else
    {
        var p = new UnityEngine.Vector3(F(s[1]), F(s[2]), F(s[3])); var q = new UnityEngine.Vector3(F(s[4]), F(s[5]), F(s[6]));
        if (s.Length > 8 && s[8] == "1") { p.y += Ground(p.x, p.z); q.y += Ground(q.x, q.z); }
        go.transform.position = p; go.transform.LookAt(q); cam.fieldOfView = s.Length > 7 ? F(s[7]) : 50;
    }
    var rt = new UnityEngine.RenderTexture(W, H, 24, UnityEngine.RenderTextureFormat.ARGB32) { antiAliasing = 4 };
    cam.targetTexture = rt;
    for (int i = 0; i < 4; i++) cam.Render();            // let temporal effects (TAA, auto exposure) settle
    UnityEngine.RenderTexture.active = rt;
    var tex = new UnityEngine.Texture2D(W, H, UnityEngine.TextureFormat.RGB24, false);
    tex.ReadPixels(new UnityEngine.Rect(0, 0, W, H), 0, 0); tex.Apply(); UnityEngine.RenderTexture.active = null;
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, s[0] + ".png"), tex.EncodeToPNG());
    cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex); n++;
}
go.transform.SetPositionAndRotation(savePos, saveRot); cam.fieldOfView = saveFov; cam.farClipPlane = saveFar;   // never destroy: it is the scene camera
return $"captured {n} -> {outDir}";
