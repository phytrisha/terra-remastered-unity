// Sandbox test (creates a scratch scene: delete it afterwards via AssetDatabase): does the lightmapper use LowPolyFoliage's _TransparencyLM for the baked shadowmask?
// Builds Scenes/Sandbox/LM_TransmissionTest.unity (floor + two raised crowns: tag variants), bakes, reports the
// shadowmask under each crown, then reopens the Rainforest scene. ARGS[0] = RenderType tag value to test (optional).
var mainPath = "Assets/Scenes/Rainforest.unity"; var path = "Assets/Scenes/Sandbox/LM_TransmissionTest.unity";
var sc = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
var ls = new UnityEngine.LightingSettings { bakedGI = true, realtimeGI = false, lightmapper = UnityEngine.LightingSettings.Lightmapper.ProgressiveGPU, mixedBakeMode = UnityEngine.MixedLightingMode.Shadowmask, lightmapResolution = 20, directSampleCount = 64, indirectSampleCount = 16, maxBounces = 1, lightmapMaxSize = 512, ao = false };
UnityEditor.Lightmapping.lightingSettings = ls;
var sunGo = new UnityEngine.GameObject("Sun"); var sun = sunGo.AddComponent<UnityEngine.Light>(); sun.type = UnityEngine.LightType.Directional; sun.lightmapBakeType = UnityEngine.LightmapBakeType.Mixed; sun.shadows = UnityEngine.LightShadows.Soft; sun.shadowAngle = 0.5f; sunGo.transform.rotation = UnityEngine.Quaternion.Euler(90, 0, 0);
var floor = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Plane); floor.transform.localScale = new UnityEngine.Vector3(3, 1, 3);
UnityEditor.GameObjectUtility.SetStaticEditorFlags(floor, UnityEditor.StaticEditorFlags.ContributeGI);
floor.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(ProjectPaths.Find("MI_Dirt.mat"));
var canopy = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(ProjectPaths.Find("MI_Canopy.mat"));
var plain = new UnityEngine.Material(canopy); plain.SetTexture("_TransparencyLM", null);
foreach (var (x, m) in new[] { (-6f, canopy), (6f, plain) })
{
    var q = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Quad); q.transform.position = new UnityEngine.Vector3(x, 3, 0); q.transform.rotation = UnityEngine.Quaternion.Euler(90, 0, 0); q.transform.localScale = UnityEngine.Vector3.one * 8;
    q.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial = m; UnityEditor.GameObjectUtility.SetStaticEditorFlags(q, UnityEditor.StaticEditorFlags.ContributeGI);
    q.GetComponent<UnityEngine.MeshRenderer>().receiveGI = UnityEngine.ReceiveGI.LightProbes;
}
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(sc, path);
bool ok = UnityEditor.Lightmapping.Bake();
var fr = floor.GetComponent<UnityEngine.MeshRenderer>(); var lm = UnityEngine.LightmapSettings.lightmaps[fr.lightmapIndex].shadowMask;
var tp = UnityEditor.AssetDatabase.GetAssetPath(lm); var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(tp); ti.isReadable = true; ti.SaveAndReimport();
lm = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(tp);
var so = fr.lightmapScaleOffset;
float Sample(float wx, float wz) { var uv = new UnityEngine.Vector2((wx + 15) / 30f, (wz + 15) / 30f); uv = new UnityEngine.Vector2(1 - uv.x, 1 - uv.y); var p = uv * new UnityEngine.Vector2(so.x, so.y) + new UnityEngine.Vector2(so.z, so.w); return lm.GetPixelBilinear(p.x, p.y).r; }
var res = new System.Text.StringBuilder($"bake {ok}  ");
foreach (var x in new[] { -6f, 6f, 0f }) { var vals = new System.Collections.Generic.List<float>(); for (float dx = -3; dx <= 3; dx += 0.5f) for (float dz = -3; dz <= 3; dz += 0.5f) vals.Add(Sample(x + dx, dz)); res.Append($"x={x}: min {vals.Min():F2} max {vals.Max():F2} mean {vals.Average():F2}  "); }
UnityEditor.SceneManagement.EditorSceneManager.OpenScene(mainPath, UnityEditor.SceneManagement.OpenSceneMode.Single);
return res.ToString();
