#!/usr/bin/env bash
# Build OrionServerBE, deploy this plugin (+ optional deps), boot and verify load.
set -euo pipefail

PLUGIN_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SERVER_DIR="${ORION_SERVER_ROOT:-${GITHUB_WORKSPACE:?ORION_SERVER_ROOT or GITHUB_WORKSPACE required}/OrionServerBE}"

plugin_id() { jq -r '.id' "$1/plugin.json"; }
assembly_name() { grep -oP '(?<=<AssemblyName>)[^<]+' "$1"/*.csproj | head -1; }

deploy_plugin() {
  local dir="$1"
  local id asm dest dll
  id="$(plugin_id "$dir")"
  asm="$(assembly_name "$dir")"
  dest="$SERVER_DIR/plugins/$id"
  mkdir -p "$dest"
  cp "$dir/plugin.json" "$dest/"
  if [ -f "$dir/bin/$asm.dll" ]; then
    dll="$dir/bin/$asm.dll"
  elif [ -f "$dir/$asm.dll" ]; then
    dll="$dir/$asm.dll"
  else
    echo "::error::Plugin DLL not found for $id ($asm.dll)"
    exit 1
  fi
  cp "$dll" "$dest/"
}

for dep in ${PLUGIN_DEP_DIRS:-}; do
  [ -n "$dep" ] || continue
  deploy_plugin "$dep"
done
deploy_plugin "$PLUGIN_DIR"

mkdir -p "$SERVER_DIR/config" "$SERVER_DIR/plugins" "$SERVER_DIR/worlds" "$SERVER_DIR/logs" "$SERVER_DIR/resource_packs"
rm -rf "$SERVER_DIR/worlds/default"
bash "$SERVER_DIR/scripts/first-run.sh" -y >/dev/null

cd "$SERVER_DIR"
dotnet build src/Server/Server.csproj -c Release -v q

PLUGIN_ID="$(plugin_id "$PLUGIN_DIR")"
LOG="$(mktemp)"
trap 'rm -f "$LOG"' EXIT

set +e
timeout 25 dotnet run --project src/Server/Server.csproj -c Release --no-build >"$LOG" 2>&1
run_status=$?
set -e

if grep -qi "fatal" "$LOG"; then
  echo "::error::Server logged Fatal during smoke boot"
  cat "$LOG"
  exit 1
fi

if ! grep -q "Loaded plugin '$PLUGIN_ID'" "$LOG"; then
  echo "::error::Plugin '$PLUGIN_ID' was not loaded (exit=$run_status)"
  cat "$LOG"
  exit 1
fi

if ! grep -q "Listening on" "$LOG"; then
  echo "::error::Server did not reach listening state"
  cat "$LOG"
  exit 1
fi

echo "Smoke boot OK for $PLUGIN_ID"
