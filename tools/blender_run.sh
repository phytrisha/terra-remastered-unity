#!/bin/bash
# Run a Blender step script with the shared libs prepended.
#
#   tools/blender_run.sh <step.py> [file.blend]      headless: private Blender process (safe for parallel sessions)
#   tools/blender_run.sh --live <step.py>            live: the Blender you have open, through the MCP socket
#
# Contract (same in both modes): the step assigns a JSON-serialisable top-level `result`; it is printed as
# one `RESULT_JSON {...}` line. Env vars pass parameters (e.g. ASSET=Crate EXPORT=1 tools/blender_run.sh ...).
# Headless output is filtered to RESULT_JSON lines and errors; set VERBOSE=1 for everything.
set -euo pipefail
source "$(dirname "$0")/env.sh"
LIVE=0; [ "${1:-}" = "--live" ] && { LIVE=1; shift; }
STEP="$1"; BLEND="${2:-}"
LIBS="${BLENDER_LIBS:-kit export audit review}"
TMP="$ROOT/blender/.current_$$.py"; trap 'rm -f "$TMP"' EXIT
{
  echo "import os, sys; ROOT = r'''$ROOT'''; os.environ.setdefault('ROOT', ROOT)"
  echo "result = None"
  for l in $LIBS; do cat "$ROOT/blender/lib/$l.py"; echo; done
  cat "$STEP"; echo
  echo 'import json as _j; print("RESULT_JSON " + _j.dumps(globals().get("result"), default=str))'
} > "$TMP"

if [ $LIVE = 1 ]; then
  "$BLENDER_MCP_PYTHON" -I "$ROOT/tools/blender_live.py" "$TMP"
else
  ARGS=(-b); [ -n "$BLEND" ] && ARGS+=("$BLEND"); ARGS+=(--python-exit-code 1 --python "$TMP")
  if [ "${VERBOSE:-0}" = 1 ]; then "$BLENDER" "${ARGS[@]}"
  else "$BLENDER" "${ARGS[@]}" 2>&1 | grep -E "RESULT_JSON|Error|Traceback|line [0-9]+|Exception|WARN" | head -80; fi
fi
