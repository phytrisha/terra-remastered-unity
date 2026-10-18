#!/bin/bash
# Play-mode walkthrough of an encounter: forest -> transition frames -> white room -> two hotspots -> back.
# SPECIES=tiger (default) | tapir | chital. Captures the Game view (incl. HUD) to renders/unity/encounter/<species>_*.png
# and reports console errors.
set -euo pipefail
source "$(dirname "$0")/../tools/env.sh"
P=(--project-path "$UNITY_PROJECT" --no-banner --format json --result-only)
R="$ROOT/tools/unity_run.sh"; S="$ROOT/unity/encounter_test.cs"
SP="${SPECIES:-tiger}"
cap() { "$UNITY_CLI" command capture_game_view "${P[@]}" -- --source screen --width "${CAPW:-1600}" --height "${CAPH:-1000}" --save_path "PlayCapture/${SP}_$1.png" >/dev/null; }
if [ "$("$ROOT/tools/unity_run.sh" "$ROOT/unity/editor_state.cs")" = "playing" ]; then echo "editor is in Play Mode: stop it first (not touching a running session)"; exit 1; fi
MUTE_WAS=$("$ROOT/tools/unity_run.sh" "$ROOT/unity/editor_mute.cs" state)
"$ROOT/tools/unity_run.sh" "$ROOT/unity/editor_mute.cs" on >/dev/null   # tests never play sound
trap '"$ROOT/tools/unity_run.sh" "$ROOT/unity/editor_mute.cs" "$MUTE_WAS" >/dev/null' EXIT
"$UNITY_CLI" command clear_console "${P[@]}" >/dev/null
"$UNITY_CLI" command editor_play "${P[@]}" >/dev/null
sleep "${WAIT:-10}"
"$R" "$S" "face $SP" >/dev/null; sleep 1.5
cap enc_0_forest
TS="${TS:-$([ "$SP" = tiger ] && echo 0.2 || echo 0.45)}"; "$R" "$S" "timescale $TS" >/dev/null; "$R" "$S" "start $SP"
for i in 01 02 03 04 05 06 07 08 09 10 11 12; do sleep 0.45; cap enc_1_charge_$i; done
"$R" "$S" "timescale 1" >/dev/null
sleep 3; "$R" "$S" state; cap enc_2_showcase
"$R" "$S" "focus 0"; sleep 2.5; cap enc_3_eyes
"$R" "$S" "focus 4"; sleep 2.5; cap enc_4_coat
"$R" "$S" back; sleep 2; cap enc_5_back
"$UNITY_CLI" command console_status "${P[@]}" | python3 -c "import json,sys; g=json.load(sys.stdin)['groundTruth']; print('console errors', g['consoleErrors'], 'warnings', g['consoleWarnings'])"
"$UNITY_CLI" command editor_stop "${P[@]}" >/dev/null
mkdir -p "$ROOT/renders/unity/encounter"; cp "$UNITY_PROJECT"/Assets/PlayCapture/${SP}_enc_*.png "$ROOT/renders/unity/encounter/"
"$ROOT/tools/unity_run.sh" "$ROOT/unity/cleanup_temp.cs" >/dev/null
printf 'state\n' > "$ROOT/unity/args/encounter_test.txt"
echo "captures -> renders/unity/encounter/"
