#!/usr/bin/env bash
# Release one mod from the collection:  scripts/release.sh <ModFolder> [--dry-run]
# Builds against your game install (the game's interop assemblies can't be put in CI), checks the version,
# tags "<mod-slug>-v<version>", pushes, and publishes a GitHub release with the DLL and a zip attached.
set -euo pipefail
cd "$(dirname "$0")/.."

mod="${1:?usage: scripts/release.sh <ModFolder> [--dry-run]}"; dry="${2:-}"
[ -f "$mod/$mod.csproj" ] || { echo "no such mod: $mod (expected $mod/$mod.csproj)"; exit 1; }
: "${S1_GAME_DIR:?set S1_GAME_DIR to the Schedule I folder (MelonLoader installed and run once)}"

# Name and version come from the mod's own MelonInfo line, the one source of truth.
info=$(grep -rhoE 'MelonInfo\(typeof\([^)]*\), *"[^"]+", *"[^"]+"' "$mod" --include=*.cs | head -1)
[ -n "$info" ] || { echo "no MelonInfo found in $mod"; exit 1; }
name=$(echo "$info" | sed -E 's/.*\), *"([^"]+)", *"([^"]+)"$/\1/')
version=$(echo "$info" | sed -E 's/.*\), *"([^"]+)", *"([^"]+)"$/\2/')
slug=$(echo "$name" | tr '[:upper:] ' '[:lower:]-')
tag="$slug-v$version"
echo "Releasing $name $version (tag $tag)"

[ -z "$(git status --porcelain)" ] || { echo "working tree is not clean - commit first"; exit 1; }
[ "$(git rev-parse --abbrev-ref HEAD)" = main ] || { echo "release from main"; exit 1; }
git fetch -q origin
[ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ] || { echo "main is not in sync with origin - push or pull first"; exit 1; }
if git rev-parse -q --verify "refs/tags/$tag" >/dev/null || git ls-remote --tags origin "$tag" | grep -q .; then
  echo "tag $tag already exists - bump the version in MelonInfo first"; exit 1
fi

out=$(mktemp -d)
dotnet build "$mod/$mod.csproj" -c Release -p:GameDir="$S1_GAME_DIR" -o "$out" --nologo -v q --no-incremental
dll="$out/$mod.dll"; [ -f "$dll" ] || { echo "build produced no $mod.dll"; exit 1; }
zip="$out/$mod-$version.zip"
( cd "$out" && python3 -c "import zipfile,sys;z=zipfile.ZipFile(sys.argv[1],'w',zipfile.ZIP_DEFLATED);z.write(sys.argv[2],'Mods/'+sys.argv[2]);z.close()" "$zip" "$mod.dll" )
echo "built $dll and $zip"

if [ "$dry" = "--dry-run" ]; then echo "dry run: stopping before tagging ($out)"; exit 0; fi

notes="$name $version. Needs MelonLoader 0.7.x. Put $mod.dll in the game's Mods folder, or unzip $mod-$version.zip into the game folder. See the README for details."
git tag -a "$tag" -m "$name $version"
git push origin "$tag"
gh release create "$tag" "$dll" "$zip" --title "$name $version" --notes "$notes" --verify-tag
echo "released: $(gh release view "$tag" --json url -q .url)"
