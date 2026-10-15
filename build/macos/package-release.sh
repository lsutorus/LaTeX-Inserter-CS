#!/usr/bin/env bash
# Packages macOS release zips (.app bundles inside zip) for osx-arm64 and osx-x64.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"

cd "$ROOT_DIR"

VERSION="$(grep -o '<Version>[^<]*</Version>' src/LaTeXInserter/LaTeXInserter.csproj | sed 's/<[^>]*>//g')"
echo "==> Packaging macOS release for LaTeX Inserter v$VERSION"

./build/macos/make-icns.sh

mkdir -p artifacts

for RID in osx-arm64 osx-x64; do
  ARCH="${RID#osx-}"
  echo "==> Publishing $RID..."
  PUBLISH_DIR="artifacts/publish-$RID"
  APP="artifacts/$RID/LaTeX Inserter.app"

  rm -rf "$PUBLISH_DIR" "artifacts/$RID"
  dotnet publish src/LaTeXInserter -c Release -r "$RID" --self-contained -o "$PUBLISH_DIR"

  echo "==> Assembling bundle for $RID..."
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
  cp -R "$PUBLISH_DIR"/* "$APP/Contents/MacOS/"
  chmod +x "$APP/Contents/MacOS/LaTeXInserter"
  cp build/macos/LaTeXInserter.icns "$APP/Contents/Resources/"
  sed "s/__VERSION__/$VERSION/g" build/macos/Info.plist > "$APP/Contents/Info.plist"

  echo "==> Ad-hoc signing $RID bundle..."
  codesign --force --deep --sign - "$APP"

  ZIP_NAME="artifacts/LaTeXInserter-mac-$ARCH.zip"
  echo "==> Creating $ZIP_NAME..."
  rm -f "$ZIP_NAME"
  (cd "artifacts/$RID" && ditto -c -k --keepParent "LaTeX Inserter.app" "../../$ZIP_NAME")

  echo "==> Generating SHA256 checksum for $ZIP_NAME..."
  (cd artifacts && shasum -a 256 "LaTeXInserter-mac-$ARCH.zip" > "LaTeXInserter-mac-$ARCH.zip.sha256")
done

echo "==> Done! macOS release packages:"
ls -lh artifacts/LaTeXInserter-mac-*
