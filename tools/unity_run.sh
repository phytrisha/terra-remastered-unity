#!/bin/bash
# Run a C# eval script (unity/*.cs) in the open Unity Editor and print its returned string.
#
#   tools/unity_run.sh unity/import_assets.cs [args...]
#
# Always runs detached + `job wait`: plain `eval` has a ~5 s main-thread limit, imports and bakes take longer.
# Arguments are written to unity/args/<script>.txt (one per line; none given = keep the file as it is, so a
# version-controlled shot list or route file can live there). The script sees them as `ARGS` and the project root as
# `ROOT`. Eval scripts are top-level C# statements ending in `return "<text>";`.
# Compile-check without side effects: COMPILE_ONLY=1 tools/unity_run.sh <script.cs>
set -euo pipefail
source "$(dirname "$0")/env.sh"
SCRIPT="$1"; shift
NAME="$(basename "$SCRIPT" .cs)"
mkdir -p "$ROOT/unity/args"
if [ $# -gt 0 ]; then printf '%s\n' "$@" > "$ROOT/unity/args/$NAME.txt"; else touch "$ROOT/unity/args/$NAME.txt"; fi
# The script gets two variables prepended: ROOT (project root) and ARGS (its argument lines).
RUN="$ROOT/unity/.run_$NAME.cs"; trap 'rm -f "$RUN"' EXIT
{
  [ "${COMPILE_ONLY:-0}" = 1 ] && echo 'if (System.DateTime.Now.Year > 0) return "compile-ok";'
  echo "var ROOT = @\"$ROOT\";"
  echo "var ARGS = System.IO.File.ReadAllLines(System.IO.Path.Combine(ROOT, \"unity\", \"args\", \"$NAME.txt\")).Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith(\"#\")).ToArray();"
  cat "$SCRIPT"
} > "$RUN"
COMMON=(--project-path "$UNITY_PROJECT" --no-banner --format json)
# The editor refuses jobs while it compiles/imports (data = null): wait and retry for up to ~3 min.
JOB=""
for _try in $(seq 1 60); do
  JOB=$("$UNITY_CLI" command eval_file --file "$RUN" --detach "${COMMON[@]}" 2>&1 \
        | python3 -c "import json,sys
try: print((json.load(sys.stdin).get('data') or {}).get('jobId') or '')
except Exception: print('')")
  [ -n "$JOB" ] && break
  sleep 3
done
[ -n "$JOB" ] || { echo "unity_run: editor did not accept the job (busy or not connected)"; exit 1; }
"$UNITY_CLI" job wait "$JOB" --timeout 0 "${COMMON[@]}" 2>&1 | python3 -c "
import json, sys
raw = sys.stdin.read()
def find(o):
    if isinstance(o, dict):
        if isinstance(o.get('result'), str): return o['result']
        for v in o.values():
            r = find(v)
            if r: return r
    if isinstance(o, list):
        for v in o:
            r = find(v)
            if r: return r
try: d = json.loads(raw); print(find(d) or json.dumps(d)[:4000])
except Exception: print(raw[:4000])"
