#!/bin/bash
# HD view cost: Play Mode, encounter to the showcase, then render stats in the overview and in close-ups
# (perf_probe.cs, 6 samples each). Editor numbers: compare A/B within one run only.
set -euo pipefail
source "$(dirname "$0")/../tools/env.sh"
P=(--project-path "$UNITY_PROJECT" --no-banner --format json --result-only)
R="$ROOT/tools/unity_run.sh"; S="$ROOT/unity/encounter_test.cs"; PP="$ROOT/unity/perf_probe.cs"
if [ "$("$R" "$ROOT/unity/editor_state.cs")" = "playing" ]; then echo "editor is in Play Mode: stop it first"; exit 1; fi
MUTE_WAS=$("$R" "$ROOT/unity/editor_mute.cs" state); "$R" "$ROOT/unity/editor_mute.cs" on >/dev/null
trap '"$R" "$ROOT/unity/editor_mute.cs" "$MUTE_WAS" >/dev/null' EXIT
"$UNITY_CLI" command editor_play "${P[@]}" >/dev/null
for _ in $(seq 1 30); do [ "$("$R" "$ROOT/unity/editor_state.cs")" = "playing" ] && break; sleep 2; done; sleep 4
echo "forest:"; for i in 1 2 3; do "$R" "$PP"; sleep 0.5; done
"$R" "$S" "timescale 4" >/dev/null; "$R" "$S" start >/dev/null; sleep 3; "$R" "$S" "timescale 1" >/dev/null; sleep 4
echo "showcase overview:"; for i in 1 2 3 4 5 6; do "$R" "$PP"; sleep 0.5; done
"$R" "$S" "focus 0" >/dev/null; sleep 3
echo "close-up (eyes):"; for i in 1 2 3 4 5 6; do "$R" "$PP"; sleep 0.5; done
"$R" "$ROOT/unity/perf_ab.cs"; sleep 2
echo "close-up, OLD setup (32 shells on the full mesh):"; for i in 1 2 3 4 5 6; do "$R" "$PP"; sleep 0.5; done
"$UNITY_CLI" command editor_stop "${P[@]}" >/dev/null
printf 'state\n' > "$ROOT/unity/args/encounter_test.txt"
