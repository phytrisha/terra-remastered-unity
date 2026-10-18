#!/bin/bash
# Play-mode captures of the ambient life (butterflies, cascade spray, bird flock) -> renders/unity/life/.
set -euo pipefail
source "$(dirname "$0")/../tools/env.sh"
P=(--project-path "$UNITY_PROJECT" --no-banner --format json --result-only)
R="$ROOT/tools/unity_run.sh"; L="$ROOT/unity/life_probe.cs"
cap() { "$UNITY_CLI" command capture_game_view "${P[@]}" -- --source screen --width 1600 --height 1000 --save_path "PlayCapture/life_$1.png" >/dev/null; }
if [ "$("$R" "$ROOT/unity/editor_state.cs")" = "playing" ]; then echo "editor is in Play Mode: stop it first"; exit 1; fi
MUTE_WAS=$("$R" "$ROOT/unity/editor_mute.cs" state); "$R" "$ROOT/unity/editor_mute.cs" on >/dev/null
trap '"$R" "$ROOT/unity/editor_mute.cs" "$MUTE_WAS" >/dev/null' EXIT
"$UNITY_CLI" command clear_console "${P[@]}" >/dev/null
"$UNITY_CLI" command editor_play "${P[@]}" >/dev/null; sleep 8
cap 0_front
"$R" "$L" "face butterfly"; sleep 1.2; cap 1_butterfly
"$R" "$L" "face spray"; sleep 1.2; cap 2_spray
"$R" "$L" "face spray" >/dev/null; "$R" "$L" pass >/dev/null   # the test pass crosses the open sky over the lake (default view)
"$UNITY_CLI" command editor_stop "${P[@]}" >/dev/null; "$UNITY_CLI" command editor_play "${P[@]}" >/dev/null; sleep 8
"$R" "$L" pass >/dev/null; for i in 1 2 3; do sleep 2.5; cap 3_birds_$i; done
"$UNITY_CLI" command console_status "${P[@]}" | python3 -c "import json,sys; g=json.load(sys.stdin)['groundTruth']; print('console errors', g['consoleErrors'], 'warnings', g['consoleWarnings'])"
"$UNITY_CLI" command editor_stop "${P[@]}" >/dev/null
mkdir -p "$ROOT/renders/unity/life"; cp "$UNITY_PROJECT"/Assets/PlayCapture/life_*.png "$ROOT/renders/unity/life/"
"$R" "$ROOT/unity/cleanup_temp.cs" >/dev/null
echo "captures -> renders/unity/life/"
