#!/usr/bin/env bash
#
# Copies the demo project into the folder the operator just authorised in the
# document picker, and seeds a recents entry pointing at it, so relaunching the
# app puts the project one tap away on the welcome screen.
#
# Run AFTER sim-setup.sh and after picking "On My iPhone" in the app's
# "Browse for Project Folder..." picker (which is what writes the bookmark).
#
# Usage: sim-seed.sh <udid> <demo-project-dir>
set -euo pipefail
UDID="${1:?usage: sim-seed.sh <udid> <demo-project-dir>}"
PROJECT_SRC="${2:?usage: sim-seed.sh <udid> <demo-project-dir>}"
BOOK='The Cartographer’s Daughter'
NAME="$(basename "$PROJECT_SRC")"

CONTAINER="$(xcrun simctl get_app_container "$UDID" com.novalist.mobile data)"
BOOKMARKS="$CONTAINER/Library/security-bookmarks.json"
[ -f "$BOOKMARKS" ] || { echo "no bookmark yet - pick a folder in the app first" >&2; exit 1; }

# A device may carry bookmarks from earlier sessions. Take the most ancestral
# one: it is the container folder we want to drop the demo project into, and its
# scope also covers everything beneath it.
PARENT="$(python3 -c 'import json,sys; print(min(json.load(open(sys.argv[1])), key=len))' "$BOOKMARKS")"
echo "authorised folder: $PARENT"

DEST="$PARENT/$NAME"
rm -rf "$DEST"
cp -R "$PROJECT_SRC" "$PARENT/"
echo "copied project to: $DEST"

python3 - "$CONTAINER/Library/settings.json" "$DEST" "$BOOK" "$NAME" <<'EOF'
import json, os, sys
out, proj, book, name = sys.argv[1:5]
json.dump({
    "language": "en",
    "theme": "system",
    "recentProjects": [{
        "name": name,
        "path": proj,
        "lastOpened": "2026-07-26T09:41:00Z",
        "coverImagePath": os.path.join(proj, book, "Images", "cover.png"),
    }],
}, open(out, "w"), indent=2)
EOF
echo "seeded recents"

xcrun simctl terminate "$UDID" com.novalist.mobile >/dev/null 2>&1 || true
sleep 1
xcrun simctl launch "$UDID" com.novalist.mobile
sleep 6
echo "relaunched"
