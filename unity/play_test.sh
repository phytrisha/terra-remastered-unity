#!/bin/bash
# Enter Play Mode, wait, capture the game camera to renders/unity/play.png, report console errors, exit Play Mode.
set -euo pipefail
source "$(dirname "$0")/../tools/env.sh"
P=(--project-path "$UNITY_PROJECT" --no-banner --format json --result-only)
MUTE_WAS=$("$ROOT/tools/unity_run.sh" "$ROOT/unity/editor_mute.cs" state)
"$ROOT/tools/unity_run.sh" "$ROOT/unity/editor_mute.cs" on >/dev/null   # tests never play sound
trap '"$ROOT/tools/unity_run.sh" "$ROOT/unity/editor_mute.cs" "$MUTE_WAS" >/dev/null' EXIT
"$UNITY_CLI" command clear_console "${P[@]}" >/dev/null
"$UNITY_CLI" command editor_play "${P[@]}" >/dev/null
sleep "${WAIT:-12}"
"$UNITY_CLI" command capture_game_view "${P[@]}" -- --source camera --width 1600 --height 1000 --save_path PlayCapture/play.png >/dev/null
"$UNITY_CLI" command console_status "${P[@]}" | python3 -c "import json,sys; g=json.load(sys.stdin)['groundTruth']; print('console errors', g['consoleErrors'], 'warnings', g['consoleWarnings'])"
"$UNITY_CLI" command editor_stop "${P[@]}" >/dev/null
cp "$UNITY_PROJECT/Assets/PlayCapture/play.png" "$ROOT/renders/unity/play.png"
"$ROOT/tools/unity_run.sh" "$ROOT/unity/cleanup_temp.cs" >/dev/null
echo "play capture -> renders/unity/play.png"
