#!/usr/bin/env bash
# Builds the add-in for Revit 2022-2026, runs the unit tests and creates
#   dist/CAD2Revit-<version>.zip
# Usage (from the addin folder):  bash tools/package.sh
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
NAME="CAD2Revit-$VERSION"
OUT="dist/$NAME"

dotnet test tests/CAD2Revit.Core.Tests -c Release -nologo -v q
rm -rf dist && mkdir -p "$OUT/templates"
for v in 2022 2023 2024 2025 2026; do
  dotnet build src/CAD2Revit -c Release -p:RevitVersion=$v -nologo -v q -warnaserror
  mkdir -p "$OUT/$v/CAD2Revit"
  cp "src/CAD2Revit/bin/Release/$v/CAD2Revit.dll" "$OUT/$v/CAD2Revit/"
  cp package/CAD2Revit.addin "$OUT/$v/"
done
cp package/install.ps1 package/Install.bat package/Uninstall.bat package/README.txt "$OUT/"
cp ../templates/mapping_template.xlsx ../templates/mapping_template.csv "$OUT/templates/"
(cd dist && python3 -m zipfile -c "$NAME.zip" "$NAME")
echo "Created dist/$NAME.zip"
