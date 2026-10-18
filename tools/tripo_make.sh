#!/bin/bash
# Generate a model with Tripo into sources/tripo/<Name>/ and log what it cost.
#
#   tools/tripo_make.sh <Name> "<prompt or image path>" [extra tripo make flags...]
#   tools/tripo_make.sh Barrel "a weathered oak barrel with iron hoops, game asset" --for game-pc
#   tools/tripo_make.sh Lamp sources/reference/lamp_concept.png --for game-mobile -p face_limit=3000
#   DRY=1 tools/tripo_make.sh ...      plan only: zero credits, prints the request bodies
#
# Defaults: --for $TRIPO_SCENARIO (game-pc). The scenario's chain adds a plain FBX convert; ingest prefers the GLB
# (PBR materials survive best) and Blender writes the engine FBX itself. Avoid non-default convert options (pivot,
# scale, orientation): they bill as "complex convert" and ingest_mesh.py does the same for free.
# Always dry-runs first and stops on plan errors.
# Next step: ASSET=<Name> SRC=sources/tripo/<Name> HEIGHT=<m> TRIS=<budget> tools/blender_run.sh blender/scripts/ingest_mesh.py
# Exit codes (from tripo): 3 auth -> tripo login; 4 no credits -> tripo topup; 5 content policy; 6 task failed (refunded);
# 7 network (check `tripo balance` before re-running: a failed submit may still exist); 9 rate limit.
set -euo pipefail
source "$(dirname "$0")/env.sh"
NAME="$1"; INPUT="$2"; shift 2
OUT="$ROOT/sources/tripo/$NAME"
SCEN=(--for "${TRIPO_SCENARIO:-game-pc}")
for a in "$@"; do [ "$a" = "--for" ] && SCEN=(); done
PLAN=$(tripo make "$INPUT" "${SCEN[@]}" -o "$OUT" "$@" --dry-run --json 2>/dev/null) || { echo "$PLAN"; echo "plan invalid"; exit 2; }
echo "$PLAN" | python3 -c "
import json, sys
r = json.loads(sys.stdin.read().strip().splitlines()[-1])
print('plan:', r.get('model'), '|', ' -> '.join(s.get('name', '?') for s in r.get('steps', [])), '| deliverables', r.get('deliverables'))
for n in r.get('cost_notes', []): print('  cost:', n)
for w in r.get('warnings', []): print('  warning:', w)
for e in r.get('errors', []): print('  ERROR:', e)" || true
[ "${DRY:-0}" = 1 ] && { echo "$PLAN"; exit 0; }
mkdir -p "$OUT"
RES=$(tripo make "$INPUT" "${SCEN[@]}" -o "$OUT" "$@" --json --yes)
echo "$RES" > "$OUT/result.json"
echo "$RES" | python3 -c "
import json, sys, datetime, os
d = json.loads(sys.stdin.read().strip().splitlines()[-1])
line = '\t'.join([datetime.date.today().isoformat(), '$NAME', str(d.get('task_id')), str(d.get('credits_consumed')),
                  str(d.get('model_file')), '''$INPUT'''.replace('\t', ' ')])
log = os.path.join('$ROOT', 'sources', 'tripo', 'LOG.tsv')
new = not os.path.exists(log)
with open(log, 'a') as f:
    if new: f.write('date\tname\ttask_id\tcredits\tmodel_file\tinput\n')
    f.write(line + '\n')
print('model:', d.get('model_file')); print('preview:', d.get('preview')); print('credits:', d.get('credits_consumed'))"
