#!/usr/bin/env bash
# Makes Menhir's installer, in artifacts/:
#   Windows (win-x64, the default): Menhir-Setup-win-x64.exe, a Windows installer made by Inno Setup 6
#     (Installer/Windows/Menhir.iss). Needs Inno Setup's ISCC: found in its usual folder or on the PATH, or given
#     by the ISCC variable (e.g. ISCC="wine C:/Inno/ISCC.exe" to make it from Linux with Wine).
#   Other platforms: Menhir-Setup-<rid>.zip, with the console installer (MenhirSetup) and Menhir.zip.
#
#   Installer/package.sh [rid] [--version X.Y.Z] [--framework-dependent] [--zip]
#
# The version defaults to Directory.Build.props'; the release workflow gives the tag's. The editor is published
# self-contained (it carries its .NET runtime); --framework-dependent makes it need .NET 10 on the machine (zip
# packages only: the console installer then asks where .NET is). --zip makes the zip package for Windows too.
set -euo pipefail
cd "$(dirname "$0")/.."

rid="win-x64"
self_contained=true
version=()
version_number="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"
format=""
while [ $# -gt 0 ]; do
    case "$1" in
        --framework-dependent) self_contained=false ;;
        --version) version=("-p:Version=$2"); version_number="$2"; shift ;;
        --zip) format="zip" ;;
        *) rid="$1" ;;
    esac
    shift
done
if [ -z "$format" ]; then
    case "$rid" in win-*) format="inno" ;; *) format="zip" ;; esac
fi

stage="artifacts/stage-$rid"
rm -rf "$stage"
mkdir -p "$stage"

echo "== Editor ($rid, self-contained: $self_contained, version $version_number)"
dotnet publish Editor/Menhir.Editor -c Release -r "$rid" --self-contained "$self_contained" \
    -p:RuntimeIdentifier="$rid" "${version[@]}" -o "$stage/Editor" -nologo -v q

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

if [ "$format" = "inno" ]; then
    # ISCC: the ISCC variable, Inno Setup 6's usual folders, or the PATH.
    if [ -n "${ISCC:-}" ]; then
        read -r -a iscc <<< "$ISCC"
    elif [ -x "/c/Program Files (x86)/Inno Setup 6/ISCC.exe" ]; then
        iscc=("/c/Program Files (x86)/Inno Setup 6/ISCC.exe")
    elif [ -x "/c/Program Files/Inno Setup 6/ISCC.exe" ]; then
        iscc=("/c/Program Files/Inno Setup 6/ISCC.exe")
    elif command -v iscc > /dev/null; then
        iscc=(iscc)
    else
        echo "Inno Setup's ISCC was not found: install Inno Setup 6, or set ISCC (or pass --zip)." >&2
        exit 1
    fi
    # ISCC wants Windows paths.
    winpath() {
        if command -v cygpath > /dev/null; then cygpath -w "$1"
        elif [ "${iscc[0]}" = "wine" ]; then winepath -w "$1" 2> /dev/null
        else echo "$1"; fi
    }
    echo "== Menhir-Setup-$rid.exe (Inno Setup)"
    rm -f "artifacts/Menhir-Setup-$rid.exe"
    # (Git Bash would turn the /switches into paths.)
    MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' "${iscc[@]}" /Q "/DAppVersion=$version_number" "/DStage=$(winpath "$(pwd)/$stage")" \
        "/DOutputDir=$(winpath "$(pwd)/artifacts")" "$(winpath "$(pwd)/Installer/Windows/Menhir.iss")"
    rm -rf "$stage"
    ls -lh "artifacts/Menhir-Setup-$rid.exe"
    exit 0
fi

out="artifacts/Menhir-Setup-$rid"
rm -rf "$out"
mkdir -p "$out"

echo "== Menhir.zip"
(cd "$stage" && rm -f "../../$out/Menhir.zip" && zip -qr -X "../../$out/Menhir.zip" .)

echo "== Installer"
dotnet publish Installer/Menhir.Setup -c Release -r "$rid" --self-contained true -p:PublishSingleFile=true \
    -p:PublishTrimmed=true -p:DebugType=none "${version[@]}" -o "$out" -nologo -v q

(cd artifacts && rm -f "Menhir-Setup-$rid.zip" && zip -qr -X "Menhir-Setup-$rid.zip" "Menhir-Setup-$rid")
rm -rf "$stage"
ls -lh "$out" "artifacts/Menhir-Setup-$rid.zip"
