#!/usr/bin/env bash
#
# Tap the simulator at a screen coordinate, wait, then save both a clean device
# screenshot (the deliverable) and a capture of the Simulator window (used to
# work out where the next tap goes).
#
# Usage: tapshot.sh <x> <y> <out.png> [wait-seconds]
#        tapshot.sh - -  <out.png> [wait-seconds]   # capture only, no tap
set -euo pipefail
X="$1"; Y="$2"; OUT="$3"; WAIT="${4:-3}"
WIN="${NL_WIN_RECT:?set NL_WIN_RECT to x,y,w,h of the Simulator window}"
# Scratch capture of the Simulator window, used to pick the next tap target.
WORK="${NL_WORK:-$(cd "$(dirname "$0")" && pwd)}"

[ "$X" = "-" ] || cliclick "c:${X},${Y}"
sleep "$WAIT"
mkdir -p "$(dirname "$OUT")"
xcrun simctl io booted screenshot "$OUT" >/dev/null 2>&1
# simctl writes the framebuffer in the device's native orientation, so a
# landscape iPad comes out sideways. NL_ROTATE corrects it (-90 for landscape).
[ -z "${NL_ROTATE:-}" ] || magick "$OUT" -rotate "$NL_ROTATE" "$OUT"
screencapture -x -R"$WIN" "$WORK/win.png"
echo "saved $OUT"
