#!/bin/bash
# Animation QA: Play Mode, sample an animal for FRAMES frames at 30 fps game time (PlayMotionSampler), then
# tools/motion_report.py: paw slip during stance, ground penetration / floating, pops.   ANIMAL=PF_WhiteTiger FRAMES=900
# NOLOCO=1: QuadrupedLocomotion off (the clips alone)
set -euo pipefail
source "$(dirname "$0")/../tools/env.sh"
P=(--project-path "$UNITY_PROJECT" --no-banner --format json --result-only)
R="$ROOT/tools/unity_run.sh"; M="$ROOT/unity/motion_sample.cs"; ANIMAL="${ANIMAL:-PF_WhiteTiger}"; FRAMES="${FRAMES:-900}"
for _ in $(seq 1 100); do "$UNITY_CLI" status 2>/dev/null | grep -q "ready" && break; sleep 3; done   # after a recompile
if [ "$("$R" "$ROOT/unity/editor_state.cs")" = "playing" ]; then echo "editor is in Play Mode: stop it first"; exit 1; fi
MUTE_WAS=$("$R" "$ROOT/unity/editor_mute.cs" state); "$R" "$ROOT/unity/editor_mute.cs" on >/dev/null
trap '"$R" "$ROOT/unity/editor_mute.cs" "$MUTE_WAS" >/dev/null' EXIT
"$UNITY_CLI" command editor_play "${P[@]}" >/dev/null
for _ in $(seq 1 30); do [ "$("$R" "$ROOT/unity/editor_state.cs")" = "playing" ] && break; sleep 2; done; sleep 2
"$R" "$M" "start $ANIMAL $FRAMES 30 ${NOLOCO:+noloco}" >/dev/null
for _ in $(seq 1 600); do case "$("$R" "$M" busy)" in done*) break;; esac; sleep 1; done
"$UNITY_CLI" command console_status "${P[@]}" | python3 -c "import json,sys; g=json.load(sys.stdin)['groundTruth']; print('console errors', g['consoleErrors'], 'warnings', g['consoleWarnings'])"
"$UNITY_CLI" command editor_stop "${P[@]}" >/dev/null
printf 'busy\n' > "$ROOT/unity/args/motion_sample.txt"
python3 "$ROOT/tools/motion_report.py" "$ROOT/renders/unity/motion_$ANIMAL.json" ${REPORT_ARGS:-}
