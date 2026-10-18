#!/bin/bash
# Watch the animals wander in the forest: Play Mode, N captures every S seconds of the fixed viewer view + a probe line
# per capture (renders/unity/wander/w_XX.png).   N=12 S=4 unity/wander_test.sh
set -euo pipefail
source "$(dirname "$0")/../tools/env.sh"
P=(--project-path "$UNITY_PROJECT" --no-banner --format json --result-only)
R="$ROOT/tools/unity_run.sh"; N="${N:-12}"; S="${S:-4}"
cap() { "$UNITY_CLI" command capture_game_view "${P[@]}" -- --source screen --width 1600 --height 1000 --save_path "PlayCapture/$1.png" >/dev/null; }
if [ "$("$R" "$ROOT/unity/editor_state.cs")" = "playing" ]; then echo "editor is in Play Mode: stop it first"; exit 1; fi
MUTE_WAS=$("$R" "$ROOT/unity/editor_mute.cs" state); "$R" "$ROOT/unity/editor_mute.cs" on >/dev/null
trap '"$R" "$ROOT/unity/editor_mute.cs" "$MUTE_WAS" >/dev/null' EXIT
"$UNITY_CLI" command clear_console "${P[@]}" >/dev/null
"$UNITY_CLI" command editor_play "${P[@]}" >/dev/null
for _ in $(seq 1 30); do [ "$("$R" "$ROOT/unity/editor_state.cs")" = "playing" ] && break; sleep 2; done; sleep 3
for i in $(seq -w 1 "$N"); do "$R" "$ROOT/unity/wander_probe.cs" | sed "s/^/$i /"; cap "w_$i"; sleep "$S"; done
"$UNITY_CLI" command console_status "${P[@]}" | python3 -c "import json,sys; g=json.load(sys.stdin)['groundTruth']; print('console errors', g['consoleErrors'], 'warnings', g['consoleWarnings'])"
"$UNITY_CLI" command editor_stop "${P[@]}" >/dev/null
mkdir -p "$ROOT/renders/unity/wander"; sleep 1; mv "$UNITY_PROJECT"/Assets/PlayCapture/w_*.png "$ROOT/renders/unity/wander/"
"$R" "$ROOT/unity/cleanup_temp.cs" >/dev/null
