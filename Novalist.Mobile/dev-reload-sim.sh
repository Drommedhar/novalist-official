#!/usr/bin/env bash
#
# Fast renderer iteration on the iOS simulator WITHOUT a MAUI/dotnet rebuild.
# Rebuilds only the web bundle (~1s) and hot-swaps it into the already-installed
# app, then relaunches. Use this after any renderer / TypeScript change.
#
# A change to the C# bridge (Novalist.Mobile/*.cs) still needs a real
# `dotnet build ... -f net10.0-ios` + reinstall - this script only refreshes the
# web layer.
#
# Prereq: the app has been installed on a BOOTED simulator at least once via the
# normal build (see docs/mobile-phase0-runbook.md).
#
set -euo pipefail

# Must match <ApplicationId> in Novalist.Mobile.csproj. It is deliberately the
# desktop's id so iOS ships as a platform of the same App Store record; pointing
# this at the old com.novalist.mobile silently reloaded whichever stale build of
# that id was still installed, and the app under test never changed.
BUNDLE_ID=com.novalist.app
HERE="$(cd "$(dirname "$0")" && pwd)"

echo "[1/3] building renderer bundle..."
( cd "$HERE/../app" && npm run build:mobile >/dev/null )

APP="$(xcrun simctl get_app_container booted "$BUNDLE_ID" app)"
echo "[2/3] hot-swapping bundle into: $APP/app"
rm -rf "$APP/app"
cp -R "$HERE/Resources/Raw/app" "$APP/app"

echo "[3/3] relaunching..."
xcrun simctl terminate booted "$BUNDLE_ID" >/dev/null 2>&1 || true
xcrun simctl launch booted "$BUNDLE_ID"
echo "done."
