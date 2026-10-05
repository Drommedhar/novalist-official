#!/bin/bash
# Build the Electron AppImage with the same backend and packaging configuration
# as the desktop release workflow. Usage: tools/build-appimage.sh [version]
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${REPO_ROOT}/app"
VERSION="${1:-$(node -p 'require("./package.json").version')}"

npm run build
dotnet publish ../Novalist.Backend/Novalist.Backend.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:VersionPrefix="${VERSION}" \
  -p:VersionSuffix= \
  -o dist-backend

# An explicit target prevents building the deb/rpm targets or publishing a
# release from a local packaging command. Preserve package.json's version.
npx --no-install electron-builder --config electron-builder.yml \
  --linux AppImage --x64 -p never --config.extraMetadata.version="${VERSION}"

echo "Built: ${REPO_ROOT}/app/dist/Novalist-x86_64.AppImage"
