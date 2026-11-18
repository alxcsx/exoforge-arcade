#!/usr/bin/env bash
# Places an Exoforge SDK tarball in every arcade project and points them at it.
#   tools/sync-sdk.sh /path/to/com.exoforge.sdk-<version>.tgz
# Build one from the exoforge checkout with `just pack-unity` (dist/com.exoforge.sdk-<version>.tgz).
# Copies the tarball into each <project>/Packages/, rewrites the manifest's com.exoforge.sdk entry,
# stages the extension SDK package in extensions/.feed and moves the extensions' reference to it.
# The tarball and the feed are git-ignored.
set -euo pipefail
[ $# -eq 1 ] && [ -f "$1" ] || { echo "usage: tools/sync-sdk.sh <path-to-com.exoforge.sdk-<version>.tgz>" >&2; exit 2; }
repo="$(cd "$(dirname "$0")/.." && pwd)"
tgz="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
name="$(basename "$tgz")"
version="${name#com.exoforge.sdk-}"; version="${version%.tgz}"

for manifest in "$repo"/*/Packages/manifest.json; do
  pkgs="$(dirname "$manifest")"
  rm -f "$pkgs"/com.exoforge.sdk-*.tgz
  cp "$tgz" "$pkgs/$name"
  python3 -I - "$manifest" "$name" <<'PY'
import json, sys
path, name = sys.argv[1:]
m = json.load(open(path))
m["dependencies"]["com.exoforge.sdk"] = "file:" + name
open(path, "w").write(json.dumps(m, indent=2) + "\n")
PY
  echo "[sync-sdk] ${pkgs#$repo/}: $name"
done

# The extensions restore Exoforge.Plugin.SDK from extensions/.feed (see extensions/nuget.config).
feed="$repo/extensions/.feed"
mkdir -p "$feed"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
tar -xzf "$tgz" -C "$tmp" --include='package/Editor/Plugins/NuGet~/*.nupkg' 2>/dev/null || tar -xzf "$tgz" -C "$tmp"
cp "$tmp"/package/Editor/Plugins/NuGet~/*.nupkg "$feed/"
for csproj in "$repo"/extensions/*/src/*.csproj "$repo"/extensions/*.Tests/*.csproj; do
  sed -E -i.bak "s|(Include=\"Exoforge\.Plugin\.SDK\" Version=\")[^\"]*|\1$version|" "$csproj" && rm -f "$csproj.bak"
done
echo "[sync-sdk] extensions/.feed and extension references: Exoforge.Plugin.SDK $version"
