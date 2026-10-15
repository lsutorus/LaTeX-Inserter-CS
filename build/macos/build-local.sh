#!/usr/bin/env bash
# Builds an unsigned .app bundle for local development.
# Not a release artifact — see .github/workflows/release.yml for the signed path.
set -euo pipefail

# Default to the host architecture: Apple Silicon -> osx-arm64, Intel -> osx-x64.
# Pass a RID explicitly as $1 to cross-publish.
RID="${1:-$([ "$(uname -m)" = "arm64" ] && echo osx-arm64 || echo osx-x64)}"
VERSION="$(grep -o '<Version>[^<]*</Version>' src/LaTeXInserter/LaTeXInserter.csproj \
           | sed 's/<[^>]*>//g')"
PUBLISH_DIR="artifacts/publish-$RID"
APP="artifacts/LaTeX Inserter.app"

echo "==> Publishing $RID (Native AOT), version $VERSION"
rm -rf "$PUBLISH_DIR" "$APP"
dotnet publish src/LaTeXInserter -c Release -r "$RID" --self-contained -o "$PUBLISH_DIR"

echo "==> Generating icon"
./build/macos/make-icns.sh

echo "==> Assembling bundle"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH_DIR"/* "$APP/Contents/MacOS/"
cp build/macos/LaTeXInserter.icns "$APP/Contents/Resources/"
sed "s/__VERSION__/$VERSION/g" build/macos/Info.plist > "$APP/Contents/Info.plist"

echo "==> Ad-hoc signing (development only)"
codesign --force --deep --sign - "$APP"

echo "==> Built: $APP"
echo "    Run with: open '$APP'"
echo "    Note: TCC permissions reset on every rebuild because the ad-hoc"
echo "          signature changes. Re-grant Accessibility + Input Monitoring."
