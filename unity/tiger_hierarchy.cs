// Tiger hierarchy facts for root motion: model paths of the root bone, where the Animator sits in the scene instance.
var sb = new System.Text.StringBuilder();
var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(ProjectPaths.Find("SK_WhiteTiger.fbx"));
foreach (var t in model.GetComponentsInChildren<UnityEngine.Transform>()) if (t.name == "root" || t.name == "hips" || t.parent == model.transform) sb.AppendLine("model: " + UnityEditor.AnimationUtility.CalculateTransformPath(t, model.transform));
var mi = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(ProjectPaths.Find("SK_WhiteTiger.fbx"));
sb.AppendLine($"motionNodeName '{mi.motionNodeName}'");
var tiger = UnityEngine.GameObject.Find("PF_WhiteTiger");
if (tiger) { var a = tiger.GetComponentInChildren<UnityEngine.Animator>(); sb.AppendLine($"scene tiger root '{tiger.name}', animator on '{a.gameObject.name}' (same as root: {a.gameObject == tiger}), applyRootMotion {a.applyRootMotion}, scale {tiger.transform.lossyScale.x}"); }
return sb.ToString();
