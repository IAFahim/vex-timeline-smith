#!/usr/bin/env bash
# Pack all NuGet packages into ./artifacts/nuget (Release).
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
cd "$root"

dotnet test -c Release --nologo
rm -rf artifacts/nuget
mkdir -p artifacts/nuget

for proj in \
  src/Vex.TimelineSmith.Ir \
  src/Vex.TimelineSmith.Runtime \
  src/Vex.TimelineSmith.Compiler \
  src/Vex.TimelineSmith.UnityYaml \
  src/Vex.TimelineSmith.Tool
do
  echo "== pack $proj =="
  dotnet pack "$proj" -c Release --nologo
done

echo
echo "Packages:"
ls -la artifacts/nuget/
echo
echo "Install tool locally:"
echo "  dotnet tool install -g vex-timeline-smith --add-source $root/artifacts/nuget --version 0.1.0"
echo "Push (when ready):"
echo "  dotnet nuget push artifacts/nuget/*.nupkg -k \$NUGET_API_KEY -s https://api.nuget.org/v3/index.json --skip-duplicate"
