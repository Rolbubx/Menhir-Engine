#!/usr/bin/env bash
# Makes the installer package: artifacts/Menhir-Setup-<rid>/ with MenhirSetup(.exe) and Menhir.zip, and a zip of both.
#   Installer/package.sh [rid]        rid: win-x64 (default), linux-x64, osx-arm64...
# The editor is published self-contained (it carries its .NET runtime); pass --framework-dependent to make a
# smaller package that needs .NET 10 on the machine (the installer then asks where it is).
set -euo pipefail
cd "$(dirname "$0")/.."

rid="win-x64"
self_contained=true
for arg in "$@"; do
    case "$arg" in
        --framework-dependent) self_contained=false ;;
        *) rid="$arg" ;;
    esac
done

out="artifacts/Menhir-Setup-$rid"
stage="artifacts/stage-$rid"
rm -rf "$out" "$stage"
mkdir -p "$out" "$stage"

echo "== Editor ($rid, self-contained: $self_contained)"
dotnet publish Editor/Menhir.Editor -c Release -r "$rid" --self-contained "$self_contained" \
    -p:RuntimeIdentifier="$rid" -o "$stage/Editor" -nologo -v q

echo "== Engine, sample, docs"
copy_tree() { # source destination, without build outputs and caches
    mkdir -p "$2"
    (cd "$1" && find . -type f ! -path '*/bin/*' ! -path '*/obj/*' ! -path '*/Cache/*' ! -path '*/Build/*' -print0) |
        while IFS= read -r -d '' file; do
            mkdir -p "$2/$(dirname "$file")"
            cp "$1/$file" "$2/$file"
        done
}
copy_tree Engine "$stage/Engine"
copy_tree Samples/Sandbox "$stage/Samples/Sandbox"
copy_tree Docs "$stage/Docs"
cp README.md "$stage/"

echo "== Menhir.zip"
(cd "$stage" && rm -f "../../$out/Menhir.zip" && zip -qr -X "../../$out/Menhir.zip" .)

echo "== Installer"
dotnet publish Installer/Menhir.Setup -c Release -r "$rid" --self-contained true -p:PublishSingleFile=true \
    -p:PublishTrimmed=true -p:DebugType=none -o "$out" -nologo -v q

(cd artifacts && rm -f "Menhir-Setup-$rid.zip" && zip -qr -X "Menhir-Setup-$rid.zip" "Menhir-Setup-$rid")
rm -rf "$stage"
ls -lh "$out" "artifacts/Menhir-Setup-$rid.zip"
