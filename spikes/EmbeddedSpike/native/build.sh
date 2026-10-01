#!/usr/bin/env bash
# Builds libghostty-vt as a static library for linux-x64, at the commit pinned
# in pins.env, with the Zig version pinned there. Both downloads are checked
# against their SHA-256. Nothing is installed system-wide: Zig and the source
# land in native/.cache, the result in native/out/linux-x64.
#
# The zig invocation mirrors herdr's build.rs (Apache-2.0,
# github.com/ogulcancelik/herdr).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
. "$here/pins.env"

cache="$here/.cache"
out="$here/out/linux-x64"
mkdir -p "$cache" "$out"

pinned() {
    local url="$1" file="$2" sha="$3"
    if [ ! -f "$file" ] || ! echo "$sha  $file" | sha256sum -c --status; then
        echo "downloading $url"
        curl -fsSL "$url" -o "$file"
    fi
    echo "$sha  $file" | sha256sum -c --status || {
        echo "checksum mismatch for $file" >&2
        exit 1
    }
}

zig_name="zig-x86_64-linux-$ZIG_VERSION"
pinned "https://ziglang.org/download/$ZIG_VERSION/$zig_name.tar.xz" "$cache/$zig_name.tar.xz" "$ZIG_SHA256_LINUX_X64"
[ -x "$cache/$zig_name/zig" ] || tar -xJf "$cache/$zig_name.tar.xz" -C "$cache"
zig="$cache/$zig_name/zig"

src_tar="$cache/ghostty-$GHOSTTY_COMMIT.tar.gz"
src_dir="$cache/ghostty-$GHOSTTY_COMMIT"
pinned "https://github.com/ghostty-org/ghostty/archive/$GHOSTTY_COMMIT.tar.gz" "$src_tar" "$GHOSTTY_SHA256"
[ -f "$src_dir/build.zig" ] || tar -xzf "$src_tar" -C "$cache"

export ZIG_GLOBAL_CACHE_DIR="$cache/zig-global"
export ZIG_LOCAL_CACHE_DIR="$cache/zig-local"
mkdir -p "$ZIG_GLOBAL_CACHE_DIR/p"

(
    cd "$src_dir"
    "$zig" build \
        -Demit-lib-vt \
        -Doptimize=ReleaseFast \
        -Dsimd=true \
        -Dtarget=x86_64-linux-gnu \
        "-Dversion-string=$GHOSTTY_VERSION_STRING" \
        -Demit-xcframework=false \
        --prefix "$out"
)

lib="$out/lib/libghostty-vt.a"
[ -f "$lib" ] || { echo "expected $lib after the build" >&2; exit 1; }
echo "built $lib"
