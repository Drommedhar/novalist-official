#!/usr/bin/env bash
# Installs the current app on one dedicated screenshot simulator and pins its
# status bar to 9:41. Other simulators and their projects are left alone.
# Usage: sim-setup.sh <simulator-udid> [app-bundle]
set -euo pipefail
UDID="${1:?usage: sim-setup.sh <simulator-udid> [app-bundle]}"
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
APP="${2:-$REPO/Novalist.Mobile/bin/Debug/net10.0-ios27.0/iossimulator-arm64/Novalist.Mobile.app}"
[ -d "$APP" ] || { echo "Build the simulator app first: $APP" >&2; exit 1; }
APP_ID="$(/usr/libexec/PlistBuddy -c 'Print CFBundleIdentifier' "$APP/Info.plist")"

STATE="$(xcrun simctl list devices available -j | python3 -c '
import json, sys
matches = [d for devices in json.load(sys.stdin)["devices"].values() for d in devices if d["udid"] == sys.argv[1]]
if len(matches) != 1:
    raise SystemExit("Expected one available simulator UDID")
print(matches[0]["state"])
' "$UDID")"
[ "$STATE" = "Booted" ] || xcrun simctl boot "$UDID"
xcrun simctl bootstatus "$UDID" -b
# These dedicated captures use English system dates and the 9:41 clock format.
# SpringBoard needs a restart before changes to the native locale take effect.
xcrun simctl spawn "$UDID" defaults write NSGlobalDomain AppleLanguages -array en
xcrun simctl spawn "$UDID" defaults write NSGlobalDomain AppleLocale -string en_US
xcrun simctl shutdown "$UDID"
xcrun simctl boot "$UDID"
xcrun simctl bootstatus "$UDID" -b
xcrun simctl install "$UDID" "$APP"
xcrun simctl status_bar "$UDID" override \
  --time "9:41" --dataNetwork wifi --wifiMode active --wifiBars 3 \
  --cellularMode active --cellularBars 4 --batteryState charged --batteryLevel 100
xcrun simctl ui "$UDID" appearance dark
xcrun simctl launch "$UDID" "$APP_ID"
echo "Ready: $UDID ($APP_ID)"
