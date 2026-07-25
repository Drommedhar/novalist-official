#!/usr/bin/env bash
#
# Prepares an iOS simulator for screenshots: boots it, installs the app, and
# pins the status bar to Apple's marketing standard (9:41, full bars).
#
# The demo project cannot simply be dropped into the container: on iOS the app
# reaches a project folder through a security-scoped bookmark, and only the
# system document picker can mint one. So the flow is:
#
#   1. this script boots + installs + launches
#   2. the operator picks "On My iPhone" once in the document picker, which
#      stores a bookmark for that folder
#   3. sim-seed.sh copies the demo project INTO that bookmarked folder and
#      writes a recents entry pointing at it
#
# Step 3 works because SecurityScopedFolders.BeginAccess walks ancestors, so the
# bookmark on the parent authorises the project subfolder.
#
# Usage: sim-setup.sh "<device name>"
set -euo pipefail
DEVICE="${1:?usage: sim-setup.sh \"<device name>\"}"
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
APP="$REPO/Novalist.Mobile/bin/Debug/net10.0-ios/iossimulator-arm64/Novalist.Mobile.app"

UDID="$(xcrun simctl list devices available | grep -F "$DEVICE (" | head -1 | sed -E 's/.*\(([0-9A-F-]{36})\).*/\1/')"
[ -n "$UDID" ] || { echo "device not found: $DEVICE" >&2; exit 1; }
echo "device: $DEVICE ($UDID)"

# Shut every other device down so `booted` is unambiguous for the capture step.
xcrun simctl shutdown all >/dev/null 2>&1 || true
xcrun simctl boot "$UDID"
xcrun simctl bootstatus "$UDID" -b >/dev/null 2>&1 || true
open -a Simulator
sleep 4

xcrun simctl install "$UDID" "$APP"
xcrun simctl status_bar "$UDID" override \
  --time "9:41" --dataNetwork wifi --wifiMode active --wifiBars 3 \
  --cellularMode active --cellularBars 4 --batteryState charged --batteryLevel 100
xcrun simctl launch "$UDID" com.novalist.mobile
sleep 6
echo "$UDID"
