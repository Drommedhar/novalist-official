#!/usr/bin/env bash
# Seeds the generated demo into a dedicated simulator's app-owned Documents.
# Current Novalist supports local projects without a document-picker grant.
# Existing projects and settings are preserved; an existing destination aborts.
# Usage: sim-seed.sh <simulator-udid> <demo-project-dir>
set -euo pipefail
UDID="${1:?usage: sim-seed.sh <simulator-udid> <demo-project-dir>}"
PROJECT_SRC="${2:?usage: sim-seed.sh <simulator-udid> <demo-project-dir>}"
APP_ID=com.novalist.app
CONTAINER="$(xcrun simctl get_app_container "$UDID" "$APP_ID" data)"
xcrun simctl terminate "$UDID" "$APP_ID" >/dev/null 2>&1 || true

python3 - "$CONTAINER" "$PROJECT_SRC" <<'PY'
import datetime
import json
from pathlib import Path
import shutil
import sys

container, source = map(Path, sys.argv[1:])
metadata = json.loads((source / ".novalist/project.json").read_text())
destination = container / "Documents" / source.name
if destination.exists():
    raise SystemExit("Demo destination already exists; use a fresh screenshot simulator.")
settings_path = container / "Library/settings.json"
settings = json.loads(settings_path.read_text()) if settings_path.exists() else {}
if settings_path.exists():
    backup = settings_path.with_name("settings.before-screenshots.json")
    if not backup.exists():
        shutil.copy2(settings_path, backup)
shutil.copytree(source, destination)
book = next(book for book in metadata["books"] if book["id"] == metadata["activeBookId"])
cover = book.get("coverImage", "")
recent = {
    "projectId": metadata["id"],
    "name": metadata["name"],
    "path": str(destination),
    "lastOpened": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    "coverImagePath": str(destination / book["folderName"] / cover) if cover else "",
}
settings.update(language="en", theme="dark")
settings["recentProjects"] = [recent, *settings.get("recentProjects", [])]
settings_path.parent.mkdir(parents=True, exist_ok=True)
settings_path.write_text(json.dumps(settings, indent=2) + "\n")
print("Seeded fictional demo project; prior settings retained.")
PY
xcrun simctl launch "$UDID" "$APP_ID"
