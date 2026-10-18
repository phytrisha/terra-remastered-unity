#!/bin/bash
# Bake lighting for the open Rainforest scene without blocking the editor: start BakeAsync, poll, then save + report.
#   tools/bake_lighting.sh            (report also goes to unity/reports/bake_last.txt)
set -euo pipefail
source "$(dirname "$0")/env.sh"
R="$ROOT/tools/unity_run.sh"; S="$ROOT/unity/bake_lighting.cs"
start=$(date +%s)
out=$("$R" "$S" start); echo "$out"; case "$out" in FAIL*|*Runtime\ Error*|*\"error\"*) echo "bake did not start"; exit 1;; esac
sleep 5
while [ "$("$R" "$S" status)" = "running" ]; do sleep 10; done
"$R" "$S" report | tee "$ROOT/unity/reports/bake_last.txt"
echo "wall clock $(( $(date +%s) - start )) s" | tee -a "$ROOT/unity/reports/bake_last.txt"
printf 'start\n' > "$ROOT/unity/args/bake_lighting.txt"
