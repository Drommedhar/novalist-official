#!/bin/bash
# Local AppImage builder. Mirrors what .github/workflows/release.yml does on
# CI so you can test changes without cutting a tag.
#
# Usage: tools/build-appimage.sh [version]
#   version defaults to 1.13.99

set -euxo pipefail

VERSION="${1:-1.13.99}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="${REPO_ROOT}/dist/Novalist.AppImage.work"
PUBLISH_DIR="${OUT_DIR}/publish"
APPDIR="${OUT_DIR}/AppDir"
APPIMAGE_OUT="${REPO_ROOT}/dist/Novalist-x86_64.AppImage"

rm -rf "${OUT_DIR}"
mkdir -p "${PUBLISH_DIR}" "${APPDIR}/usr/bin" \
         "${APPDIR}/usr/share/applications" \
         "${APPDIR}/usr/share/icons/hicolor/256x256/apps"

dotnet publish "${REPO_ROOT}/Novalist.Desktop/Novalist.Desktop.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -p:VersionPrefix="${VERSION}" \
  -p:VersionSuffix= \
  -o "${PUBLISH_DIR}"

cp -a "${PUBLISH_DIR}/." "${APPDIR}/usr/bin/"
chmod +x "${APPDIR}/usr/bin/Novalist.Desktop"

convert "${REPO_ROOT}/Novalist.Desktop/novalist.ico[0]" -resize 256x256 \
  "PNG32:${APPDIR}/novalist.png"
cp "${APPDIR}/novalist.png" "${APPDIR}/usr/share/icons/hicolor/256x256/apps/novalist.png"

cat > "${APPDIR}/novalist.desktop" <<EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Novalist
Comment=Standalone novel planning and writing app
Exec=Novalist.Desktop
Icon=novalist
Terminal=false
Categories=Office;WordProcessor;
StartupNotify=true
EOF
cp "${APPDIR}/novalist.desktop" "${APPDIR}/usr/share/applications/"

cat > "${APPDIR}/AppRun" <<'EOF'
#!/bin/bash
HERE="$(dirname "$(readlink -f "${0}")")"
if [ -n "${DISPLAY:-}" ]; then
  export GDK_BACKEND=x11
fi
export WEBKIT_DISABLE_DMABUF_RENDERER=1
# Sandbox can't see the AppImage's FUSE mount — file:// loads fail silently.
export WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS=1
exec "${HERE}/usr/bin/Novalist.Desktop" "$@"
EOF
chmod +x "${APPDIR}/AppRun"

APPIMAGETOOL="${OUT_DIR}/appimagetool"
if [ ! -x "${APPIMAGETOOL}" ]; then
  wget -q -O "${APPIMAGETOOL}" \
    https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
  chmod +x "${APPIMAGETOOL}"
fi

ARCH=x86_64 "${APPIMAGETOOL}" --no-appstream "${APPDIR}" "${APPIMAGE_OUT}"

echo
echo "Built: ${APPIMAGE_OUT}"
echo "Run with verbose logging:"
echo "  NOVALIST_VERBOSE=1 ${APPIMAGE_OUT} 2>&1 | tee /tmp/nov.log"
