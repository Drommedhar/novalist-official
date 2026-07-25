#!/usr/bin/env bash
#
# Wraps one app screenshot in an App Store marketing frame: a caption over a
# dark gradient drawn from the app's own palette, with the screenshot inset,
# rounded and shadowed.
#
# Output is written at exactly the size Apple expects for the target device, so
# the result can be uploaded without further processing.
#
# Usage: frame.sh <src.png> <out.png> <canvas-w> <canvas-h> <headline> [subline]
set -euo pipefail
SRC="$1"; OUT="$2"; CW="$3"; CH="$4"; HEAD="$5"; SUB="${6:-}"

SANS=/System/Library/Fonts/HelveticaNeue.ttc
ACCENT='#7cc0f0'
INK='#f2f6fa'

py() { python3 -c "print(int($1))"; }

# Portrait frames are narrow, so their type has to be sized off the width or it
# runs off the canvas; landscape frames have width to spare.
if [ "$CH" -gt "$CW" ]; then
  BAND=$(py "$CH * 0.135"); HEAD_PT=$(py "$CW * 0.050"); SUB_PT=$(py "$CW * 0.028")
else
  BAND=$(py "$CH * 0.155"); HEAD_PT=$(py "$CW * 0.024"); SUB_PT=$(py "$CW * 0.0135")
fi
PAD=$(py "$CW * 0.055")
RADIUS=$(py "[$CW,$CH][$CW>$CH] * 0.018")
ART_W=$(py "$CW - 2*$PAD")
ART_H=$(py "$CH - $BAND - $PAD")

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# DstIn multiplies the artwork's alpha by the mask's, which is what actually
# rounds the corners; copyopacity on an -alpha off source silently does nothing
# and leaves a square image whose shadow then swallows it.
magick "$SRC" -resize "${ART_W}x${ART_H}" "$TMP/fit.png"
read -r fw fh < <(magick identify -format '%w %h\n' "$TMP/fit.png")
magick -size "${fw}x${fh}" xc:none -fill white \
  -draw "roundrectangle 0,0 $((fw-1)),$((fh-1)) $RADIUS,$RADIUS" "$TMP/mask.png"
magick "$TMP/fit.png" -alpha set "$TMP/mask.png" -compose DstIn -composite "$TMP/round.png"
magick "$TMP/round.png" \( +clone -background black -shadow 60x"$RADIUS"+0+"$((RADIUS/2))" \) \
  +swap -background none -layers merge +repage "$TMP/shadow.png"

# Backdrop: a vertical gradient, lit slightly at the top so the caption sits on
# the brighter end and the artwork on the darker one. A radial "glow" is
# deliberately avoided here - screening an opaque radial-gradient over this
# swamps the whole canvas rather than tinting it.
magick -size "${CW}x${CH}" gradient:'#20304e-#070a10' "$TMP/bg.png"

# caption: wraps to the given width, so a long headline becomes two lines
# instead of running off the edge.
magick -background none -fill "$INK" -font "$SANS" -pointsize "$HEAD_PT" \
  -size "${ART_W}x" -gravity center caption:"$HEAD" "$TMP/head.png"
read -r _ hh < <(magick identify -format '%w %h\n' "$TMP/head.png")

magick "$TMP/bg.png" "$TMP/head.png" \
  -gravity north -geometry "+0+$(py "$BAND * 0.20")" -compose over -composite "$TMP/cap.png"

if [ -n "$SUB" ]; then
  magick -background none -fill "$ACCENT" -font "$SANS" -pointsize "$SUB_PT" \
    -size "${ART_W}x" -gravity center caption:"$SUB" "$TMP/sub.png"
  magick "$TMP/cap.png" "$TMP/sub.png" \
    -gravity north -geometry "+0+$(py "$BAND * 0.20 + $hh + $SUB_PT * 0.5")" \
    -compose over -composite "$TMP/cap.png"
fi

magick "$TMP/cap.png" "$TMP/shadow.png" \
  -gravity north -geometry "+0+$BAND" -compose over -composite \
  -resize "${CW}x${CH}!" -strip -define png:color-type=2 "$OUT"
echo "  framed $(basename "$OUT")"
