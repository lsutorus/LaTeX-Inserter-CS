#!/usr/bin/env bash
# Generates LaTeXInserter.icns from the 1024px source PNG.
# macOS only — uses sips and iconutil.
set -euo pipefail

SRC="${1:-src/LaTeXInserter/Assets/LaTeX-Inserter-icon-final.png}"
OUT="${2:-build/macos/LaTeXInserter.icns}"

WORK="$(mktemp -d)/LaTeXInserter.iconset"
mkdir -p "$WORK"

for size in 16 32 128 256 512; do
  sips -z $size $size        "$SRC" --out "$WORK/icon_${size}x${size}.png"        >/dev/null
  sips -z $((size*2)) $((size*2)) "$SRC" --out "$WORK/icon_${size}x${size}@2x.png" >/dev/null
done

mkdir -p "$(dirname "$OUT")"
iconutil --convert icns "$WORK" --output "$OUT"
echo "Wrote $OUT"
