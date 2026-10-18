// Play-mode automation for the encounters (unity/encounter_test.sh): ARGS[0] = start <species> | face <species> |
// focus <i> | back | state | timescale <f> | watch <species> | wstate <species>. Without a species: the encounter in progress, else the tiger.
if (!UnityEditor.EditorApplication.isPlaying) return "FAIL not in Play Mode";
var all = UnityEngine.Object.FindObjectsByType<TigerEncounter>(UnityEngine.FindObjectsSortMode.None);
if (all.Length == 0) return "FAIL no TigerEncounter";
var a = ARGS.Length > 0 ? ARGS[0].Split(' ') : new[] { "state" };
TigerEncounter enc = null;
if ((a[0] == "start" || a[0] == "face" || a[0] == "watch" || a[0] == "wstate") && a.Length > 1) enc = System.Array.Find(all, e => e.SpeciesId == a[1]);
enc ??= System.Array.Find(all, e => e.StateName != "Forest") ?? System.Array.Find(all, e => e.SpeciesId == "tiger") ?? all[0];
if (!enc) return "FAIL no encounter for " + ARGS[0];
if (a[0] == "start") enc.TestStart();
else if (a[0] == "face") UnityEngine.Object.FindFirstObjectByType<FixedLookCamera>()?.LookAt(enc.transform.position + UnityEngine.Vector3.up * 0.7f);
else if (a[0] == "watch") enc.wander.Watch(4f);
else if (a[0] == "wstate") return $"{enc.SpeciesId} watching {enc.wander.Watching} walking {enc.wander.Walking} speed {enc.wander.Speed:F2} lookW {enc.wander.LookWeight:F2} lookT {enc.wander.LookTarget} cam {UnityEngine.Camera.main.transform.position}";
else if (a[0] == "focus") enc.TestFocus(int.Parse(a[1]));
else if (a[0] == "back") enc.TestBack();
else if (a[0] == "timescale") UnityEngine.Time.timeScale = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
return enc.StateName + " " + enc.SpeciesId + " " + enc.transform.position + " cam " + UnityEngine.Camera.main.transform.position;
