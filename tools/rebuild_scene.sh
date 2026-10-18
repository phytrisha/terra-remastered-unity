#!/bin/bash
# Recompile (if scripts changed), rebuild Assets/Scenes/Rainforest.unity, bake lighting and capture the fixed shot list.
# The rebuild recreates every object, so the old lightmaps no longer match: always rebake before committing.
#   tools/rebuild_scene.sh            build + bake + capture
#   NO_BAKE=1 tools/rebuild_scene.sh  quick iteration (lighting stale until the next bake)
#   NO_CAPTURE=1 tools/rebuild_scene.sh
#   QUICK=1 tools/rebuild_scene.sh     skip the overlap pass (~1 min instead of ~20; plants may interpenetrate)
set -euo pipefail
source "$(dirname "$0")/env.sh"
wait_ready() { for _ in $(seq 1 100); do "$UNITY_CLI" status 2>/dev/null | grep -q "ready" && return 0; sleep 3; done; echo "editor not ready"; exit 1; }
wait_ready
if [ "$("$ROOT/tools/unity_run.sh" "$ROOT/unity/editor_state.cs")" = "playing" ]; then echo "editor is in Play Mode: stop it first (not touching a running session)"; exit 1; fi
"$UNITY_CLI" recompile --project-path "$UNITY_PROJECT" --timeout 380 >/dev/null 2>&1 || true
sleep 3; wait_ready
"$ROOT/tools/unity_run.sh" "$ROOT/unity/build_scene.cs" "$([ "${QUICK:-0}" = 1 ] && echo quick || echo full)"
[ "${NO_BAKE:-0}" = 1 ] || "$ROOT/tools/bake_lighting.sh"
[ "${NO_CAPTURE:-0}" = 1 ] || "$ROOT/tools/unity_run.sh" "$ROOT/unity/capture.cs"
