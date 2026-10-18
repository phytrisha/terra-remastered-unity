// Prints "playing" or "edit": tools refuse to rebuild/bake/test while someone is in Play Mode.
return UnityEditor.EditorApplication.isPlaying ? "playing" : "edit";
