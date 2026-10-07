#!/usr/bin/env bash
# Compile the current working tree on a prepared Mac; device acceptance stays manual.
set -euo pipefail

if [[ "$(uname -s)" != Darwin ]]; then
  echo "Run this script on macOS with Xcode 27 and the repository's iOS workload." >&2
  exit 2
fi

report_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$report_root/../.." && pwd)"
for tool in dotnet node npm python3 xcrun; do
  if ! command -v "$tool" >/dev/null; then
    echo "Missing prerequisite: $tool" >&2
    exit 2
  fi
done

if ! node -e 'process.exit(typeof require("node:module").stripTypeScriptTypes === "function" ? 0 : 1)'; then
  echo "The renderer checks need Node's TypeScript stripping API; use current Node 22 LTS (22.13+) or 24+." >&2
  exit 2
fi

ios_sdk="$(xcrun --sdk iphoneos --show-sdk-version)"
if [[ "$ios_sdk" != 27.0 ]]; then
  echo "The current project targets iOS 27.0; selected Xcode reports $ios_sdk." >&2
  exit 2
fi
if ! dotnet workload list | grep -Eq 'maui-ios|(^|[[:space:]])ios([[:space:]]|$)'; then
  echo "Install the matching workload first: dotnet workload install maui-ios --version 10.0.401.1" >&2
  exit 2
fi

run_dir="$(mktemp -d "$report_root/native-run.XXXXXX")"
echo "Verification logs: $run_dir"
run_check() {
  local name="$1"
  shift
  echo "Running $name"
  if "$@" >"$run_dir/$name.log" 2>&1; then
    printf '%s\tpassed\n' "$name" >>"$run_dir/checks.tsv"
  else
    local result=$?
    printf '%s\tfailed (%s)\n' "$name" "$result" >>"$run_dir/checks.tsv"
    tail -n 60 "$run_dir/$name.log" >&2
    echo "Stopped at $name. Logs remain in $run_dir." >&2
    return "$result"
  fi
}

cd "$repo_root"
run_check dotnet-info dotnet --info
run_check apple-build-regressions python3 -m unittest discover -s tools -p test_apple_speech_build.py
cd app
run_check install-web-dependencies npm ci --no-audit
run_check typecheck npm run typecheck
run_check transport npm run test:transport
run_check mobile-renderer npm run build:mobile
cd "$repo_root"
run_check source-gates python3 tools/check-source.py

rid=iossimulator-arm64
[[ "$(uname -m)" != x86_64 ]] || rid=iossimulator-x64
run_check ios-simulator dotnet build Novalist.Mobile/Novalist.Mobile.csproj \
  -f net10.0-ios27.0 -p:RuntimeIdentifier="$rid" -p:EnableCodeSigning=false -m:1 -nr:false
run_check ios-speech-archive bash native/AppleSpeech/build.sh ios arm64 "$run_dir/libNovalistSpeech.a"

echo "Native compile checks passed. See $run_dir/checks.tsv."
echo "Run the device scenarios in implementation.html before marking native findings verified."
