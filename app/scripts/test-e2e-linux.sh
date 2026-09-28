#!/usr/bin/env bash
set -euo pipefail

# Xvfb supplies a display, but native maximize/fullscreen transitions also need
# a window manager. Keep it on the test display and stop it when Playwright exits.
openbox --sm-disable &
window_manager_pid=$!
trap 'kill "$window_manager_pid" 2>/dev/null || true' EXIT

for attempt in {1..100}; do
  if wmctrl -m >/dev/null 2>&1; then
    npx playwright test "$@"
    exit 0
  fi
  kill -0 "$window_manager_pid"
  sleep 0.1
done

echo 'The E2E window manager did not become ready.' >&2
exit 1
