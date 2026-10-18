var data = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>("Assets/Settings/Rendering/URP_Default_Renderer.asset");
var f = data.rendererFeatures.First(x => x.name == ARGS[0]); f.SetActive(ARGS[1] == "on"); return f.name + " " + f.isActive;
