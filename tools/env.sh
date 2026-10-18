#!/bin/bash
# Sourced by every tool: sets ROOT and loads .env (falling back to .env.example), so no script hard-codes a machine path.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export ROOT
_envfile="$ROOT/.env"; [ -f "$_envfile" ] || _envfile="$ROOT/.env.example"
while IFS= read -r line || [ -n "$line" ]; do
  [[ "$line" =~ ^[[:space:]]*(#|$) ]] && continue
  key="${line%%=*}"; val="${line#*=}"
  [ -n "${!key:-}" ] && continue                      # an exported variable wins over the file
  export "$key=$(eval echo "$val")"
done < "$_envfile"
unset _envfile line key val
