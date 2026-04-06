#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec_path="${script_dir}/Novalist.Desktop"
icon_source="${script_dir}/novalist.png"
template_path="${script_dir}/novalist.desktop.template"
data_home="${XDG_DATA_HOME:-${HOME}/.local/share}"
applications_dir="${data_home}/applications"
icons_dir="${data_home}/icons/hicolor/256x256/apps"
desktop_file="${applications_dir}/novalist.desktop"
icon_file="${icons_dir}/novalist.png"

if [[ ! -x "${exec_path}" ]]; then
  echo "Expected executable at ${exec_path}" >&2
  exit 1
fi

mkdir -p "${applications_dir}" "${icons_dir}"
install -m 0644 "${icon_source}" "${icon_file}"
sed \
  -e "s|__EXEC_PATH__|${exec_path}|g" \
  -e "s|__ICON_PATH__|${icon_file}|g" \
  "${template_path}" > "${desktop_file}"
chmod 0644 "${desktop_file}"

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "${applications_dir}" >/dev/null 2>&1 || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache "${data_home}/icons/hicolor" >/dev/null 2>&1 || true
fi

echo "Installed Novalist desktop entry to ${desktop_file}"