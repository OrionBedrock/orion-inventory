#!/usr/bin/env bash
# Pack Orion SDK from checked-out OrionServerBE so plugin build matches server runtime.
set -euo pipefail

SERVER_DIR="${ORION_SERVER_ROOT:-${GITHUB_WORKSPACE:?GITHUB_WORKSPACE required}/OrionServerBE}"
NUGET_DIR="${ORION_LOCAL_NUGET:-${GITHUB_WORKSPACE}/local-nuget}"

mkdir -p "$NUGET_DIR"
cd "$SERVER_DIR"

dotnet restore OrionServerBE.slnx
for proj in \
  src/PluginContracts/PluginContracts.csproj \
  src/Orion.Api/Orion.Api.csproj \
  src/Orion.Gameplay.Api/Orion.Gameplay.Api.csproj \
  src/Protocol/Protocol.csproj
do
  dotnet pack "$proj" -c Release -o "$NUGET_DIR"
done

if dotnet nuget list source | grep -q orion-local; then
  dotnet nuget update source orion-local --source "$NUGET_DIR"
else
  dotnet nuget add source "$NUGET_DIR" --name orion-local
fi

PLUGIN_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# CI checkouts are ephemeral; rewrite nuget.config so Orion.* resolves from packed server SDK.
cat > "$PLUGIN_DIR/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="orion-local" value="$NUGET_DIR" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="orion-local">
      <package pattern="Orion.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF

ls -la "$NUGET_DIR"
