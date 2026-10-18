UnityEditor.AssetDatabase.Refresh();
var ok = UnityEditor.AssetDatabase.DeleteAsset("Assets/PlayCapture");
return "deleted Assets/PlayCapture: " + ok;
