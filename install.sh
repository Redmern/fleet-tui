#!/usr/bin/env sh
# Build fleet from source and install it. The Linux and macOS counterpart of
# install.ps1; for a machine without the .NET SDK use scripts/get-fleet.sh.
#
#   ./install.sh                build, install, run 'fleet setup'
#   ./install.sh --with-deps    install wezterm, neovim and a neovim config first
#   ./install.sh --deps-only    install only those dependencies
#   ./install.sh --uninstall    remove the binary (configuration is kept)
#
# --with-deps uses the package manager it can find. FLEET_NVIM_CONFIG picks the
# neovim config to clone; DEFAULT_NVIM_CONFIG below is used when it is unset.

set -eu

DEFAULT_NVIM_CONFIG="https://github.com/Redmern/nvim_0.12.git"
WITH_DEPS=0
DEPS_ONLY=0

REPO_ROOT="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
BIN_DIR="${FLEET_BIN_DIR:-$HOME/.local/bin}"
BIN="$BIN_DIR/fleet"

step() { printf '==> %s\n' "$1"; }
ok() { printf '    %s\n' "$1"; }

for arg in "$@"; do
    case "$arg" in
        --with-deps) WITH_DEPS=1 ;;
        --deps-only) DEPS_ONLY=1; WITH_DEPS=1 ;;
    esac
done

install_deps() {
    step 'Installing dependencies'

    if command -v pacman >/dev/null 2>&1; then
        sudo pacman -S --needed --noconfirm git neovim wezterm || ok 'pacman could not install everything'
    elif command -v dnf >/dev/null 2>&1; then
        sudo dnf install -y git neovim || ok 'dnf could not install everything'
        command -v wezterm >/dev/null 2>&1 || ok 'wezterm: see https://wezterm.org/installation'
    elif command -v apt-get >/dev/null 2>&1; then
        sudo apt-get update
        sudo apt-get install -y git neovim || ok 'apt could not install everything'
        # Debian and Ubuntu carry no wezterm package; it ships its own .deb.
        command -v wezterm >/dev/null 2>&1 || ok 'wezterm: see https://wezterm.org/installation'
    else
        ok 'no known package manager - install git, neovim and wezterm yourself'
    fi

    url="${FLEET_NVIM_CONFIG:-$DEFAULT_NVIM_CONFIG}"
    target="${XDG_CONFIG_HOME:-$HOME/.config}/nvim"

    step 'Neovim config'

    if [ -d "$target/.git" ]; then
        remote="$(git -C "$target" remote get-url origin 2>/dev/null || true)"

        if [ "$remote" = "$url" ]; then
            git -C "$target" pull --ff-only >/dev/null
            ok "updated $target"
        else
            ok "$target already holds a different config ($remote) - left alone"
        fi
    elif [ -d "$target" ] && [ -n "$(ls -A "$target" 2>/dev/null)" ]; then
        ok "$target exists and is not a git checkout - left alone"
    else
        git clone --depth 1 "$url" "$target" && ok "cloned $url into $target"
    fi

    if [ -f "$target/bootstrap.sh" ]; then
        ok "this config ships bootstrap.sh for extra tooling - run it yourself if you want it"
    fi
}

if [ "${1:-}" = "--uninstall" ]; then
    step 'Uninstalling'
    rm -f "$BIN"
    ok "removed $BIN"
    ok "configuration kept in ${XDG_CONFIG_HOME:-$HOME/.config}/fleet"
    exit 0
fi

if [ "$WITH_DEPS" = 1 ]; then
    install_deps
fi

if [ "$DEPS_ONLY" = 1 ]; then
    exit 0
fi

step 'Checking prerequisites'

if ! command -v dotnet >/dev/null 2>&1; then
    echo "fleet: the .NET SDK is required to build from source." >&2
    echo "       install it, or use scripts/get-fleet.sh for a published binary." >&2
    exit 1
fi

ok "dotnet $(dotnet --version)"

# NativeAOT links with clang and needs zlib headers. Say so before the linker does.
for tool in clang; do
    command -v "$tool" >/dev/null 2>&1 || ok "missing $tool - NativeAOT needs it (apt install clang zlib1g-dev)"
done

case "$(uname -s)" in
    Darwin)
        case "$(uname -m)" in
            arm64) RID=osx-arm64 ;;
            *) RID=osx-x64 ;;
        esac
        ;;
    *)
        case "$(uname -m)" in
            aarch64|arm64) RID=linux-arm64 ;;
            *) RID=linux-x64 ;;
        esac
        ;;
esac

ok "target $RID"

step 'Publishing (NativeAOT)'
OUT="$REPO_ROOT/out/$RID"
dotnet publish "$REPO_ROOT/src/Fleet/Fleet.csproj" -c Release -r "$RID" -o "$OUT" --nologo
ok "published to $OUT"

step 'Installing'
mkdir -p "$BIN_DIR"
pkill -x fleet 2>/dev/null && ok 'stopped a running fleet' || true
install -m 755 "$OUT/fleet" "$BIN"
ok "$BIN"

case ":$PATH:" in
    *":$BIN_DIR:"*) ok "already on PATH: $BIN_DIR" ;;
    *) ok "add to PATH: export PATH=\"$BIN_DIR:\$PATH\"" ;;
esac

step 'Setting up'
"$BIN" setup || true

echo
echo "fleet installed. Run 'fleet' to open a project."
