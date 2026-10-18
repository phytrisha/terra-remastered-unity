#!/bin/bash
# Record the tiger's low-poly -> HD portal transition as a video: Play Mode, start the encounter, capture every frame at
# a fixed 30 fps game-time step (PlayFrameRecorder), stop, encode renders/unity/transition.mp4 + a contact sheet.
#   unity/transition_test.sh            (FRAMES=150 FPS=30 by default: 5 s of game time from the click)
set -euo pipefail
source "$(dirname "$0")/../tools/env.sh"
P=(--project-path "$UNITY_PROJECT" --no-banner --format json --result-only)
R="$ROOT/tools/unity_run.sh"; S="$ROOT/unity/encounter_test.cs"; REC="$ROOT/unity/transition_record.cs"
FRAMES="${FRAMES:-150}"; FPS="${FPS:-30}"
if [ "$("$R" "$ROOT/unity/editor_state.cs")" = "playing" ]; then echo "editor is in Play Mode: stop it first (not touching a running session)"; exit 1; fi
MUTE_WAS=$("$R" "$ROOT/unity/editor_mute.cs" state)
"$R" "$ROOT/unity/editor_mute.cs" on >/dev/null   # tests never play sound
trap '"$R" "$ROOT/unity/editor_mute.cs" "$MUTE_WAS" >/dev/null' EXIT
"$UNITY_CLI" command clear_console "${P[@]}" >/dev/null
"$UNITY_CLI" command editor_play "${P[@]}" >/dev/null
sleep "${WAIT:-10}"
"$R" "$REC" "record $FRAMES $FPS" >/dev/null
"$R" "$S" start >/dev/null
for _ in $(seq 1 240); do case "$("$R" "$REC" busy)" in done*) break;; esac; sleep 1; done
"$R" "$S" state
"$UNITY_CLI" command console_status "${P[@]}" | python3 -c "import json,sys; g=json.load(sys.stdin)['groundTruth']; print('console errors', g['consoleErrors'], 'warnings', g['consoleWarnings'])"
"$UNITY_CLI" command editor_stop "${P[@]}" >/dev/null
printf 'state\n' > "$ROOT/unity/args/encounter_test.txt"; printf 'busy\n' > "$ROOT/unity/args/transition_record.txt"
D="$ROOT/renders/unity/transition"; sleep 2
N=$(ls "$D"/f_*.png 2>/dev/null | wc -l | tr -d ' '); echo "frames $N"
ffmpeg -y -loglevel error -framerate "$FPS" -i "$D/f_%04d.png" -vf "scale=trunc(iw/2)*2:trunc(ih/2)*2" -c:v libx264 -pix_fmt yuv420p -crf 18 "$ROOT/renders/unity/transition.mp4"
python3 "$ROOT/tools/sheet.py" "$ROOT/renders/unity/transition_sheet.png" $(ls "$D"/f_*.png | awk 'NR%6==1') --cols 5 --width 3000 >/dev/null
echo "video -> renders/unity/transition.mp4, sheet -> renders/unity/transition_sheet.png"
