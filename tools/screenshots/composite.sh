#!/usr/bin/env bash
#
# Fixes the Liquid Glass transparency in a raw capture.
#
# On macOS 26 the app runs with `transparent: true` (see app/src/main/glass.ts),
# so the window chrome is painted at 0.6-0.72 alpha and expects the native
# NSGlassEffectView behind it. Playwright captures only the web layer, which is
# why the raw PNGs come out with alpha down to 0.6 and read as washed out.
#
# Flattening each capture onto a neutral desktop backdrop restores exactly what
# the user sees when the window sits on a plain wallpaper: the chrome stays
# translucent, but it now has something to be translucent against.
#
# Usage: composite.sh <in-dir> <out-dir> [width] [height]
set -euo pipefail
IN="${1:?usage: composite.sh <in-dir> <out-dir> [w] [h]}"
OUT="${2:?usage: composite.sh <in-dir> <out-dir> [w] [h]}"
W="${3:-}"
H="${4:-}"
mkdir -p "$OUT"

# Neutral, slightly cool desktop. Dark enough that the UI keeps its contrast,
# light enough that the translucent chrome reads as glass rather than as a flat
# opaque panel.
backdrop() { # w h out
  magick -size "${1}x${2}" gradient:'#2f3540-#14161b' \
    -blur 0x"$(( ${1} / 100 ))" "$3"
}

for src in "$IN"/*.png; do
  name="$(basename "$src")"
  # identify emits no trailing newline, so read exits non-zero on a full line.
  read -r w h < <(magick identify -format '%w %h\n' "$src")
  bg="$(mktemp -t nlbg).png"
  backdrop "$w" "$h" "$bg"
  if [ -n "$W" ]; then
    magick "$bg" "$src" -compose over -composite -resize "${W}x${H}!" \
      -strip -define png:color-type=2 "$OUT/$name"
  else
    magick "$bg" "$src" -compose over -composite \
      -strip -define png:color-type=2 "$OUT/$name"
  fi
  rm -f "$bg"
  echo "  $name"
done
echo "composited $(ls -1 "$OUT" | wc -l | tr -d ' ') images into $OUT"
