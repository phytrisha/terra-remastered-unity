#!/bin/bash
# Quick look at the HD tiger in the white room: Play Mode, encounter at 4x through the transition, then captures of the
# showcase and hotspot close-ups (renders/unity/fur/<TAG>_*.png, CAPW x CAPH, default 2560x1600).
#   TAG=r2 unity/fur_test.sh
set -euo pipefail
source "$(dirname "$0")/../tools/env.sh"
P=(--project-path "$UNITY_PROJECT" --no-banner --format json --result-only)
R="$ROOT/tools/unity_run.sh"; S="$ROOT/unity/encounter_test.cs"; TAG="${TAG:-fur}"
cap() { "$UNITY_CLI" command capture_game_view "${P[@]}" -- --source screen --width "${CAPW:-2560}" --height "${CAPH:-1600}" --save_path "PlayCapture/${TAG}_$1.png" >/dev/null; }
if [ "$("$R" "$ROOT/unity/editor_state.cs")" = "playing" ]; then echo "editor is in Play Mode: stop it first"; exit 1; fi
MUTE_WAS=$("$R" "$ROOT/unity/editor_mute.cs" state); "$R" "$ROOT/unity/editor_mute.cs" on >/dev/null
trap '"$R" "$ROOT/unity/editor_mute.cs" "$MUTE_WAS" >/dev/null' EXIT
"$UNITY_CLI" command clear_console "${P[@]}" >/dev/null
"$UNITY_CLI" command editor_play "${P[@]}" >/dev/null
for _ in $(seq 1 30); do [ "$("$R" "$ROOT/unity/editor_state.cs")" = "playing" ] && break; sleep 2; done; sleep 4
"$R" "$S" "timescale 4" >/dev/null; "$R" "$S" start >/dev/null; sleep 3
"$R" "$S" "timescale 1" >/dev/null; sleep 3; cap showcase
for i in ${SPOTS:-0 1 2 4 5 6}; do "$R" "$S" "focus $i" >/dev/null; sleep 2.6; cap "spot$i"; done
"$UNITY_CLI" command console_status "${P[@]}" | python3 -c "import json,sys; g=json.load(sys.stdin)['groundTruth']; print('console errors', g['consoleErrors'], 'warnings', g['consoleWarnings'])"
"$UNITY_CLI" command editor_stop "${P[@]}" >/dev/null
printf 'state\n' > "$ROOT/unity/args/encounter_test.txt"
mkdir -p "$ROOT/renders/unity/fur"; sleep 1; mv "$UNITY_PROJECT"/Assets/PlayCapture/${TAG}_*.png "$ROOT/renders/unity/fur/"
"$R" "$ROOT/unity/cleanup_temp.cs" >/dev/null
echo "captures -> renders/unity/fur/${TAG}_*"
