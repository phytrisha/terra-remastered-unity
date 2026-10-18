#!/bin/bash
# Compile the game + editor C# offline (no editor recompile, no Safe Mode prompt on errors), with the CURRENT file list.
#
#   tools/compile_check.sh            -> "compile_check: OK" or the compiler errors
#
# Uses the editor's last Bee response files (references, defines) and swaps in the .cs files under Assets/ (Editor
# folders go to the editor assembly), so new files are checked too. Output goes to a temp folder, never Library/.
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$UNITY_PROJECT"
DN="$(dirname "$UNITY_CLI")"
CSC=$(find /Applications/Unity/Hub/Editor/*/Unity.app/Contents/Resources/Scripting/DotNetSdk -name csc.dll -path "*Roslyn/bincore*" 2>/dev/null | sort | tail -1)
DOTNET=$(dirname "$(dirname "$(dirname "$(dirname "$CSC")")")")/../dotnet
[ -x "$DOTNET" ] || DOTNET=$(find "$(dirname "$CSC")/../../../.." -maxdepth 2 -name dotnet -type f | head -1)
OUT=$(mktemp -d)
trap 'rm -rf "$OUT"' EXIT
RT_RSP=$(ls -t Library/Bee/artifacts/*E.dag/Assembly-CSharp.rsp | head -1)
ED_RSP=$(ls -t Library/Bee/artifacts/*E.dag/Assembly-CSharp-Editor.rsp | head -1)
RT_SRC=$(find Assets -name "*.cs" -not -path "*/Editor/*" -not -path "Assets/ThirdParty/*/Editor/*" | sort)
ED_SRC=$(find Assets -name "*.cs" -path "*/Editor/*" | sort)
mk() {  # rsp, sources, out dll, extra ref
  grep -v -E '\.cs"?$|^-out:|^-refout:|^-doc:' "$1" | sed -E "s#\"[^\"]*/Assembly-CSharp(\.ref)?\.dll\"#\"$OUT/Assembly-CSharp.dll\"#"
  echo "-out:\"$3\""
  echo "$2" | sed 's/.*/"&"/'
}
mk "$RT_RSP" "$RT_SRC" "$OUT/Assembly-CSharp.dll" > "$OUT/rt.rsp"
mk "$ED_RSP" "$ED_SRC" "$OUT/Assembly-CSharp-Editor.dll" > "$OUT/ed.rsp"
status=0
for a in rt ed; do
  if ! "$DOTNET" "$CSC" /nostdlib /noconfig "@$OUT/$a.rsp" > "$OUT/$a.log" 2>&1; then
    grep -E "error CS" "$OUT/$a.log" | sort -u | head -40; status=1; break
  fi
done
[ $status = 0 ] && echo "compile_check: OK ($(echo "$RT_SRC" | wc -l | tr -d ' ') game + $(echo "$ED_SRC" | wc -l | tr -d ' ') editor files)"
exit $status
