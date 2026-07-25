#!/usr/bin/env bash
# Generates the demo book's cover and banner: abstract bathymetric contour lines
# in the app's dark palette. No third-party assets, so the result is safe to ship
# in store screenshots and the manual.
set -euo pipefail
OUT="${1:?usage: make-art.sh <out-dir>}"
mkdir -p "$OUT"
DIDOT=/System/Library/Fonts/Supplemental/Didot.ttc
FUTURA=/System/Library/Fonts/Supplemental/Futura.ttc

# Deterministic contour field: heavily smoothed plasma posterised into bands,
# then edge-detected so only the band boundaries survive as thin depth contours.
# Written as a white-on-black mask; the caller tints and composites it.
contours() { # w h seed blur bands outfile
  local w=$1 h=$2 seed=$3 blur=$4 bands=$5 out=$6
  magick -size "${w}x${h}" -seed "$seed" plasma:fractal \
    -colorspace Gray -blur "0x${blur}" -normalize \
    -posterize "$bands" -edge 1 -threshold 20% -blur 0x0.5 \
    "$out"
}

# --- Cover: portrait, 1200x1800 ---------------------------------------------
contours 1200 1800 7 40 9 "$OUT/_c.png"
magick -size 1200x1800 gradient:'#16293f-#080d14' \
  \( "$OUT/_c.png" -alpha copy -fill '#5aa9dd' -colorize 100 -channel A -evaluate multiply 0.42 +channel \) \
  -compose over -composite \
  "$OUT/cover.png"

# Compass-rose ring and rules, drawn rather than filtered so they stay crisp.
magick "$OUT/cover.png" \
  -stroke '#9fd0ef' -strokewidth 2 -fill none \
  -draw "circle 600,700 600,470" \
  -strokewidth 1 \
  -draw "circle 600,700 600,540" \
  -draw "line 600,420 600,980" -draw "line 320,700 880,700" \
  -stroke none -fill '#eef5fb' \
  -font "$DIDOT" -pointsize 86 -interline-spacing 20 -gravity north \
  -annotate +0+1180 'THE\nCARTOGRAPHER’S\nDAUGHTER' \
  -fill '#87b9dc' -font "$FUTURA" -pointsize 32 -kerning 10 \
  -annotate +0+1600 'A NOVEL' \
  "$OUT/cover.png"

# --- Banner: wide, 2400x800 --------------------------------------------------
contours 2400 800 21 30 8 "$OUT/_b.png"
magick -size 2400x800 gradient:'#182c44-#090f18' \
  \( "$OUT/_b.png" -alpha copy -fill '#4e9ed4' -colorize 100 -channel A -evaluate multiply 0.38 +channel \) \
  -compose over -composite \
  "$OUT/banner.png"

rm -f "$OUT/_c.png" "$OUT/_b.png"
echo "wrote $OUT/cover.png and $OUT/banner.png"
