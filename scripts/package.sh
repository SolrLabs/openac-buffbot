#!/usr/bin/env bash
# Build BuffBot in Release and package it as a launcher-ready zip: plugin.json at the zip root,
# only the extensions the launcher allows, plus <id>-<version>.zip.sha256 so the same output can
# back a GitHub release.
#
#   scripts/package.sh [outdir]
#
# Needs a sibling OpenAC checkout, or AcDreamSource pointing at one (see README).
set -euo pipefail

here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
dist=${1:-$here/artifacts}
src=$here/src/SolrLabs.BuffBot
out=$src/bin/Release/net10.0
stage=$dist/stage
dotnet=${DOTNET:-dotnet}

build_args=("$here/BuffBot.slnx" -c Release --nologo)
[ -n "${AcDreamSource:-}" ] && build_args+=("-p:AcDreamSource=$AcDreamSource")
"$dotnet" build "${build_args[@]}"

read -r id version < <(python3 - "$out/plugin.json" <<'PY'
import json, sys
m = json.load(open(sys.argv[1]))
print(m["id"], m["version"])
PY
)
name=$id-$version

rm -rf "$stage"
mkdir -p "$stage"
cp "$out/plugin.json" "$out/SolrLabs.BuffBot.dll" "$out/SolrLabs.BuffBot.deps.json" \
   "$out/SolrLabs.BuffBot.pdb" "$stage/"
cp "$out"/buffbot*.xml "$stage/"

# The icon is optional, but a bad one refuses the whole plugin: exactly 64x64 PNG, at most 64 KiB.
if [ -f "$src/icon.png" ]; then
  python3 - "$src/icon.png" <<'PY'
import struct, sys
path = sys.argv[1]
raw = open(path, "rb").read()
if not raw.startswith(b"\x89PNG\r\n\x1a\n"):
    sys.exit(f"{path} is not a PNG")
width, height = struct.unpack(">II", raw[16:24])
if (width, height) != (64, 64):
    sys.exit(f"{path} is {width}x{height}, must be 64x64")
if len(raw) > 65536:
    sys.exit(f"{path} is {len(raw)} bytes, max 65536")
PY
  cp "$src/icon.png" "$src/THIRD-PARTY-NOTICES.txt" "$stage/"
fi

# Mirror the launcher's content policy, so a refusal shows up here and not on its Plugins tab.
if find "$stage" -name runtimes -print -quit | grep -q .; then
  echo "a runtimes folder is refused" >&2; exit 1
fi
if find "$stage" -name AcDream.Plugin.Abstractions.dll -print -quit | grep -q .; then
  echo "Abstractions must not ship" >&2; exit 1
fi
bad=$(find "$stage" -type f ! \( -iname '*.dll' -o -iname '*.pdb' -o -iname '*.json' -o -iname '*.xml' \
  -o -iname '*.txt' -o -iname '*.md' -o -iname '*.png' -o -iname '*.jpg' -o -iname '*.jpeg' \
  -o -iname '*.ttf' -o -iname '*.otf' \))
if [ -n "$bad" ]; then
  printf 'disallowed files:\n%s\n' "$bad" >&2; exit 1
fi

rm -f "$dist/$name.zip" "$dist/$name.zip.sha256"
(cd "$stage" && zip -qX -r "$dist/$name.zip" .)
(cd "$dist" && shasum -a 256 "$name.zip" > "$name.zip.sha256")

# The launcher reads plugin.json and icon.png from the release itself, before it fetches the zip,
# and refuses an install when either differs from the zip's own copy.
cp "$stage/plugin.json" "$dist/plugin.json"
[ -f "$stage/icon.png" ] && cp "$stage/icon.png" "$dist/icon.png"
rm -rf "$stage"

echo "packaged: $dist/$name.zip, with plugin.json and icon.png beside it"
unzip -l "$dist/$name.zip"
cat "$dist/$name.zip.sha256"
