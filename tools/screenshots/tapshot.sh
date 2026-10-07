#!/usr/bin/env bash
#
# Tap the simulator at a screen coordinate, wait, then save both a clean device
# screenshot (the deliverable) and a capture of the Simulator window (used to
# work out where the next tap goes).
#
# Usage: tapshot.sh <x> <y> <out.png> [wait-seconds]
#        tapshot.sh - -  <out.png> [wait-seconds]   # capture only, no tap
# Set NL_SIMULATOR_UDID to the dedicated screenshot simulator.
set -euo pipefail
X="$1"; Y="$2"; OUT="$3"; WAIT="${4:-3}"
WIN="${NL_WIN_RECT:?set NL_WIN_RECT to x,y,w,h of the Simulator window}"
UDID="${NL_SIMULATOR_UDID:?set NL_SIMULATOR_UDID to the screenshot simulator}"
# Scratch capture of the Simulator window, used to pick the next tap target.
WORK="${NL_WORK:-$(cd "$(dirname "$0")" && pwd)}"

[ "$X" = "-" ] || cliclick "c:${X},${Y}"
sleep "$WAIT"
mkdir -p "$(dirname "$OUT")"
xcrun simctl io "$UDID" screenshot "$OUT" >/dev/null 2>&1
# simctl writes the framebuffer in the device's native orientation, so a
# landscape iPad comes out sideways. NL_ROTATE corrects it (-90 for landscape).
[ -z "${NL_ROTATE:-}" ] || magick "$OUT" -rotate "$NL_ROTATE" "$OUT"
screencapture -x -R"$WIN" "$WORK/win.png"
echo "saved $OUT"
