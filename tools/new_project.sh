#!/bin/bash
# Start a new project from this template.
#
#   tools/new_project.sh ~/Dev/my-project "My Project"
#
# Copies the template (without generated files and the example asset's outputs), creates .env, sets the project
# name in PROJECT.md, initialises git + Git LFS, and makes the first commit. Then:
#   1. open UnityProject/ with Unity 6.6 (Hub > Add > the folder), let it import, run Tools > Asset Pipeline > Setup Project
#      (or: tools/unity_run.sh with `return ProjectSetup.Run();`)
#   2. fill in PROJECT.md (brief, budgets, art direction), add the AI Hub key to .claude/settings.local.json
#   3. `tripo doctor`, then build the example: ASSET=Crate tools/blender_run.sh blender/assets/Crate/build.py
set -euo pipefail
SRC="$(cd "$(dirname "$0")/.." && pwd)"
DST="${1:?usage: new_project.sh <target dir> [\"Project Name\"]}"
NAME="${2:-$(basename "$DST")}"
[ -e "$DST" ] && { echo "$DST exists"; exit 1; }
mkdir -p "$DST"
rsync -a \
  --exclude .git --exclude .env --exclude .claude/settings.local.json \
  --exclude 'UnityProject/Library' --exclude 'UnityProject/Temp' --exclude 'UnityProject/Logs' \
  --exclude 'UnityProject/UserSettings' --exclude 'UnityProject/obj' --exclude '*.csproj' --exclude '*.sln*' \
  --exclude 'blender/export' --exclude 'renders' --exclude 'unity/reports' --exclude '*.blend1' \
  --exclude '.claude/scheduled_tasks.lock' --exclude 'blender/.current_*' --exclude 'unity/.run_*' --exclude '__pycache__' --exclude '.DS_Store' \
  "$SRC/" "$DST/"
cp "$DST/.env.example" "$DST/.env"
[ -f "$DST/.claude/settings.local.json" ] || cp "$DST/.claude/settings.local.json.example" "$DST/.claude/settings.local.json"
python3 - "$DST/PROJECT.md" "$NAME" <<'EOF'
import sys
p, name = sys.argv[1], sys.argv[2]
s = open(p).read().replace("# <Project name>", "# " + name, 1)
open(p, "w").write(s)
EOF
cd "$DST"
git init -q -b main
if git lfs version >/dev/null 2>&1; then git lfs install --local >/dev/null; else echo "warning: git-lfs not installed (brew install git-lfs); large files would go into plain git"; fi
git add -A
git commit -q -m "Start $NAME from unity-build-template"
echo "created $DST ($NAME)"
echo "next: open UnityProject/ in Unity 6.6, then Tools > Asset Pipeline > Setup Project; fill in PROJECT.md"
