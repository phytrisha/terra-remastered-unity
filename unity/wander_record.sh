#!/bin/bash
# Record the forest (animals wandering) as a video at a fixed 30 fps game-time step: renders/unity/wander.mp4 + frames.
#   FRAMES=300 unity/wander_record.sh
set -euo pipefail
source "$(dirname "$0")/../tools/env.sh"
P=(--project-path "$UNITY_PROJECT" --no-banner --format json --result-only)
R="$ROOT/tools/unity_run.sh"; REC="$ROOT/unity/transition_record.cs"; FRAMES="${FRAMES:-300}"
if [ "$("$R" "$ROOT/unity/editor_state.cs")" = "playing" ]; then echo "editor is in Play Mode: stop it first"; exit 1; fi
MUTE_WAS=$("$R" "$ROOT/unity/editor_mute.cs" state); "$R" "$ROOT/unity/editor_mute.cs" on >/dev/null
trap '"$R" "$ROOT/unity/editor_mute.cs" "$MUTE_WAS" >/dev/null' EXIT
"$UNITY_CLI" command clear_console "${P[@]}" >/dev/null
"$UNITY_CLI" command editor_play "${P[@]}" >/dev/null
for _ in $(seq 1 30); do [ "$("$R" "$ROOT/unity/editor_state.cs")" = "playing" ] && break; sleep 2; done; sleep "${WAIT:-3}"
"$R" "$REC" "record $FRAMES 30" >/dev/null
for _ in $(seq 1 400); do case "$("$R" "$REC" busy)" in done*) break;; esac; sleep 1; done
"$UNITY_CLI" command console_status "${P[@]}" | python3 -c "import json,sys; g=json.load(sys.stdin)['groundTruth']; print('console errors', g['consoleErrors'], 'warnings', g['consoleWarnings'])"
"$UNITY_CLI" command editor_stop "${P[@]}" >/dev/null
printf 'busy\n' > "$ROOT/unity/args/transition_record.txt"
D="$ROOT/renders/unity/transition"; sleep 2; rm -rf "$ROOT/renders/unity/wander_frames"; mv "$D" "$ROOT/renders/unity/wander_frames"
ffmpeg -y -loglevel error -framerate 30 -i "$ROOT/renders/unity/wander_frames/f_%04d.png" -vf "scale=trunc(iw/2)*2:trunc(ih/2)*2" -c:v libx264 -pix_fmt yuv420p -crf 18 "$ROOT/renders/unity/wander.mp4"
echo "video -> renders/unity/wander.mp4"
