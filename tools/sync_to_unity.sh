#!/bin/bash
# Copy Blender exports into the Unity project and run the import step (materials, prefab, report).
#
#   tools/sync_to_unity.sh Props Crate [Buildings Shed ...]      category/name pairs (folders under blender/export/)
#   NO_IMPORT=1 tools/sync_to_unity.sh Props Crate               copy only (Unity closed; import later)
#
# Destination: Assets/Art/Environment/<Category>/<Name>/ (Characters: Assets/Art/Characters/<Name>/), LODs included.
# Overwriting files in place keeps their .meta GUIDs, so prefab and scene references survive a re-export.
# Never move/rename/delete inside Assets/ from the shell: do that through Unity (AssetDatabase.MoveAsset).
set -euo pipefail
source "$(dirname "$0")/env.sh"
[ $# -ge 2 ] || { echo "usage: $0 <Category> <Name> [<Category> <Name> ...]"; exit 2; }
ARGS=()
while [ $# -ge 2 ]; do
  CAT="$1"; NAME="$2"; shift 2
  SRC="$ROOT/blender/export/$CAT/$NAME"
  [ -d "$SRC" ] || { echo "missing $SRC (export first)"; exit 1; }
  if [ "$CAT" = "Characters" ]; then DST="$UNITY_PROJECT/Assets/Art/Characters/$NAME"
  else DST="$UNITY_PROJECT/Assets/Art/Environment/$CAT/$NAME"; fi
  mkdir -p "$DST"
  for f in "$SRC"/*.fbx; do cp "$f" "$DST/"; done
  # texture sets exported with the asset (unique/baked maps); shared tileables go to Art/Textures by hand or by script
  [ -d "$SRC/Textures" ] && { mkdir -p "$DST/Textures"; cp "$SRC"/Textures/T_*.png "$DST/Textures/" 2>/dev/null || true; }
  echo "copied $CAT/$NAME -> ${DST#$ROOT/}"
  ARGS+=("$CAT $NAME")
done
[ "${NO_IMPORT:-0}" = 1 ] && exit 0
"$ROOT/tools/unity_run.sh" "$ROOT/unity/import_assets.cs" "${ARGS[@]}"
