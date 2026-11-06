#!/bin/bash
set -euo pipefail
rid="$1"
archive="$2"
case "$rid" in
  iossimulator-arm64) arch=arm64; host=aarch64-apple-darwin ;;
  iossimulator-x64) arch=x86_64; host=x86_64-apple-darwin ;;
  *) echo "Unsupported simulator: $rid" >&2; exit 1 ;;
esac
mkdir -p "$(dirname "$archive")"
output="$(cd "$(dirname "$archive")" && pwd)"
if [ -f "$output/libsodium.a" ]; then exit 0; fi
cd "$output"
curl --fail --location --silent --show-error https://download.libsodium.org/libsodium/releases/libsodium-1.0.22.tar.gz -o source.tar.gz
echo 'adbdd8f16149e81ac6078a03aca6fc03b592b89ef7b5ed83841c086191be3349  source.tar.gz' | shasum -a 256 -c -
tar -xzf source.tar.gz
cd libsodium-1.0.22
export CC="$(xcrun --sdk iphonesimulator --find clang)"
export CFLAGS="-O2 -arch $arch -isysroot $(xcrun --sdk iphonesimulator --show-sdk-path) -mios-simulator-version-min=27.0"
export LDFLAGS="$CFLAGS"
./configure --host="$host" --disable-shared --enable-static --prefix="$output/install" > "$output/build.log" 2>&1
make -j "$(sysctl -n hw.ncpu)" install >> "$output/build.log" 2>&1
cp "$output/install/lib/libsodium.a" "$output/libsodium.a"
