// Editor audio mute for automated Play Mode tests: ARGS[0] = on | off | state (prints "on"/"off")
if (ARGS.Length > 0 && ARGS[0] != "state") UnityEditor.EditorUtility.audioMasterMute = ARGS[0] == "on";
return UnityEditor.EditorUtility.audioMasterMute ? "on" : "off";
